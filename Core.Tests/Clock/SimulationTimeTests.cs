using System;
using KingdomWatch.Core.Clock;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Clock
{
    [TestFixture]
    public sealed class SimulationTimeTests
    {
        [Test]
        public void The_start_of_the_world_is_the_default()
        {
            Assert.Multiple(() =>
            {
                Assert.That(default(SimulationTime), Is.EqualTo(SimulationTime.Zero));
                Assert.That(SimulationTime.Zero.Ticks, Is.Zero);
                Assert.That(SimulationTime.Zero.DayNumber, Is.Zero);
                Assert.That(SimulationTime.Zero.TickOfDay, Is.Zero);
            });
        }

        [Test]
        public void One_tick_is_one_simulated_second()
        {
            Assert.Multiple(() =>
            {
                Assert.That(SimulationTime.TicksPerSecond, Is.EqualTo(1L));
                Assert.That(SimulationTime.TicksPerMinute, Is.EqualTo(60L));
                Assert.That(SimulationTime.TicksPerHour, Is.EqualTo(3600L));
                Assert.That(SimulationTime.TicksPerDay, Is.EqualTo(86400L));
            });
        }

        [Test]
        public void Time_never_runs_backwards()
        {
            // Simulation time is a point on a clock that starts at zero, not a
            // duration. A negative one would mean an event scheduled before the
            // world began.
            Assert.Multiple(() =>
            {
                Assert.That(
                    () => new SimulationTime(-1L), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => SimulationTime.FromDays(-1L), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => SimulationTime.FromHours(-1L), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => SimulationTime.FromMinutes(-1L), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => SimulationTime.FromSeconds(-1L), Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void Units_convert_to_ticks()
        {
            Assert.Multiple(() =>
            {
                Assert.That(SimulationTime.FromSeconds(90L).Ticks, Is.EqualTo(90L));
                Assert.That(SimulationTime.FromMinutes(2L).Ticks, Is.EqualTo(120L));
                Assert.That(SimulationTime.FromHours(3L).Ticks, Is.EqualTo(10_800L));
                Assert.That(SimulationTime.FromDays(2L).Ticks, Is.EqualTo(172_800L));
            });
        }

        [Test]
        public void Zero_of_any_unit_is_the_start_of_the_world()
        {
            Assert.Multiple(() =>
            {
                Assert.That(SimulationTime.FromSeconds(0L), Is.EqualTo(SimulationTime.Zero));
                Assert.That(SimulationTime.FromMinutes(0L), Is.EqualTo(SimulationTime.Zero));
                Assert.That(SimulationTime.FromHours(0L), Is.EqualTo(SimulationTime.Zero));
                Assert.That(SimulationTime.FromDays(0L), Is.EqualTo(SimulationTime.Zero));
            });
        }

        [Test]
        public void A_conversion_that_would_overflow_the_clock_is_rejected()
        {
            // Silently wrapping to a negative tick would put events before the
            // start of the world, where nothing would ever dispatch them.
            // Same reasoning as the Plus overflow test: the message is what
            // separates a rejected conversion from a wrapped one that happened
            // to land negative and get caught by the wrong guard.
            Assert.Multiple(() =>
            {
                Assert.That(
                    () => SimulationTime.FromDays(long.MaxValue),
                    Throws.TypeOf<ArgumentOutOfRangeException>()
                        .With.Message.Contains("overflow"));
                Assert.That(
                    () => SimulationTime.FromHours(long.MaxValue),
                    Throws.TypeOf<ArgumentOutOfRangeException>()
                        .With.Message.Contains("overflow"));
                Assert.That(
                    () => SimulationTime.FromMinutes(long.MaxValue),
                    Throws.TypeOf<ArgumentOutOfRangeException>()
                        .With.Message.Contains("overflow"));
                Assert.That(
                    () => SimulationTime.FromDays((long.MaxValue / SimulationTime.TicksPerDay) + 1L),
                    Throws.TypeOf<ArgumentOutOfRangeException>()
                        .With.Message.Contains("overflow"));
            });
        }

        [Test]
        public void Seconds_convert_at_the_very_top_of_the_range()
        {
            // TicksPerSecond is 1, so this one must NOT be rejected - the
            // overflow guard divides by the scale and has to stay exact at the
            // boundary rather than rejecting a legal value.
            Assert.That(SimulationTime.FromSeconds(long.MaxValue).Ticks, Is.EqualTo(long.MaxValue));
        }

        [Test]
        public void A_tick_splits_into_a_day_and_a_time_of_day()
        {
            var time = SimulationTime.FromDays(2L).Plus((17L * 3600L) + (42L * 60L) + 9L);

            Assert.Multiple(() =>
            {
                Assert.That(time.DayNumber, Is.EqualTo(2L));
                Assert.That(time.TickOfDay, Is.EqualTo(63_729L));
            });
        }

        [Test]
        public void Adding_a_duration_moves_the_clock()
        {
            var start = SimulationTime.FromHours(10L);

            Assert.Multiple(() =>
            {
                Assert.That(start.Plus(12L * 60L), Is.EqualTo(SimulationTime.FromMinutes(612L)));
                Assert.That(start.Plus(0L), Is.EqualTo(start));
                Assert.That(start.Plus(-3600L), Is.EqualTo(SimulationTime.FromHours(9L)));
            });
        }

        [Test]
        public void Adding_cannot_land_before_the_start_of_the_world()
        {
            Assert.That(
                () => SimulationTime.FromHours(1L).Plus(-3601L),
                Throws.TypeOf<ArgumentOutOfRangeException>()
                    .With.Message.Contains("before the start of the world"));
        }

        [Test]
        public void Adding_cannot_overflow_the_clock()
        {
            // Asserting the message, not just the type. Unchecked arithmetic
            // wraps an overflowing sum to a negative tick, so the
            // before-the-start-of-the-world guard rejects it too - and a test
            // that only checked the exception type would pass with the overflow
            // guard deleted, reporting the wrong cause to whoever hit it.
            Assert.Multiple(() =>
            {
                Assert.That(
                    () => new SimulationTime(long.MaxValue).Plus(1L),
                    Throws.TypeOf<ArgumentOutOfRangeException>()
                        .With.Message.Contains("overflow"));
                Assert.That(
                    () => SimulationTime.FromDays(1L).Plus(long.MaxValue),
                    Throws.TypeOf<ArgumentOutOfRangeException>()
                        .With.Message.Contains("overflow"));
            });
        }

        [Test]
        public void Adding_the_extremes_of_a_long_is_rejected_rather_than_wrapped()
        {
            // The far ends of the parameter's own type, which is where a
            // missing guard turns into a silently wrapped tick rather than an
            // error.
            Assert.Multiple(() =>
            {
                Assert.That(
                    () => SimulationTime.Zero.Plus(long.MinValue),
                    Throws.TypeOf<ArgumentOutOfRangeException>()
                        .With.Message.Contains("before the start of the world"));
                Assert.That(
                    new SimulationTime(long.MaxValue).Plus(0L).Ticks, Is.EqualTo(long.MaxValue));
            });
        }

        [Test]
        public void Two_hundred_years_is_nowhere_near_the_limit()
        {
            // The reason a long is enough: section 4's 200-year harness runs
            // are about 6.3e9 ticks, nine orders of magnitude inside the range.
            var twoHundredYears = SimulationTime.FromDays(200L * 365L);

            Assert.That(twoHundredYears.Ticks, Is.LessThan(long.MaxValue / 1_000_000L));
        }

        [Test]
        public void Distance_between_two_times_is_signed()
        {
            var earlier = SimulationTime.FromHours(1L);
            var later = SimulationTime.FromHours(3L);

            Assert.Multiple(() =>
            {
                Assert.That(earlier.TicksUntil(later), Is.EqualTo(7200L));
                Assert.That(later.TicksUntil(earlier), Is.EqualTo(-7200L));
                Assert.That(earlier.TicksUntil(earlier), Is.Zero);
            });
        }

        [Test]
        public void Equal_ticks_are_equal_times()
        {
            var left = SimulationTime.FromMinutes(5L);
            var right = new SimulationTime(300L);

            Assert.Multiple(() =>
            {
                Assert.That(left, Is.EqualTo(right));
                Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
                Assert.That(left == right, Is.True);
                Assert.That(left != right, Is.False);
                Assert.That(left.Equals((object)right), Is.True);
            });
        }

        [Test]
        public void Nothing_that_is_not_a_time_is_equal_to_one()
        {
            var time = SimulationTime.FromMinutes(5L);

            Assert.Multiple(() =>
            {
                Assert.That(time.Equals(null), Is.False);
                Assert.That(time.Equals("day 0 00:05:00"), Is.False);
                Assert.That(time.Equals(300L), Is.False);
            });
        }

        [Test]
        public void Times_order_by_tick()
        {
            var earlier = SimulationTime.FromMinutes(5L);
            var sameAsEarlier = new SimulationTime(300L);
            var later = SimulationTime.FromMinutes(6L);

            Assert.Multiple(() =>
            {
                Assert.That(earlier.CompareTo(later), Is.Negative);
                Assert.That(later.CompareTo(earlier), Is.Positive);
                Assert.That(earlier.CompareTo(sameAsEarlier), Is.Zero);
                Assert.That(earlier < later, Is.True);
                Assert.That(earlier <= later, Is.True);
                Assert.That(later > earlier, Is.True);
                Assert.That(later >= earlier, Is.True);
                Assert.That(earlier >= sameAsEarlier, Is.True);
                Assert.That(earlier <= sameAsEarlier, Is.True);
                Assert.That(earlier < sameAsEarlier, Is.False);
                Assert.That(earlier > sameAsEarlier, Is.False);
            });
        }

        [Test]
        public void ToString_carries_both_the_readable_time_and_the_repro_tick()
        {
            // Section 5 wants a bug report to become "seed 38471928, tick
            // 183729", so the raw tick has to survive being printed.
            var time = SimulationTime.FromDays(2L).Plus((17L * 3600L) + (42L * 60L) + 9L);

            Assert.Multiple(() =>
            {
                Assert.That(time.ToString(), Is.EqualTo("day 2 17:42:09 (tick 236529)"));
                Assert.That(SimulationTime.Zero.ToString(), Is.EqualTo("day 0 00:00:00 (tick 0)"));
            });
        }
    }
}
