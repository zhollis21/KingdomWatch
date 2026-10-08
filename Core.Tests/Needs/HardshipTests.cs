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
        public void A_winter_short_of_both_food_and_wood_leaves_those_it_rations_for_alive()
        {
            // Four couples, four hearths, with food and wood each enough for
            // every day but the last HardshipDays - what one shortage alone
            // would not ration. Short of both, each rations to its shared gap
            // instead, so those fed and warmed are fed and warmed until nearly
            // spring and live; served until both ran out, everyone spent both
            // full gaps at once and died on the last night.
            var w = new HouseholdWorld();
            var hunger = new Hunger(w.Bus, w.People);
            var warmth = new Warmth(w.Clock, w.People);
            var crossings = new Crossings();
            var router = new ScheduledEventRouter();
            router.Register(ScheduledEventKind.MealDue, hunger);
            router.Register(ScheduledEventKind.WarmthDue, warmth);
            router.Register(ScheduledEventKind.StarvationCritical, crossings);
            router.Register(ScheduledEventKind.ExposureCritical, crossings);

            var band = w.NewBand();
            var households = new List<Household>();

            for (var i = 0; i < 4; i++)
            {
                households.Add(w.NewCouple(out var wife, out var husband));
                band.AddMember(wife);
                band.AddMember(husband);
            }

            var oneShortDays = (int)(SimulationTime.DaysPerSeason - Hunger.HardshipDays);
            band.SharedSupplies.Gather(ResourceKind.Food, 8 * oneShortDays * Hunger.DailyRation);
            band.SharedSupplies.Gather(ResourceKind.Wood, 4 * oneShortDays * Warmth.FuelPerFire);
            w.Clock.AdvanceTo(SimulationTime.FromDays((3L * SimulationTime.DaysPerSeason) - 1L), router);
            hunger.Track(band);
            warmth.Track(band);

            w.Clock.AdvanceTo(SimulationTime.FromDays(SimulationTime.DaysPerYear).Plus(-1L), router);

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
    }
}
