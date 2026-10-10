using System.Collections.Generic;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Lifecycle;
using KingdomWatch.Core.Needs;
using KingdomWatch.Core.Tests.Lifecycle;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Needs
{
    /// <summary>
    /// Hunger and cold together (#149, the #156 review). Each rations its
    /// winter store to all but its last hardship days, and both take from
    /// the same health, so when both are short the two gaps together must
    /// still be survivable.
    /// </summary>
    [TestFixture]
    public sealed class HardshipTests
    {
        // Answers the crossings Mortality would, so health can be read after
        // the winter rather than the death.
        private sealed class Crossings : IScheduledEventHandler
        {
            internal List<ScheduledEvent> Raised { get; } = new List<ScheduledEvent>();

            public void Handle(ScheduledEvent scheduled, SimulationClock clock) => Raised.Add(scheduled);
        }

        [Test]
        public void Food_that_lasts_the_winter_lets_a_short_woodpile_keep_its_full_gap()
        {
            // Meals at midnight, fires at nightfall: by the first winter night
            // the day's meal is eaten, and food for every meal still due must
            // not read as short (the #156 review). Short, it would ration the
            // fires to the shared gap and leave a hearth dark from the start.
            var w = Wired(out var hunger, out var warmth, out _);
            var band = Couples(w, 8, out var households);
            band.SharedSupplies.Gather(ResourceKind.Food, 16 * (int)SimulationTime.DaysPerSeason * Hunger.DailyRation);
            band.SharedSupplies.Gather(ResourceKind.Wood, 8 * OneShort(Warmth.HardshipNights) * Warmth.FuelPerFire);
            Track(w, hunger, warmth, band, afterNightfall: false);

            w.Run(FirstWinterNight);

            Assert.Multiple(() =>
            {
                foreach (var household in households)
                {
                    Assert.That(w.People.GetLastWarmedAt(household.Members[0]), Is.EqualTo(FirstWinterNight), household + " lit");
                }
            });
        }

        [Test]
        public void Wood_that_lasts_the_winter_lets_a_short_store_keep_its_full_gap()
        {
            // Fires at nightfall, meals after them: by the first winter meal
            // tonight's fire has burned, and wood for every night still to come
            // must not read as short, or the meal is rationed to the shared gap.
            var w = Wired(out var hunger, out var warmth, out _);
            var band = Couples(w, 8, out var households);
            band.SharedSupplies.Gather(ResourceKind.Food, 16 * OneShort(Hunger.HardshipDays) * Hunger.DailyRation);
            band.SharedSupplies.Gather(ResourceKind.Wood, 8 * (int)SimulationTime.DaysPerSeason * Warmth.FuelPerFire);
            Track(w, hunger, warmth, band, afterNightfall: true);

            var firstWinterMeal = FirstWinterDay.Plus(Warmth.Nightfall + SimulationTime.TicksPerHour);
            w.Run(firstWinterMeal);

            Assert.Multiple(() =>
            {
                foreach (var household in households)
                {
                    foreach (var member in household.Members)
                    {
                        Assert.That(w.People.GetLastFedAt(member), Is.EqualTo(firstWinterMeal), member + " fed");
                    }
                }
            });
        }

        [Test]
        public void A_winter_short_of_both_food_and_wood_leaves_those_it_rations_for_alive()
        {
            // Four couples, four hearths, with food and wood each enough for
            // every day but the last HardshipDays - what one shortage alone
            // would not ration. Short of both, each rations to its shared gap
            // instead, so those fed and warmed are fed and warmed until nearly
            // spring and live; served until both ran out, everyone spent both
            // full gaps at once and died on the last night.
            var w = Wired(out var hunger, out var warmth, out var crossings);
            var band = Couples(w, 4, out var households);
            band.SharedSupplies.Gather(ResourceKind.Food, 8 * OneShort(Hunger.HardshipDays) * Hunger.DailyRation);
            band.SharedSupplies.Gather(ResourceKind.Wood, 4 * OneShort(Warmth.HardshipNights) * Warmth.FuelPerFire);
            Track(w, hunger, warmth, band, afterNightfall: false);

            w.Run(SimulationTime.FromDays(SimulationTime.DaysPerYear).Plus(-1L));

            Assert.Multiple(() =>
            {
                for (var i = 0; i < 3; i++)
                {
                    foreach (var member in households[i].Members)
                    {
                        Assert.That(w.People.GetHealth(member), Is.GreaterThan(0), "household " + i + " was rationed for and lived");
                    }
                }

                Assert.That(crossings.Raised, Has.Count.LessThanOrEqualTo(2), "only the household rationed out");
            });
        }

        private static readonly SimulationTime FirstWinterDay = SimulationTime.FromDays(3L * SimulationTime.DaysPerSeason);

        private static readonly SimulationTime FirstWinterNight = FirstWinterDay.Plus(Warmth.Nightfall);

        // Days or nights of a winter that leave out its last gap.
        private static int OneShort(long gap) => (int)(SimulationTime.DaysPerSeason - gap);

        // Hunger and Warmth on one clock, with a recorder answering the
        // crossings Mortality would.
        private static Fixture Wired(out Hunger hunger, out Warmth warmth, out Crossings crossings)
        {
            var w = new Fixture();
            hunger = new Hunger(w.Bus, w.People);
            warmth = new Warmth(w.Clock, w.People);
            crossings = new Crossings();
            w.Router.Register(ScheduledEventKind.MealDue, hunger);
            w.Router.Register(ScheduledEventKind.WarmthDue, warmth);
            w.Router.Register(ScheduledEventKind.StarvationCritical, crossings);
            w.Router.Register(ScheduledEventKind.ExposureCritical, crossings);
            return w;
        }

        // A band of couples, each its own household and hearth.
        private static MobileGroup Couples(Fixture w, int count, out List<Household> households)
        {
            var band = w.NewBand();
            households = new List<Household>();

            for (var i = 0; i < count; i++)
            {
                households.Add(w.NewCouple(out var wife, out var husband));
                band.AddMember(wife);
                band.AddMember(husband);
            }

            return band;
        }

        // Tracked the day before winter: meals then fall due at midnight, or
        // an hour after nightfall, every day from the first winter day.
        private static void Track(Fixture w, Hunger hunger, Warmth warmth, MobileGroup band, bool afterNightfall)
        {
            var dayBefore = FirstWinterDay.Plus(-SimulationTime.TicksPerDay);
            w.Run(afterNightfall ? dayBefore.Plus(Warmth.Nightfall + SimulationTime.TicksPerHour) : dayBefore);
            hunger.Track(band);
            warmth.Track(band);
        }

        private sealed class Fixture
        {
            private readonly HouseholdWorld _base = new HouseholdWorld();

            internal ScheduledEventRouter Router { get; } = new ScheduledEventRouter();

            internal KingdomWatch.Core.Events.DomainEventBus Bus => _base.Bus;

            internal SimulationClock Clock => _base.Clock;

            internal PersonStore People => _base.People;

            internal MobileGroup NewBand() => _base.NewBand();

            internal Household NewCouple(out PersonHandle wife, out PersonHandle husband) => _base.NewCouple(out wife, out husband);

            internal void Run(SimulationTime to) => Clock.AdvanceTo(to, Router);
        }
    }
}
