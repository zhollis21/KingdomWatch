using System;
using System.Collections.Generic;
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
    /// **The queue is state, and it leaves and returns as data.** Section 17
    /// says pending events are serialized rather than rebuilt from entity
    /// fields, because rebuilding can silently shift history across a
    /// version change. <see cref="CopyPendingTo"/> is the export and the
    /// restoring constructor is the import; both carry each event's
    /// <see cref="EventId"/>, so a booking a system kept (a
    /// <see cref="PendingBooking"/>) still names the same event afterwards.
    /// The export is refused unless <see cref="AtCheckpoint"/> - the one
    /// moment section 17 permits a snapshot. What is written to disk, and how a
    /// whole world is rebuilt from it, is issue #42. See
    /// docs/design/kingdom-watch-plan-v7.1.md sections 4 and 17.
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
        private bool _publishing;

        // Set when a handler or subscriber throws out of its call, and never
        // cleared - see AtCheckpoint.
        private bool _faulted;
        private bool _busClaimed;
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

        /// <summary>
        /// Rebuilds a clock from a snapshot: standing at <paramref name="now"/>,
        /// with <paramref name="pending"/> still due, each event keeping the
        /// id it was booked under.
        /// </summary>
        /// <remarks>
        /// This is the import half of section 17's rule that pending events
        /// are state. The list is what <see cref="CopyPendingTo"/> exported,
        /// in any order - the dispatch order is a property of the events, not
        /// of the list or the heap they sat in.
        ///
        /// Refuses rather than repairs, the way <see cref="IdAllocator.ResumeFrom"/>
        /// does: an event whose id the allocator has not yet handed out would
        /// be handed out again by the next <see cref="Schedule"/>, an id that
        /// appears twice breaks the total order, and an event before
        /// <paramref name="now"/> is a past the world cannot run back to. The
        /// caller resumes <paramref name="ids"/> first, the way it resumes
        /// everything else.
        /// </remarks>
        /// <param name="ids">The world's allocator, already resumed past every id in <paramref name="pending"/>.</param>
        /// <param name="now">Where the clock stood when the snapshot was taken.</param>
        /// <param name="pending">Every event that was still due.</param>
        public SimulationClock(IdAllocator ids, SimulationTime now, IReadOnlyList<ScheduledEvent> pending)
            : this(ids)
        {
            if (pending is null)
            {
                throw new ArgumentNullException(nameof(pending));
            }

            var next = ids.PeekNextEvent();

            for (var i = 0; i < pending.Count; i++)
            {
                var scheduled = pending[i];

                // The struct's constructor guards its fields; a default one
                // never went through it, and id 0 would pass the check below.
                if (scheduled.Id.IsNone)
                {
                    throw new ArgumentException(
                        "Cannot restore a defaulted entry (index " + i + "): it names no event, no phase and "
                        + "no kind, and nothing could have booked it.",
                        nameof(pending));
                }

                if (scheduled.Id.Value >= next)
                {
                    throw new ArgumentException(
                        "Cannot restore " + scheduled + ": its id has not been allocated (next is " + next
                        + "), so a later Schedule would hand it out again. Resume the allocator first.",
                        nameof(pending));
                }

                if (scheduled.Time < now)
                {
                    throw new ArgumentException(
                        "Cannot restore " + scheduled + " to a clock standing at " + now
                        + ": the world does not run backwards.",
                        nameof(pending));
                }

                if (!_queue.TryEnqueueRestored(scheduled))
                {
                    throw new ArgumentException(
                        "Cannot restore " + scheduled + ": its id appears twice, and an id is the last "
                        + "component of the ordering, so two events sharing one have no defined order.",
                        nameof(pending));
                }
            }

            Now = now;
        }

        /// <summary>Where the world clock currently stands.</summary>
        public SimulationTime Now { get; private set; }

        /// <summary>
        /// True when the world is consistent enough to snapshot: no
        /// <see cref="AdvanceTo"/> is running and no domain event is being
        /// published. Section 17's checkpoint.
        /// </summary>
        /// <remarks>
        /// Inside a handler, the event has been dequeued and whatever it
        /// mutates is in progress; inside a publish, subscribers are hearing
        /// about a change that has been announced but not yet made (the
        /// death cascade publishes first and mutates after). Either is the
        /// half-state section 17 says must never be serialized. Between
        /// calls there is none: <see cref="AdvanceTo"/> drains every event due
        /// on or before its target before returning, including the
        /// same-instant reactions handlers booked into later phases, so a
        /// cascade cannot be left half-run by the clock.
        ///
        /// Both flags live here rather than one on the clock and one on the
        /// bus so that a single question has a single answer. The bus reports
        /// through <see cref="Publishing"/> and <see cref="EndPublish"/>.
        ///
        /// **A fault is permanent.** When a handler or subscriber throws out
        /// of its call, the in-flight flags are put back - but the world is
        /// not. The event was dequeued and the mutation was partway through;
        /// a driver that catches the exception and asks this question would
        /// otherwise hear "yes" over exactly the half-state the flags exist
        /// to hide. So the clock remembers, and this stays false for the rest
        /// of its life. It still advances and publishes: a thrown tick means
        /// the world is being discarded (see <see cref="Lifecycle.Households.Form"/>),
        /// and the gate is where that is enforced, not the clock at large.
        /// A refusal the handler catches - a nested advance, scheduling in the
        /// past - is not a fault: the handler completed, and what it leaves
        /// is whole.
        ///
        /// What this does NOT guard is a driver that stops between two
        /// <see cref="AdvanceTo"/> calls at the same instant - that is a
        /// checkpoint, and correctly so: nothing has been dequeued and not
        /// finished. Nor does it need to guard a suspend: the simulation is
        /// single-threaded, so a platform pause lands between calls, never
        /// inside one.
        /// </remarks>
        public bool AtCheckpoint => !_dispatching && !_publishing && !_faulted;

        /// <summary>
        /// The allocator scheduled events draw their ids from. Internal so
        /// that <see cref="Events.DomainEventBus"/> can share it by taking
        /// the clock alone: a bus that accepted its own allocator could be
        /// wired with a different one, and two counters both starting at 1
        /// would hand the same id to a wake-up and a fact.
        /// </summary>
        internal IdAllocator Ids => _ids;

        /// <summary>
        /// Called by <see cref="Events.DomainEventBus"/> as it is built.
        /// Refuses a second bus on this clock: the bus's promise that
        /// publishing never nests is kept by <see cref="Publishing"/>, and
        /// two buses over one clock would let a subscriber on one publish
        /// through the other with neither noticing.
        /// </summary>
        internal void ClaimBus()
        {
            if (_busClaimed)
            {
                throw new InvalidOperationException(
                    "This clock already has a DomainEventBus. A world has one bus, so that the rule against "
                    + "publishing from inside a subscriber holds across every event stream there is.");
            }

            _busClaimed = true;
        }

        /// <summary>
        /// Whether <see cref="Events.DomainEventBus"/> is mid-publish. The
        /// bus's rule that reactions are queued rather than run recursively
        /// is checked against this flag, which lives here rather than on the
        /// bus so that <see cref="AtCheckpoint"/> sees it.
        /// </summary>
        internal bool Publishing
        {
            get => _publishing;
            set => _publishing = value;
        }

        /// <summary>
        /// Called by <see cref="Events.DomainEventBus"/> as a publish ends,
        /// however it ends. A publish that did not complete - a subscriber
        /// threw - is a fault, for the reason <see cref="AtCheckpoint"/> gives.
        /// </summary>
        internal void EndPublish(bool completed)
        {
            _faulted |= !completed;
            _publishing = false;
        }

        /// <summary>Events still due. Cancelled ones are not counted.</summary>
        public int ScheduledCount => _queue.Count;

        /// <summary>
        /// Fills <paramref name="into"/> with every event still due, ordered
        /// by <see cref="ScheduledEvent.CompareTo"/> - the order they will be
        /// dispatched in. Clears the list first.
        /// </summary>
        /// <remarks>
        /// The queue is a heap, so its array order is an implementation
        /// detail that compaction rewrites (<see cref="EventQueue"/>). What
        /// makes a stable answer possible is that
        /// <see cref="ScheduledEvent"/>'s ordering is *total* - it ends at
        /// <see cref="ScheduledEvent.Id"/>, which is unique - so sorting by it
        /// is a property of the events rather than of the container, and no
        /// two entries can tie. That also means the sort needs no stability
        /// guarantee, which is why <see cref="List{T}.Sort()"/> is enough.
        ///
        /// The caller supplies the list because the two things that want this
        /// - the world hash (section 5's canonical serialization, which asks
        /// for exactly this order) and the validator's scheduler rules - take
        /// it repeatedly over one run.
        ///
        /// This is deliberately not an <c>IEnumerable</c> property. Handing
        /// out a lazy view of the queue would let a caller hold it across a
        /// <see cref="AdvanceTo"/> and read a half-dispatched world. For the
        /// same reason it is refused unless <see cref="AtCheckpoint"/>: this
        /// is the export a snapshot is built from, and section 17 permits
        /// one only there. A handler that wants to look ahead has
        /// <see cref="TryPeekNext"/>.
        /// </remarks>
        /// <param name="into">The list to fill. Cleared before use.</param>
        public void CopyPendingTo(List<ScheduledEvent> into)
        {
            if (into is null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            if (!AtCheckpoint)
            {
                throw new InvalidOperationException(
                    "The pending events can only be read at a checkpoint, and the clock is "
                    + (_dispatching ? "mid-dispatch" : _publishing ? "mid-publish" : "faulted")
                    + ": what is due has already been partly acted on, and a snapshot of it would be "
                    + "the half-state section 17 forbids. Use TryPeekNext from inside a handler; a "
                    + "world that threw out of a tick is discarded, not saved.");
            }

            _queue.CopyLiveTo(into);
            into.Sort();
        }

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

                    // Completed, not "did not throw": a handler that catches
                    // one of this clock's own refusals and carries on has
                    // finished its work, and the world it leaves is whole.
                    var completed = false;

                    try
                    {
                        handler.Handle(due, this);
                        completed = true;
                    }
                    finally
                    {
                        _faulted |= !completed;
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
