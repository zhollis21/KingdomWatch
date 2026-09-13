using System;
using KingdomWatch.Core.Data;

namespace KingdomWatch.Core.Clock
{
    /// <summary>
    /// The world clock and its event scheduler. Owns simulation time, the
    /// pending events, and the order they are dispatched in.
    /// </summary>
    /// <remarks>
    /// Running centuries at 10,000x rules out iterating every tick, so the
    /// clock jumps: <see cref="AdvanceTo"/> runs every event due on or before a
    /// target and then lands on the target. Section 4 writes that as
    /// min(next discrete event, next threshold crossing, requested target
    /// time); here it is min(queue head, target), because a predicted threshold
    /// crossing and a discrete event are both "wake me at T" and differ only in
    /// where they came from. One queue, and thresholds are ordinary scheduled
    /// events - see <see cref="ScheduledEventKind"/>.
    ///
    /// What a threshold needs beyond scheduling is re-prediction: Aldric eats,
    /// so the hunger crossing he was booked for is wrong. <see cref="Cancel"/>
    /// drops the stale entry and the system schedules a new one. That, rather
    /// than a second queue, is what makes compression trustworthy.
    ///
    /// **Reactions flow forward, never backward or inward.** Section 4 requires
    /// that events emitted while handling another event are queued rather than
    /// executed recursively, and that ordering never depends on subscriber
    /// order. Both are enforced here rather than documented and hoped for:
    /// <see cref="AdvanceTo"/> refuses to run inside itself, and
    /// <see cref="Schedule"/> refuses any position at or before the one being
    /// dispatched. A handler that wants a same-instant reaction schedules it
    /// into a LATER phase, which is what the phases are for.
    ///
    /// That guard is deliberately strict: it makes "dispatch proceeds in
    /// non-decreasing key order" a real invariant that the validator (#13) can
    /// check, rather than a convention. Loosening it later is easy; noticing
    /// later that it was never true is not.
    ///
    /// It compares POSITION rather than full identity - see
    /// <see cref="ScheduledEvent.ComparePositionTo"/>. That distinction is
    /// load-bearing rather than pedantic: a freshly allocated
    /// <see cref="EventId"/> is always the larger one, so a guard built on
    /// <see cref="ScheduledEvent.CompareTo"/> accepts a reaction landing
    /// exactly where its own cause did, and a handler that reproduces itself
    /// there dispatches forever with the clock frozen.
    ///
    /// Position alone still cannot bound a cascade that climbs - reacting for
    /// one person, then the next, then the next - so
    /// <see cref="MaxCascadePerAdvance"/> puts a ceiling on how far one call may
    /// cascade at a single instant. Between them, a runaway fails loudly
    /// instead of hanging.
    ///
    /// Not thread-safe, and not intended to be. The simulation is
    /// single-threaded by design - section 5's determinism rules do not survive
    /// arbitrary interleaving.
    ///
    /// Saving and loading the queue is issue #15. See
    /// docs/design/kingdom-watch-plan-v7.1.md section 4.
    /// </remarks>
    public sealed class SimulationClock
    {
        /// <summary>
        /// How many same-instant reactions one <see cref="AdvanceTo"/> call
        /// may cascade into before the clock calls it a runaway.
        /// </summary>
        /// <remarks>
        /// This counts only reactions scheduled from inside a handler AT the
        /// instant being dispatched - never events booked ahead of time - so a
        /// legitimate same-tick batch does not consume any of it however large
        /// the population grows. A cascade that deep is a system reacting to
        /// its own reaction, and the alternative to failing is an
        /// <see cref="AdvanceTo"/> that never returns.
        ///
        /// The budget is per call, and resets whenever the dispatched instant
        /// changes within one. It does NOT carry across calls that happen to
        /// dispatch at the same instant. That is deliberate: a paused player
        /// casting a power is an event at the frozen instant, dispatched by its
        /// own <see cref="AdvanceTo"/>, with a short cascade behind it - and a
        /// budget shared across calls would eventually throw at a player who
        /// merely acted enough times while paused. What this guards is that
        /// <see cref="AdvanceTo"/> terminates. A driver that keeps re-entering
        /// one instant has control between calls, can read <see cref="Now"/>,
        /// and can see for itself that time is not moving.
        /// </remarks>
        public const int MaxCascadePerAdvance = 10_000;

        private readonly IdAllocator _ids;
        private readonly EventQueue _queue = new EventQueue();

        private bool _dispatching;
        private bool _hasCurrent;
        private ScheduledEvent _current;

        // Written by Schedule, reset by AdvanceTo - see MaxCascadePerAdvance.
        private int _cascadeCount;

        /// <summary>
        /// Builds a clock starting at <see cref="SimulationTime.Zero"/>.
        /// </summary>
        /// <param name="ids">
        /// The world's allocator. Scheduled events draw from its single event
        /// counter - the same one the domain-event bus draws from - so an id
        /// in the queue and an id in the journal never name two different
        /// things.
        /// </param>
        public SimulationClock(IdAllocator ids)
        {
            _ids = ids ?? throw new ArgumentNullException(nameof(ids));
        }

        /// <summary>Where the world clock currently stands.</summary>
        public SimulationTime Now { get; private set; }

        /// <summary>
        /// The allocator scheduled events draw their ids from. Internal so
        /// that <see cref="Events.DomainEventBus"/> can share it by taking
        /// the clock alone: a bus that accepted its own allocator could be
        /// wired with a different one, and two counters both starting at 1
        /// would hand the same id to a wake-up and a fact.
        /// </summary>
        internal IdAllocator Ids => _ids;

        /// <summary>Events still due. Cancelled ones are not counted.</summary>
        public int ScheduledCount => _queue.Count;

        /// <summary>
        /// Books an event and returns its durable id, which is what
        /// <see cref="Cancel"/> takes.
        /// </summary>
        /// <remarks>
        /// Rejects anything at or before the current position: in the past
        /// while idle, and at or before the event being dispatched while
        /// running. A rejection consumes an id, which is harmless - event ids
        /// need to be unique and increasing, not contiguous, and nothing reads
        /// a gap as meaning anything.
        /// </remarks>
        public EventId Schedule(
            SimulationTime time,
            SimulationPhase phase,
            ScheduledEventKind kind,
            EntityId primaryEntity,
            EntityId secondaryEntity)
        {
            var scheduled = new ScheduledEvent(
                _ids.NextEvent(), time, phase, kind, primaryEntity, secondaryEntity);

            if (_hasCurrent)
            {
                // Position, not CompareTo. A freshly allocated id is always the
                // larger one, so comparing full identity would accept a
                // reaction landing exactly where its own cause did - and a
                // handler that reproduces itself there dispatches forever
                // without the clock ever moving.
                if (scheduled.ComparePositionTo(_current) <= 0)
                {
                    throw new InvalidOperationException(
                        "Cannot schedule " + scheduled + " while dispatching " + _current
                        + ": reactions run after the event that caused them, never at or before it. "
                        + "Schedule a same-instant reaction into a later SimulationPhase.");
                }

                if (time == _current.Time)
                {
                    _cascadeCount++;

                    if (_cascadeCount > MaxCascadePerAdvance)
                    {
                        throw new InvalidOperationException(
                            "A cascade at " + time + " has scheduled more than "
                            + MaxCascadePerAdvance + " reactions at that same instant without the "
                            + "clock advancing, most recently " + scheduled
                            + ". Something is reacting to its own reaction.");
                    }
                }
            }
            else if (time < Now)
            {
                throw new InvalidOperationException(
                    "Cannot schedule " + scheduled + ": the clock already stands at " + Now
                    + ", and the world does not run backwards.");
            }

            _queue.Enqueue(scheduled);
            return scheduled.Id;
        }

        /// <summary>
        /// Drops a scheduled event. Returns false when the id was never
        /// scheduled, or has already been dispatched or cancelled - so a system
        /// re-predicting a threshold can tell whether it beat the crossing.
        /// </summary>
        public bool Cancel(EventId id) => _queue.Cancel(id);

        /// <summary>
        /// The next event due, without dispatching it. False when nothing is
        /// pending, which is how a caller knows compression may run freely to
        /// its target.
        /// </summary>
        public bool TryPeekNext(out ScheduledEvent next) => _queue.TryPeek(out next);

        /// <summary>
        /// Runs every event due on or before <paramref name="target"/>, in
        /// order, then leaves <see cref="Now"/> at <paramref name="target"/>.
        /// Returns how many events were dispatched.
        /// </summary>
        /// <remarks>
        /// Stepped local detail is this same call with a small target; extreme
        /// compression is this same call with a distant one. The clock does not
        /// need to know which, and section 4's warning about baking camera
        /// assumptions into simulation interfaces is why it must not.
        ///
        /// <see cref="Now"/> moves to each event's time before that event is
        /// handled, so a handler reading the clock sees its own instant.
        /// </remarks>
        public int AdvanceTo(SimulationTime target, IScheduledEventHandler handler)
        {
            if (handler is null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            if (target < Now)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(target),
                    target,
                    "The clock already stands at " + Now + ", and the world does not run backwards.");
            }

            if (_dispatching)
            {
                throw new InvalidOperationException(
                    "AdvanceTo is already running. Advancing the clock from inside an event handler is "
                    + "the reentrant dispatch section 4 rules out; schedule the reaction instead.");
            }

            _dispatching = true;
            _cascadeCount = 0;

            try
            {
                var dispatched = 0;

                // The instant currently being dispatched. Its starting value is
                // irrelevant: the count was just zeroed, so whether the first
                // event matches or not, it begins the call with a full budget.
                var instant = SimulationTime.Zero;

                while (_queue.TryDequeueDueBy(target, out var due))
                {
                    if (due.Time != instant)
                    {
                        instant = due.Time;
                        _cascadeCount = 0;
                    }

                    Now = due.Time;
                    _current = due;
                    _hasCurrent = true;

                    try
                    {
                        handler.Handle(due, this);
                    }
                    finally
                    {
                        _hasCurrent = false;
                        _current = default;
                    }

                    dispatched++;
                }

                Now = target;
                return dispatched;
            }
            finally
            {
                _dispatching = false;
                _hasCurrent = false;
                _current = default;
            }
        }
    }
}
