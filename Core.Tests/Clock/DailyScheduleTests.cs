using KingdomWatch.Core.Clock;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Clock
{
    /// <summary>The hours of a villager's day (#21).</summary>
    [TestFixture]
    public sealed class DailyScheduleTests
    {
        private static readonly long Hour = SimulationTime.TicksPerHour;

        [Test]
        public void The_day_runs_wake_dawn_dusk_supper_nightfall_bed_in_that_order()
        {
            Assert.That(
                new[] { DailySchedule.Wake, DailySchedule.Dawn, DailySchedule.Dusk, DailySchedule.Supper, DailySchedule.Nightfall, DailySchedule.Bed },
                Is.Ordered.And.All.InRange(0L, SimulationTime.TicksPerDay - 1L));
        }

        [Test]
        public void The_next_hour_is_strictly_after_now()
        {
            var day = SimulationTime.FromDays(3L);

            Assert.Multiple(() =>
            {
                Assert.That(DailySchedule.TicksUntilNext(day, DailySchedule.Supper), Is.EqualTo(DailySchedule.Supper));
                Assert.That(DailySchedule.TicksUntilNext(day.Plus(DailySchedule.Supper - 1L), DailySchedule.Supper), Is.EqualTo(1L));
                Assert.That(DailySchedule.TicksUntilNext(day.Plus(DailySchedule.Supper), DailySchedule.Supper), Is.EqualTo(SimulationTime.TicksPerDay), "at the hour itself, tomorrow's");
                Assert.That(DailySchedule.TicksUntilNext(day.Plus(DailySchedule.Bed), DailySchedule.Wake), Is.EqualTo(8L * Hour), "across midnight");
            });
        }

        [Test]
        public void Bedtime_runs_from_bed_to_wake_across_midnight()
        {
            var day = SimulationTime.FromDays(3L);

            Assert.Multiple(() =>
            {
                Assert.That(DailySchedule.IsBedtime(day.Plus(DailySchedule.Bed - 1L)), Is.False);
                Assert.That(DailySchedule.IsBedtime(day.Plus(DailySchedule.Bed)), Is.True);
                Assert.That(DailySchedule.IsBedtime(day), Is.True, "midnight");
                Assert.That(DailySchedule.IsBedtime(day.Plus(DailySchedule.Wake - 1L)), Is.True);
                Assert.That(DailySchedule.IsBedtime(day.Plus(DailySchedule.Wake)), Is.False);
            });
        }
    }
}
