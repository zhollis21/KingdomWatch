using System;
using System.Collections.Generic;
using System.Linq;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Construction;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Land;
using KingdomWatch.Core.Lifecycle;
using KingdomWatch.Core.Needs;
using KingdomWatch.Core.Traversal;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Construction
{
    /// <summary>
    /// A settlement's houses as its housing stock (#69): who may form a
    /// household, when a house is built for one, and what the land and the
    /// stores have to say about it.
    /// </summary>
    [TestFixture]
    public sealed class HousingTests
    {
        // Forest five rows deep across the north of the map, inside the
        // settlement's reach and clear of its yard: 150 trees, about 300 Wood
        // a year at five-year regrowth.
        private static readonly Action<TerrainGrid> Woods = grid =>
        {
            for (var y = 5; y < 10; y++)
            {
                for (var x = 5; x < 35; x++)
                {
                    grid.Set(new WorldPosition(x, y), TerrainKind.Forest);
                }
            }
        };

        [Test]
        public void A_free_house_is_a_vacancy_only_once_every_tent_family_is_housed()
        {
            var w = Fed(new BuildingsWorld());
            var id = w.Settlement.Id;

            Assert.That(w.Buildings.HasVacancy(id), Is.False, "no house yet");
            HouseEveryone(w);
            Assert.That(w.Buildings.HasVacancy(id), Is.False, "every house is lived in");

            var emptied = EmptyOneHouse(w);
            Assert.That(w.Buildings.HasVacancy(id), Is.True, "a house whose household is gone");

            // A household of the settlement's own with no home - a band's
            // tent family - is housed before a new couple is.
            var tent = w.World.Households.Form();
            w.World.Households.Join(tent, Newcomer(w, Sex.Female));

            Assert.Multiple(() =>
            {
                Assert.That(emptied.Occupant.IsNone, Is.True);
                Assert.That(w.Buildings.HasVacancy(id), Is.False, "the tent family waits for it");
            });
        }

        [Test]
        public void A_household_formed_in_a_settlement_moves_into_the_house_it_claimed()
        {
            var w = Fed(new BuildingsWorld());
            HouseEveryone(w);
            var house = EmptyOneHouse(w);

            var household = w.World.Households.Form(w.Settlement.Id);

            Assert.Multiple(() =>
            {
                Assert.That(household.Home, Is.EqualTo(house.Id));
                Assert.That(house.Occupant, Is.EqualTo(household.Id));
                Assert.That(w.Buildings.HomeOf(household.Id), Is.SameAs(house));
                Assert.That(w.Buildings.HasVacancy(w.Settlement.Id), Is.False, "and it is taken");
                Assert.That(() => w.World.Households.Form(w.Settlement.Id), Throws.InvalidOperationException);
            });
        }

        [Test]
        public void A_claim_cannot_be_made_twice_before_its_household_moves_in_or_without_a_vacancy()
        {
            var w = Fed(new BuildingsWorld());
            HouseEveryone(w);

            Assert.That(() => w.Buildings.Claim(w.Settlement.Id), Throws.InvalidOperationException, "no vacancy");

            EmptyOneHouse(w);
            EmptyOneHouse(w);
            w.Buildings.Claim(w.Settlement.Id);

            Assert.That(
                () => w.Buildings.Claim(w.Settlement.Id),
                Throws.InvalidOperationException,
                "the first claim's household has not moved in");
        }

        [Test]
        public void A_house_is_given_back_only_once_it_is_empty()
        {
            var w = Fed(new BuildingsWorld());
            HouseEveryone(w);
            var lived = w.Buildings.All.First(b => b.Kind == BuildingKind.House && !b.Occupant.IsNone);

            Assert.Multiple(() =>
            {
                Assert.That(() => w.Buildings.Release(lived.Id), Throws.InvalidOperationException, "lived in");
                Assert.That(() => w.Buildings.Release(new EntityId(EntityKind.Building, 999_999UL)), Throws.InvalidOperationException, "no such house");
                Assert.That(() => w.Buildings.Release(EmptyOneHouse(w).Id), Throws.Nothing);
            });
        }

        [Test]
        public void A_settlement_s_households_live_in_its_houses_and_everyone_else_s_in_camp()
        {
            var w = Fed(new BuildingsWorld());
            var housing = new SettlementHousing();
            var band = new EntityId(EntityKind.MobileGroup, 999UL);

            Assert.Multiple(() =>
            {
                Assert.That(housing.HasVacancy(band), Is.True, "a band always has room for a tent");
                Assert.That(housing.Claim(EntityId.None), Is.EqualTo(EntityId.None));
                Assert.That(() => housing.Release(EntityId.None), Throws.Nothing);
                Assert.That(() => housing.HasVacancy(w.Settlement.Id), Throws.InvalidOperationException, "no stock wired");
                Assert.That(() => housing.Stock = null!, Throws.ArgumentNullException);
            });

            housing.Stock = w.Buildings;
            Assert.That(housing.HasVacancy(w.Settlement.Id), Is.False, "the settlement has no house free");
        }

        [Test]
        public void A_spare_house_waits_for_single_adults_and_for_forest_to_fuel_it()
        {
            // Every household housed: the next house is a spare for the next
            // couple, built only when there is one to marry and the land can
            // fuel the hearth it would light.
            var bare = Fed(new BuildingsWorld());
            var wooded = Fed(new BuildingsWorld(paint: Woods));
            var lonely = Fed(new BuildingsWorld(paint: Woods));

            foreach (var w in new[] { bare, wooded, lonely })
            {
                HouseEveryone(w);
            }

            foreach (var w in new[] { bare, wooded })
            {
                Newcomer(w, Sex.Female);
                Newcomer(w, Sex.Male);
            }

            Newcomer(lonely, Sex.Female);
            var built = new Dictionary<string, int>();

            foreach (var (name, w) in new[] { ("bare", bare), ("wooded", wooded), ("lonely", lonely) })
            {
                var before = w.Buildings.All.Count;
                w.Dawn();
                built[name] = w.Buildings.All.Count - before;
            }

            Assert.Multiple(() =>
            {
                Assert.That(wooded.Buildings.CanFuelAnotherHearth(wooded.Settlement), Is.True);
                Assert.That(built["wooded"], Is.EqualTo(1), "a spare house");
                Assert.That(wooded.Latest!.Kind, Is.EqualTo(BuildingKind.House));
                Assert.That(bare.Buildings.CanFuelAnotherHearth(bare.Settlement), Is.False);
                Assert.That(built["bare"], Is.Zero, "no forest to fuel it");
                Assert.That(built["lonely"], Is.Zero, "nobody for her to marry");
            });
        }

        [Test]
        public void Wood_in_reach_is_every_forest_cell_grown_back_once_a_regrowth()
        {
            var w = new BuildingsWorld(paint: Woods);
            var trees = 5 * 30;
            var expected = (long)trees * w.World.Land.TreeCuts * Buildings.WoodPerCut * SimulationTime.DaysPerYear / LandCover.RegrowDays;

            Assert.Multiple(() =>
            {
                Assert.That(w.Buildings.WoodInReach(w.Settlement), Is.EqualTo(expected));
                Assert.That(w.Buildings.WoodInReach(w.Settlement.Position, w.Settlement.Id), Is.EqualTo(expected));
                Assert.That(new BuildingsWorld().Buildings.WoodInReach(BuildingsWorld.Centre, new BuildingsWorld().Settlement.Id), Is.Zero);
                Assert.That(() => w.Buildings.WoodInReach(null!), Throws.ArgumentNullException);
                Assert.That(() => w.Buildings.CanFuelAnotherHearth(null!), Throws.ArgumentNullException);
            });
        }

        [Test]
        public void Another_hearth_needs_its_winter_and_a_quarter_again_from_the_forest()
        {
            var w = new BuildingsWorld(paint: Woods);
            var hearths = Warmth.CountHearths(w.Settlement.Members, w.World.People, new List<EntityId>());
            var burn = (hearths + 1L) * Warmth.FuelPerFire * SimulationTime.DaysPerSeason;
            var wood = w.Buildings.WoodInReach(w.Settlement);

            Assert.That(
                w.Buildings.CanFuelAnotherHearth(w.Settlement),
                Is.EqualTo(wood * Buildings.WoodHeadroomDenominator >= burn * Buildings.WoodHeadroomNumerator));
        }

        [Test]
        public void A_year_s_food_in_store_counts_only_while_the_store_is_not_falling()
        {
            // #149: one village held off new fields for seventeen years on a
            // year's Grain that was draining the whole time. Read on the first
            // day of each year; lower than the year before, it is no cover.
            var w = new BuildingsWorld();
            var need = Buildings.YearlyNeed(w.Living);
            w.Stores.Gather(ResourceKind.Food, (int)(3 * need));
            w.Buildings.AtDawn(w.Settlement, 0, w.Living);
            var coveredAtFirst = !w.Buildings.IsFoodShort(w.Settlement, w.Living);

            // A year on, a year's eating gone and more than a year left.
            w.World.Clock.AdvanceTo(SimulationTime.FromDays(SimulationTime.DaysPerYear), w.World.Router);
            w.Buildings.AtDawn(w.Settlement, 0, w.Living);
            var trend = w.Buildings.StoreTrends.Single();

            Assert.Multiple(() =>
            {
                Assert.That(coveredAtFirst, Is.True, "a single reading is no trend");
                Assert.That(trend.Settlement, Is.EqualTo(w.Settlement.Id));
                Assert.That(trend.ThisYear, Is.LessThan(trend.LastYear));
                Assert.That(Hunger.MealsInStore(w.Stores), Is.GreaterThanOrEqualTo(need), "a year still in store");
                Assert.That(w.Buildings.IsFoodShort(w.Settlement, w.Living), Is.True, "but it is falling");
                Assert.That(w.Buildings.IsFoodShort(w.Settlement.Id), Is.True, "and so for the outlook");
                Assert.That(w.Buildings.IsFoodShort(new EntityId(EntityKind.MobileGroup, 1UL)), Is.False, "a band builds no fields");
            });
        }

        [Test]
        public void A_store_is_read_once_on_the_first_day_of_the_year()
        {
            var w = new BuildingsWorld();
            var first = Hunger.MealsInStore(w.Stores);
            w.Buildings.AtDawn(w.Settlement, 0, w.Living);
            w.Stores.Gather(ResourceKind.Food, 500);
            w.Buildings.AtDawn(w.Settlement, 0, w.Living);
            var trend = w.Buildings.StoreTrends.Single();

            Assert.Multiple(() =>
            {
                Assert.That(trend.ThisYear, Is.EqualTo(first), "the second dawn that day reads nothing");
                Assert.That(trend.LastYear, Is.EqualTo(-1L));
                Assert.That(trend.ReadOn, Is.Zero);
            });
        }

        [Test]
        public void The_world_hash_sees_the_store_readings()
        {
            // A reading changes what IsFoodShort answers, and so what a village
            // builds, courts and bears: two worlds that differ only there differ.
            var w = new BuildingsWorld();
            var before = new KingdomWatch.Core.Validation.WorldHash().AddBuildings(w.Buildings).Value;
            w.Buildings.AtDawn(w.Settlement, 0, w.Living);
            var read = new KingdomWatch.Core.Validation.WorldHash().AddBuildings(w.Buildings).Value;

            Assert.That(read, Is.Not.EqualTo(before));
        }

        [Test]
        public void A_band_settles_only_where_the_forest_fuels_half_its_hearths()
        {
            var bushes = (int)(((Buildings.YearlyNeed(12) * 3 / 2 / SimulationTime.SeasonsPerYear) / 42) + 1);
            void Bushes(TerrainGrid grid)
            {
                for (var i = 0; i < bushes; i++)
                {
                    grid.Set(new WorldPosition(5 + (i % 30), 30 + (i / 30)), TerrainKind.Scrub);
                }
            }

            var bare = new BuildingsWorld(paint: Bushes);
            var wooded = new BuildingsWorld(paint: grid => { Bushes(grid); Woods(grid); });
            var wood = wooded.Buildings.WoodInReach(BuildingsWorld.Centre, wooded.Settlement.Id);
            var half = (int)(wood * Buildings.WoodHeadroomDenominator * 2L / (Warmth.FuelPerFire * SimulationTime.DaysPerSeason * Buildings.WoodHeadroomNumerator));

            Assert.Multiple(() =>
            {
                Assert.That(bare.Buildings.CanSettle(BuildingsWorld.Centre, bare.Settlement.Id, 12, 0), Is.True, "no hearths, no wood needed");
                Assert.That(bare.Buildings.CanSettle(BuildingsWorld.Centre, bare.Settlement.Id, 12, 1), Is.False, "no forest");
                Assert.That(wooded.Buildings.CanSettle(BuildingsWorld.Centre, wooded.Settlement.Id, 12, half), Is.True, "the most the forest carries twice over");
                Assert.That(wooded.Buildings.CanSettle(BuildingsWorld.Centre, wooded.Settlement.Id, 12, half + 1), Is.False, "one more");
            });
        }

        // Food enough that the settlement is never short, so its plans are
        // houses rather than barns and fields.
        private static BuildingsWorld Fed(BuildingsWorld w)
        {
            w.Stores.Gather(ResourceKind.Food, (int)(10 * Buildings.YearlyNeed(100)));
            w.Wood(10_000);
            return w;
        }

        // Builds and hands out a house for every household the band brought.
        private static void HouseEveryone(BuildingsWorld w)
        {
            for (var i = 0; i < 40 && Unhoused(w) > 0; i++)
            {
                var before = w.Buildings.All.Count;
                w.Dawn();

                if (w.Buildings.All.Count > before)
                {
                    w.Finish(w.Latest!);
                }

                w.Buildings.AtDawn(w.Settlement, 0, w.Living);
            }

            Assert.That(Unhoused(w), Is.Zero, "every household was housed");
        }

        private static int Unhoused(BuildingsWorld w) =>
            w.Settlement.Members
                .Select(m => w.World.Households.Of(m))
                .OfType<Household>()
                .Distinct()
                .Count(h => w.Buildings.HomeOf(h.Id) is null);

        // Everyone in one housed household dies, and the household with them.
        private static Building EmptyOneHouse(BuildingsWorld w)
        {
            var house = w.Buildings.All.First(b => b.Kind == BuildingKind.House && !b.Occupant.IsNone);
            w.World.Households.TryGet(house.Occupant, out var household);

            foreach (var member in household.Members.ToList())
            {
                w.World.Deaths.Die(member, new Reasons(ReasonCode.Illness));
            }

            return house;
        }

        // An unpartnered adult in the settlement, in no household.
        private static PersonHandle Newcomer(BuildingsWorld w, Sex sex)
        {
            var world = w.World;
            var id = world.Ids.Next(EntityKind.Person);
            var person = world.People.Add(id, w.Settlement.Position, Hunger.FullHealth, AgeStage.Adult, sex, 0, 0, world.Now, world.Now.Ticks);
            world.Genealogy.Record(id, EntityId.None, EntityId.None);
            w.Settlement.AddMember(person);
            return person;
        }
    }
}
