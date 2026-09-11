using System;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;

namespace KingdomWatch.Harness
{
    /// <summary>
    /// A synthetic workload that drives <see cref="SimulationClock"/> the way
    /// the M1 systems will, so there is something to time and something to
    /// allocation-check before those systems exist (#58, #59).
    /// </summary>
    /// <remarks>
    /// Every entity has one recurring threshold crossing booked - think of it
    /// as a hunger crossing. Handling it books the next one. Every other
    /// dispatch also "eats": it cancels a neighbour's pending crossing and
    /// re-predicts it, which is the re-prediction section 4 says is the
    /// normal case and what keeps the queue's cancel-and-compact path busy.
    ///
    /// This is a soak of the scheduler, not a world. The numbers it produces
    /// describe <c>EventQueue</c> and <c>AdvanceTo</c>, nothing else, and it
    /// is replaced by the real two-band run when #17 lands.
    ///
    /// Everything here is plain arithmetic on preallocated arrays. The
    /// zero-allocation test wraps this handler together with the clock, so
    /// an allocation in here would be reported as a scheduler regression.
    /// </remarks>
    public sealed class SchedulerSoak : IScheduledEventHandler
    {
        private readonly SimulationClock _clock;
        private readonly EntityId[] _entities;
        private readonly EventId[] _pending;

        public SchedulerSoak(SimulationClock clock, int entityCount)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));

            if (entityCount < 2)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(entityCount), entityCount, "The soak needs at least two entities so each has a neighbour to disturb.");
            }

            _entities = new EntityId[entityCount];
            _pending = new EventId[entityCount];

            for (var i = 0; i < entityCount; i++)
            {
                _entities[i] = new EntityId(EntityKind.Person, (ulong)i + 1UL);
                // Staggered so the first day is not one burst at a single instant.
                _pending[i] = Book(_clock, i, _clock.Now.Plus(SimulationTime.TicksPerHour * (1L + (i % 23))));
            }
        }

        public long EventsDispatched { get; private set; }

        public long Cancellations { get; private set; }

        /// <summary>The largest number of live queue entries seen during dispatch.</summary>
        public int PeakPending { get; private set; }

        /// <summary>
        /// Advances the clock one simulated day at a time. Per-day stepping is
        /// the shape the validator (#13) needs, and it keeps the per-call
        /// cascade budget meaningful.
        /// </summary>
        public void RunDays(long days)
        {
            if (days < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(days), days, "Cannot run a negative number of days.");
            }

            var start = _clock.Now;
            for (var day = 1L; day <= days; day++)
            {
                _clock.AdvanceTo(start.Plus(day * SimulationTime.TicksPerDay), this);
            }
        }

        public void Handle(ScheduledEvent scheduled, SimulationClock clock)
        {
            var i = (int)(scheduled.PrimaryEntity.Value - 1UL);
            EventsDispatched++;

            _pending[i] = Book(clock, i, clock.Now.Plus(Interval(i)));

            if ((EventsDispatched & 1L) == 0L)
            {
                // Eating moves someone else's crossing. The offset is in
                // 1..N-1 so the neighbour is never the eater, and it drifts
                // with the dispatch count so the same pair is not disturbed
                // every time.
                var offset = 1L + (EventsDispatched % (_entities.Length - 1));
                var j = (int)((i + offset) % _entities.Length);

                if (clock.Cancel(_pending[j]))
                {
                    Cancellations++;
                }

                _pending[j] = Book(clock, j, clock.Now.Plus(SimulationTime.TicksPerHour + (Interval(j) / 2L)));
            }

            if (clock.ScheduledCount > PeakPending)
            {
                PeakPending = clock.ScheduledCount;
            }
        }

        // Six to twelve hours, varying by entity so they do not march in lockstep.
        private static long Interval(int entity) => SimulationTime.TicksPerHour * (6L + (entity % 7));

        private EventId Book(SimulationClock clock, int entity, SimulationTime at) =>
            clock.Schedule(
                at,
                SimulationPhase.Physical,
                ScheduledEventKind.TaskCompleted,
                _entities[entity],
                EntityId.None);
    }
}
