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
        public void A_settlement_buildings_was_never_told_about_is_a_wiring_bug()
        {
            var w = new BuildingsWorld();
            var stranger = new EntityId(EntityKind.Settlement, 999_999UL);

            Assert.Multiple(() =>
            {
                Assert.That(() => w.Buildings.HasVacancy(stranger), Throws.Nothing, "no house of its own, so no question to ask");
                Assert.That(w.Buildings.HasVacancy(stranger), Is.False);
                Assert.That(() => w.Buildings.IsFoodShort(stranger), Throws.InvalidOperationException);
            });
        }

        [Test]
        public void No_spare_house_is_built_while_one_stands_empty()
        {
            var w = Fed(new BuildingsWorld(paint: Woods));
            HouseEveryone(w);
            Newcomer(w, Sex.Female);
            Newcomer(w, Sex.Male);
            EmptyOneHouse(w);
            var before = w.Buildings.All.Count;

            w.Dawn();

            Assert.That(w.Buildings.All, Has.Count.EqualTo(before));
        }

        [Test]
        public void The_validator_names_a_household_whose_home_is_not_its_house()
        {
            // Households keeps a household's home and Buildings the house's
            // occupant: the same fact from both ends, so they are checked
            // against each other.
            var w = Fed(new BuildingsWorld());
            HouseEveryone(w);
            EmptyOneHouse(w);
            var household = w.World.Households.Form(w.Settlement.Id);
            w.World.Households.Join(household, Newcomer(w, Sex.Female));
            var clean = new KingdomWatch.Harness.WorldValidator();
            clean.CheckBuildings(w.Buildings, w.World.Founding, w.World.Households, w.World.Grid, w.World.Clock);

            // The house forgets its household, which still names it.
            w.World.Bus.Publish(DomainEventKind.HouseholdDissolved, household.Id, EntityId.None);
            var broken = new KingdomWatch.Harness.WorldValidator();
            broken.CheckBuildings(w.Buildings, w.World.Founding, w.World.Households, w.World.Grid, w.World.Clock);

            Assert.Multiple(() =>
            {
                Assert.That(clean.Findings, Is.Empty);
                Assert.That(broken.Findings.Select(f => f.Rule), Is.EqualTo(new[] { KingdomWatch.Harness.ValidationRule.BuildingReference }));
                Assert.That(broken.Findings.Single().Subject, Is.EqualTo(household.Id));
            });
        }

        [Test]
        public void A_claim_on_its_own_changes_nothing_and_needs_a_vacancy()
        {
            // Claim names the house a household about to form will take; the
            // household moves in when its forming is announced. On its own a
            // claim leaves nothing behind - no state the world hash would have
            // to see, nothing to go stale (the #156 review).
            var w = Fed(new BuildingsWorld());
            HouseEveryone(w);

            Assert.That(() => w.Buildings.Claim(w.Settlement.Id), Throws.InvalidOperationException, "no vacancy");

            var house = EmptyOneHouse(w);
            var before = new KingdomWatch.Core.Validation.WorldHash().AddBuildings(w.Buildings).Value;
            var first = w.Buildings.Claim(w.Settlement.Id);
            var second = w.Buildings.Claim(w.Settlement.Id);

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.EqualTo(house.Id));
                Assert.That(second, Is.EqualTo(house.Id), "the same house, since nobody has moved in");
                Assert.That(w.Buildings.HasVacancy(w.Settlement.Id), Is.True);
                Assert.That(house.Occupant.IsNone, Is.True);
                Assert.That(new KingdomWatch.Core.Validation.WorldHash().AddBuildings(w.Buildings).Value, Is.EqualTo(before));
            });
        }

        [Test]
        public void A_household_announced_into_a_house_already_lived_in_is_refused()
        {
            // The house is taken by the announcement, so a second household
            // named into it is a wiring bug, not a second occupant.
            var w = Fed(new BuildingsWorld());
            HouseEveryone(w);
            var lived = w.Buildings.All.First(b => b.Kind == BuildingKind.House && !b.Occupant.IsNone);
            var occupant = lived.Occupant;

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => w.World.Bus.Publish(DomainEventKind.HouseholdFormed, new EntityId(EntityKind.Household, 999_999UL), lived.Id),
                    Throws.InvalidOperationException);
                Assert.That(lived.Occupant, Is.EqualTo(occupant), "the first household keeps it");
            });
        }

        [Test]
        public void The_store_readings_hash_the_same_whatever_order_the_settlements_were_first_read_in()
        {
            // WorldHash folds nothing in storage order (the #156 review): two
            // settlements read in opposite orders are the same world.
            static ulong ReadIn(bool westFirst)
            {
                var world = new World(1UL, new TerrainGrid(BuildingsWorld.Size, BuildingsWorld.Size, TerrainKind.Plains), DemographicSettings.Default);
                world.Nomads.Settles = false;
                var west = world.Founding.Found(world.AddBand(6, new WorldPosition(8, 20)), new Reasons(ReasonCode.PopulationPressure));
                var east = world.Founding.Found(world.AddBand(6, new WorldPosition(30, 20)), new Reasons(ReasonCode.PopulationPressure));
                var first = westFirst ? west : east;
                var second = westFirst ? east : west;
                world.Buildings.AtDawn(first, 0, first.Members.Count);
                world.Buildings.AtDawn(second, 0, second.Members.Count);
                return new KingdomWatch.Core.Validation.WorldHash().AddBuildings(world.Buildings).Value;
            }

            Assert.That(ReadIn(westFirst: false), Is.EqualTo(ReadIn(westFirst: true)));
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
        public void The_settlements_houses_are_handed_to_housing_once()
        {
            var w = new BuildingsWorld();
            var housing = new SettlementHousing { Stock = w.Buildings };

            Assert.Multiple(() =>
            {
                Assert.That(() => housing.Stock = w.Buildings, Throws.InvalidOperationException, "the same stock again");
                Assert.That(() => housing.Stock = new BuildingsWorld().Buildings, Throws.InvalidOperationException, "another stock");
                Assert.That(housing.Stock, Is.SameAs(w.Buildings));
            });
        }

        [Test]
        public void Housing_is_asked_only_about_a_band_a_settlement_or_no_community()
        {
            var housing = new SettlementHousing { Stock = new BuildingsWorld().Buildings };
            var strangers = new[]
            {
                new EntityId(EntityKind.Person, 7UL),
                new EntityId(EntityKind.Household, 7UL),
                new EntityId(EntityKind.Building, 7UL),
            };

            Assert.Multiple(() =>
            {
                foreach (var stranger in strangers)
                {
                    Assert.That(() => housing.HasVacancy(stranger), Throws.ArgumentException, "asking about " + stranger);
                    Assert.That(() => housing.Claim(stranger), Throws.ArgumentException, "claiming in " + stranger);
                }
            });
        }

        [Test]
        public void A_household_given_a_house_at_dawn_names_it_as_its_home()
        {
            var w = Fed(new BuildingsWorld());
            HouseEveryone(w);
            var housed = w.World.Households.All.Where(h => !(w.Buildings.HomeOf(h.Id) is null)).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(housed, Is.Not.Empty);

                foreach (var household in housed)
                {
                    Assert.That(w.Buildings.HomeOf(household.Id)?.Id, Is.EqualTo(household.Home), household.ToString());
                }
            });

            // Its house is given back when it dissolves, as a house claimed
            // at forming is.
            var emptied = EmptyOneHouse(w);
            Assert.That(w.Buildings.HasVacancy(w.Settlement.Id), Is.True);
            Assert.That(emptied.Occupant.IsNone, Is.True);
        }

        [Test]
        public void Only_a_household_in_camp_moves_into_a_home()
        {
            var w = Fed(new BuildingsWorld());
            HouseEveryone(w);
            var housed = w.World.Households.All.First(h => !h.Home.IsNone);
            var tent = w.World.Households.Form();
            var house = new EntityId(EntityKind.Building, 999_999UL);

            Assert.Multiple(() =>
            {
                Assert.That(() => w.World.Households.MoveIn(housed, house), Throws.InvalidOperationException, "already housed");
                Assert.That(() => w.World.Households.MoveIn(tent, EntityId.None), Throws.ArgumentException, "into no home");
                Assert.That(() => w.World.Households.MoveIn(null!, house), Throws.ArgumentNullException);
            });

            w.World.Households.Dissolve(tent);
            Assert.That(() => w.World.Households.MoveIn(tent, house), Throws.InvalidOperationException, "dissolved");
        }

        [Test]
        public void The_validator_names_a_household_living_in_a_house_it_does_not_name()
        {
            var w = Fed(new BuildingsWorld());
            HouseEveryone(w);
            var house = EmptyOneHouse(w);
            var tent = w.World.Households.Form();
            w.World.Households.Join(tent, Newcomer(w, Sex.Female));

            // Buildings moves the tent family in; its household never hears.
            w.World.Bus.Publish(DomainEventKind.HouseholdFormed, tent.Id, house.Id);
            var validator = new KingdomWatch.Harness.WorldValidator();
            validator.CheckBuildings(w.Buildings, w.World.Founding, w.World.Households, w.World.Grid, w.World.Clock);

            Assert.Multiple(() =>
            {
                Assert.That(validator.Findings.Select(f => f.Rule), Is.EqualTo(new[] { KingdomWatch.Harness.ValidationRule.BuildingReference }));
                Assert.That(validator.Findings.Single().Subject, Is.EqualTo(tent.Id));
            });
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
                Assert.That(wooded.Buildings.All[wooded.Buildings.All.Count - 1].Kind, Is.EqualTo(BuildingKind.House));
                Assert.That(bare.Buildings.CanFuelAnotherHearth(bare.Settlement), Is.False);
                Assert.That(built["bare"], Is.Zero, "no forest to fuel it");
                Assert.That(built["lonely"], Is.Zero, "nobody for her to marry");
            });
        }

        [Test]
        public void Wood_in_reach_is_every_forest_cell_grown_back_once_a_regrowth()
        {
            var w = new BuildingsWorld(paint: Woods);
            var bare = new BuildingsWorld();
            var trees = 5 * 30;
            var expected = (long)trees * w.World.Land.TreeCuts * Buildings.WoodPerCut * SimulationTime.DaysPerYear / LandCover.RegrowDays;

            Assert.Multiple(() =>
            {
                Assert.That(w.Buildings.WoodInReach(w.Settlement), Is.EqualTo(expected));
                Assert.That(bare.Buildings.WoodInReach(bare.Settlement), Is.Zero);
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
            // Wood for the winter, so the village is the same size a year on.
            var w = new BuildingsWorld();
            w.Wood(10_000);
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
        public void A_store_deep_enough_is_cover_even_while_it_falls()
        {
            // A village living off savings that deep keeps growing; only as
            // they run low does a falling store stop counting. Wood for the
            // winter, so the village is the same size a year on.
            var w = new BuildingsWorld();
            w.Wood(10_000);
            var need = Buildings.YearlyNeed(w.Living);
            w.Stores.Gather(ResourceKind.Food, (int)((Buildings.DeepStoreYears + 2L) * need));
            w.Buildings.AtDawn(w.Settlement, 0, w.Living);
            w.World.Clock.AdvanceTo(SimulationTime.FromDays(SimulationTime.DaysPerYear), w.World.Router);
            w.Buildings.AtDawn(w.Settlement, 0, w.Living);
            var trend = w.Buildings.StoreTrends.Single();

            Assert.Multiple(() =>
            {
                Assert.That(trend.ThisYear, Is.LessThan(trend.LastYear), "falling");
                Assert.That(Hunger.MealsInStore(w.Stores), Is.GreaterThanOrEqualTo(Buildings.DeepStoreYears * need));
                Assert.That(w.Buildings.IsFoodShort(w.Settlement, w.Living), Is.False);
            });
        }

        [Test]
        public void A_year_s_food_in_a_store_that_is_growing_is_cover()
        {
            // Wood for the winter, so the village is the same size a year on.
            var w = new BuildingsWorld();
            w.Wood(10_000);
            var need = Buildings.YearlyNeed(w.Living);
            w.Stores.Gather(ResourceKind.Food, (int)need);
            w.Buildings.AtDawn(w.Settlement, 0, w.Living);
            w.World.Clock.AdvanceTo(SimulationTime.FromDays(SimulationTime.DaysPerYear - 1L), w.World.Router);
            w.Stores.Gather(ResourceKind.Food, (int)(3 * need));
            w.World.Clock.AdvanceTo(SimulationTime.FromDays(SimulationTime.DaysPerYear), w.World.Router);
            w.Buildings.AtDawn(w.Settlement, 0, w.Living);
            var trend = w.Buildings.StoreTrends.Single();

            Assert.Multiple(() =>
            {
                Assert.That(trend.ThisYear, Is.GreaterThan(trend.LastYear));
                Assert.That(w.Buildings.IsFoodShort(w.Settlement, w.Living), Is.False);
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
                    w.Finish(w.Buildings.All[w.Buildings.All.Count - 1]);
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
