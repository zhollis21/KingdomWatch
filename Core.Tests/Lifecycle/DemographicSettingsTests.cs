using System;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Lifecycle;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Lifecycle
{
    [TestFixture]
    public sealed class DemographicSettingsTests
    {
        [Test]
        public void The_default_table_is_valid()
        {
            Assert.That(() => DemographicSettings.Default.Validate(), Throws.Nothing);
        }

        [Test]
        public void Validation_refuses_boundaries_out_of_order_chances_outside_per_mille_and_zero_intervals()
        {
            Assert.Multiple(() =>
            {
                Refused(new DemographicSettings { ChildAtYears = 0L });
                Refused(new DemographicSettings { AdolescentAtYears = 3L });
                Refused(new DemographicSettings { AdultAtYears = 12L });
                Refused(new DemographicSettings { ElderAtYears = 16L });
                Refused(new DemographicSettings { SoftLifespanYears = 55L });
                Refused(new DemographicSettings { MaxLifespanYears = 70L });
                Refused(new DemographicSettings { FertileFromYears = 0L });
                Refused(new DemographicSettings { FertileUntilYears = 16L });
                Refused(new DemographicSettings { BirthCheckTicks = 0L });
                Refused(new DemographicSettings { GestationTicks = -1L });
                Refused(new DemographicSettings { PostpartumTicks = -1L });
                Assert.That(() => new DemographicSettings { PostpartumTicks = 0L }.Validate(), Throws.Nothing, "no recovery at all is a valid table");
                Refused(new DemographicSettings { FrailtyMultiplier = 0 });
                Refused(new DemographicSettings { HungerMultiplier = 0 });
                Refused(new DemographicSettings { FrailtyMultiplier = 1001 });
                Refused(new DemographicSettings { HungerMultiplier = 1001 });
                Assert.That(() => new DemographicSettings { FrailtyMultiplier = 1000, HungerMultiplier = 1000 }.Validate(), Throws.Nothing);
                Refused(new DemographicSettings { SoftLifespanYears = DemographicSettings.MaxYears, MaxLifespanYears = DemographicSettings.MaxYears + 1L });
                Refused(new DemographicSettings { FertileUntilYears = DemographicSettings.MaxYears + 1L });
                Assert.That(() => new DemographicSettings { SoftLifespanYears = DemographicSettings.MaxYears - 1L, MaxLifespanYears = DemographicSettings.MaxYears, FertileUntilYears = DemographicSettings.MaxYears }.Validate(), Throws.Nothing);
                Assert.That(DemographicSettings.MaxYears * SimulationTime.TicksPerYear, Is.LessThan(long.MaxValue / 1000L), "a boundary in ticks stays far inside the clock");
                Refused(new DemographicSettings { ConceptionPerMille = 1001 });
                Refused(new DemographicSettings { InfantMortalityPerMille = -1 });
                Refused(new DemographicSettings { ChildMortalityPerMille = 1001 });
                Refused(new DemographicSettings { AdolescentMortalityPerMille = 1001 });
                Refused(new DemographicSettings { AdultMortalityPerMille = 1001 });
                Refused(new DemographicSettings { ElderMortalityPerMille = 1001 });
            });
        }

        [Test]
        public void Stages_are_contiguous_and_the_boundaries_are_the_birthdays()
        {
            var s = DemographicSettings.Default;

            Assert.Multiple(() =>
            {
                Assert.That(s.StageAt(0L), Is.EqualTo(AgeStage.Infant));
                Assert.That(s.StageAt(s.ChildAtYears - 1L), Is.EqualTo(AgeStage.Infant));
                Assert.That(s.StageAt(s.ChildAtYears), Is.EqualTo(AgeStage.Child));
                Assert.That(s.StageAt(s.AdolescentAtYears), Is.EqualTo(AgeStage.Adolescent));
                Assert.That(s.StageAt(s.AdultAtYears), Is.EqualTo(AgeStage.Adult));
                Assert.That(s.StageAt(s.ElderAtYears - 1L), Is.EqualTo(AgeStage.Adult));
                Assert.That(s.StageAt(s.ElderAtYears), Is.EqualTo(AgeStage.Elder));
                Assert.That(s.StageAt(500L), Is.EqualTo(AgeStage.Elder));
            });
        }

        [Test]
        public void The_next_boundary_is_the_first_birthday_strictly_ahead_and_none_for_an_elder()
        {
            var s = DemographicSettings.Default;

            Assert.Multiple(() =>
            {
                Assert.That(s.TryNextBoundary(0L, out var b1) && b1 == s.ChildAtYears, Is.True);
                Assert.That(s.TryNextBoundary(s.ChildAtYears, out var b2) && b2 == s.AdolescentAtYears, Is.True, "on the birthday itself, the next one");
                Assert.That(s.TryNextBoundary(s.AdolescentAtYears, out var b3) && b3 == s.AdultAtYears, Is.True);
                Assert.That(s.TryNextBoundary(s.AdultAtYears, out var b4) && b4 == s.ElderAtYears, Is.True);
                Assert.That(s.TryNextBoundary(s.ElderAtYears, out _), Is.False);
            });
        }

        [Test]
        public void The_fertile_window_is_closed_below_and_open_above()
        {
            var s = DemographicSettings.Default;

            Assert.Multiple(() =>
            {
                Assert.That(s.IsFertileAge(s.FertileFromYears - 1L), Is.False);
                Assert.That(s.IsFertileAge(s.FertileFromYears), Is.True);
                Assert.That(s.IsFertileAge(s.FertileUntilYears - 1L), Is.True);
                Assert.That(s.IsFertileAge(s.FertileUntilYears), Is.False);
            });
        }

        [Test]
        public void Base_mortality_is_the_stage_rate_then_ramps_to_certainty_at_the_maximum()
        {
            var s = new DemographicSettings
            {
                ElderAtYears = 55L,
                ElderMortalityPerMille = 100,
                SoftLifespanYears = 60L,
                MaxLifespanYears = 70L,
            };
            s.Validate();

            Assert.Multiple(() =>
            {
                Assert.That(s.BaseMortalityPerMille(0L), Is.EqualTo(s.InfantMortalityPerMille));
                Assert.That(s.BaseMortalityPerMille(s.ChildAtYears), Is.EqualTo(s.ChildMortalityPerMille));
                Assert.That(s.BaseMortalityPerMille(s.AdolescentAtYears), Is.EqualTo(s.AdolescentMortalityPerMille));
                Assert.That(s.BaseMortalityPerMille(s.AdultAtYears), Is.EqualTo(s.AdultMortalityPerMille));
                Assert.That(s.BaseMortalityPerMille(59L), Is.EqualTo(100), "the elder rate up to the soft lifespan");
                Assert.That(s.BaseMortalityPerMille(60L), Is.EqualTo(100), "the ramp starts from the rate");
                Assert.That(s.BaseMortalityPerMille(65L), Is.EqualTo(550), "halfway");
                Assert.That(s.BaseMortalityPerMille(69L), Is.EqualTo(910));
                Assert.That(s.BaseMortalityPerMille(70L), Is.EqualTo(1000), "certain at the maximum");
                Assert.That(s.BaseMortalityPerMille(200L), Is.EqualTo(1000), "and past it");
            });
        }

        private static void Refused(DemographicSettings settings) =>
            Assert.That(() => settings.Validate(), Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}
