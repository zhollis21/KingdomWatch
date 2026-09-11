using System;
using System.Diagnostics;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Harness;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Performance
{
    /// <summary>
    /// The two guards #58 and #59 ask for, pointed at the only tick loop that
    /// exists so far: <see cref="SchedulerSoak"/> driving
    /// <see cref="SimulationClock.AdvanceTo"/>. When #17 lands, the same two
    /// assertions move to the real run.
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    public sealed class SchedulerSoakTests
    {
        private const int Entities = 300;

        // Two years of 300 entities is a few hundred milliseconds in Release
        // on a desktop. The bound is deliberately an order of magnitude and
        // more above that: it exists to catch Cancel turning into a heap scan,
        // not to police percentages, and a flaky perf test is worse than none.
        private const int ThroughputYears = 2;
        private static readonly TimeSpan ThroughputBound = TimeSpan.FromSeconds(10);

        private static SchedulerSoak NewSoak(int entities = Entities) =>
            new SchedulerSoak(new SimulationClock(new IdAllocator()), entities);

        [Test]
        public void Soak_needs_a_clock_and_at_least_two_entities()
        {
            Assert.Multiple(() =>
            {
                Assert.That(() => new SchedulerSoak(null!, 2), Throws.ArgumentNullException);
                Assert.That(() => NewSoak(1), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => NewSoak(0), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => NewSoak(-1), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => NewSoak().RunDays(-1L), Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        // A span that cannot fit in SimulationTime is refused before the first
        // day runs. Without the guard the loop would iterate for roughly a
        // hundred billion days before the tick arithmetic wrapped.
        [Test]
        public void A_span_past_the_end_of_time_is_refused_up_front()
        {
            var soak = NewSoak();
            var tooMany = (long.MaxValue / SimulationTime.TicksPerDay) + 1L;

            Assert.Multiple(() =>
            {
                Assert.That(() => soak.RunDays(tooMany), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => soak.RunDays(long.MaxValue), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(soak.EventsDispatched, Is.Zero, "nothing should have run");
            });
        }

        // The measurements below only mean something if the workload really
        // exercises the paths it claims to - scheduling, cancelling, and
        // compacting - and does so identically every time.
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(Entities)]
        public void Soak_dispatches_cancels_and_is_deterministic(int entities)
        {
            var first = NewSoak(entities);
            first.RunDays(30L);

            var second = NewSoak(entities);
            second.RunDays(30L);

            Assert.Multiple(() =>
            {
                Assert.That(first.EventsDispatched, Is.GreaterThan(0L));
                // Every other dispatch eats, and eating always finds a live
                // neighbour booking to cancel.
                Assert.That(first.Cancellations, Is.EqualTo(first.EventsDispatched / 2L));
                // Every entity keeps exactly one crossing booked, so the live
                // count can never exceed the entity count. More than that
                // would mean a stale booking was left behind uncancelled.
                Assert.That(first.PeakPending, Is.EqualTo(entities));

                Assert.That(second.EventsDispatched, Is.EqualTo(first.EventsDispatched));
                Assert.That(second.Cancellations, Is.EqualTo(first.Cancellations));
                Assert.That(second.PeakPending, Is.EqualTo(first.PeakPending));
                // Counts alone would let two runs dispatch the same events
                // in a different order. The trace hash would not.
                Assert.That(first.TraceHash, Is.Not.Zero);
                Assert.That(second.TraceHash, Is.EqualTo(first.TraceHash));
            });
        }

        [Test]
        public void Running_zero_days_dispatches_nothing()
        {
            var soak = NewSoak();
            soak.RunDays(0L);

            Assert.That(soak.EventsDispatched, Is.Zero);
        }

        // #59. Section 18: zero allocations in the tick loop.
        [Test]
        public void AdvanceTo_at_steady_state_allocates_nothing()
        {
            var soak = NewSoak();

            // First pass grows the heap and hash set to their working set and
            // JITs everything on the path. None of that is per-tick.
            soak.RunDays(30L);

            var allocated = Allocations.Measure(() => soak.RunDays(30L));

            Assert.That(allocated, Is.Zero, "bytes allocated on the test thread across 30 simulated days");
        }

        // #58. A representative run finishes under a loose bound.
        [Test]
        public void Soak_finishes_within_bound()
        {
            var soak = NewSoak();

            var stopwatch = Stopwatch.StartNew();
            soak.RunDays(ThroughputYears * 365L);
            stopwatch.Stop();

            var simYearsPerSecond = ThroughputYears / stopwatch.Elapsed.TotalSeconds;
            TestContext.Out.WriteLine(
                $"{soak.EventsDispatched:N0} events in {stopwatch.ElapsedMilliseconds:N0} ms "
                + $"({simYearsPerSecond:N1} sim-years/s, bound {ThroughputBound.TotalSeconds:N0} s)");

            Assert.That(stopwatch.Elapsed, Is.LessThan(ThroughputBound));
        }
    }
}
