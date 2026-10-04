using System.Linq;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Construction;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Land;
using KingdomWatch.Core.Lifecycle;
using KingdomWatch.Core.Traversal;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Construction
{
    [TestFixture]
    public sealed class BuildingsTests
    {
        private const long Hour = SimulationTime.TicksPerHour;

        [Test]
        public void Construction_refuses_a_missing_collaborator()
        {
            var w = new BuildingsWorld().World;
            var skills = new NoviceSkills();

            Assert.Multiple(() =>
            {
                Assert.That(() => new Buildings(null!, w.People, w.Households, w.Pathfinder, w.KnownMaps, w.Land, skills), Throws.ArgumentNullException);
                Assert.That(() => new Buildings(w.Bus, null!, w.Households, w.Pathfinder, w.KnownMaps, w.Land, skills), Throws.ArgumentNullException);
                Assert.That(() => new Buildings(w.Bus, w.People, null!, w.Pathfinder, w.KnownMaps, w.Land, skills), Throws.ArgumentNullException);
                Assert.That(() => new Buildings(w.Bus, w.People, w.Households, null!, w.KnownMaps, w.Land, skills), Throws.ArgumentNullException);
                Assert.That(() => new Buildings(w.Bus, w.People, w.Households, w.Pathfinder, null!, w.Land, skills), Throws.ArgumentNullException);
                Assert.That(() => new Buildings(w.Bus, w.People, w.Households, w.Pathfinder, w.KnownMaps, null!, skills), Throws.ArgumentNullException);
                Assert.That(() => new Buildings(w.Bus, w.People, w.Households, w.Pathfinder, w.KnownMaps, w.Land, null!), Throws.ArgumentNullException);
            });
        }

        [Test]
        public void A_band_is_not_a_settlement_and_builds_nothing()
        {
            var w = new BuildingsWorld().World;
            var band = w.AddBand(4, new WorldPosition(5, 5));

            Assert.That(() => w.Buildings.AtDawn(band, 1, 4), Throws.ArgumentException);
        }

        [Test]
        public void Households_without_homes_get_a_house_when_hands_and_wood_are_spare()
        {
            var w = new BuildingsWorld();
            w.Wood(100);
            var woodBefore = w.Stores.Available(ResourceKind.Wood);

            w.Dawn();

            var house = w.Buildings.All.Single();
            var spec = BuildingTable.Of(BuildingKind.House);

            Assert.Multiple(() =>
            {
                Assert.That(house.Kind, Is.EqualTo(BuildingKind.House));
                Assert.That(house.Settlement, Is.EqualTo(w.Settlement.Id));
                Assert.That(house.Id.Kind, Is.EqualTo(EntityKind.Building));
                Assert.That(house.IsComplete, Is.False);
                Assert.That(house.ClearTicks, Is.Zero, "open plains first: nothing to clear");
                Assert.That(house.LabourTicks, Is.EqualTo(spec.BuildTicks));
                Assert.That(house.Covers(BuildingsWorld.Centre), Is.False, "the camp cell is kept");
                Assert.That(w.Stores.Available(ResourceKind.Wood), Is.EqualTo(woodBefore - spec.Wood));
                Assert.That(w.Stores.Flows(ResourceKind.Wood).Embodied, Is.EqualTo(spec.Wood));
            });
        }

        [Test]
        public void No_spare_hands_no_building()
        {
            var w = new BuildingsWorld();
            w.Wood(100);

            w.Buildings.AtDawn(w.Settlement, 0, w.Living);

            Assert.That(w.Buildings.All, Is.Empty);
        }

        [Test]
        public void No_skill_no_building()
        {
            var w = new BuildingsWorld();
            var unskilled = new Buildings(
                w.World.Bus, w.World.People, w.World.Households, w.World.Pathfinder, w.World.KnownMaps, w.World.Land, new NoSkills());
            w.Wood(100);

            unskilled.AtDawn(w.Settlement, 1, w.Living);

            Assert.That(unskilled.All, Is.Empty);
        }

        [Test]
        public void Wood_for_the_coming_winter_is_not_built_with()
        {
            var w = new BuildingsWorld();
            // Fed through the wait, since nobody here works.
            w.Stores.Gather(ResourceKind.Food, w.Living * 200);

            // Summer: a whole winter of fires is spoken for.
            w.World.AdvanceTo(new SimulationTime(SimulationTime.TicksPerDay * SimulationTime.DaysPerSeason));
            var hearths = KingdomWatch.Core.Needs.Warmth.CountHearths(w.Settlement.Members, w.World.People, new System.Collections.Generic.List<EntityId>());
            var reserve = hearths * KingdomWatch.Core.Needs.Warmth.FuelPerFire * (int)SimulationTime.DaysPerSeason;
            var have = w.Stores.Available(ResourceKind.Wood);
            var spec = BuildingTable.Of(BuildingKind.House);
            var count = w.Buildings.All.Count;

            if (have >= reserve + spec.Wood)
            {
                w.Stores.Consume(ResourceKind.Wood, have - (reserve + spec.Wood - 1));
            }
            else
            {
                w.Wood(reserve + spec.Wood - 1 - have);
            }

            w.Dawn();
            Assert.That(w.Buildings.All.Count, Is.EqualTo(count), "one short of the reserve plus the house");

            w.Wood(1);
            w.Dawn();
            Assert.That(w.Buildings.All.Count, Is.EqualTo(count + 1));
        }

        [Test]
        public void One_building_goes_up_at_a_time()
        {
            var w = new BuildingsWorld();
            w.Wood(500);

            w.Dawn();
            w.Dawn();

            Assert.That(w.Buildings.All, Has.Count.EqualTo(1));
        }

        [Test]
        public void A_finished_house_is_announced_and_goes_to_the_first_household_without_one()
        {
            var w = new BuildingsWorld();
            w.Wood(100);
            var house = w.BuildNext();

            Assert.That(w.Heard.Count(e => e.Kind == DomainEventKind.BuildingCompleted && e.PrimaryEntity == house.Id && e.SecondaryEntity == w.Settlement.Id), Is.EqualTo(1));
            Assert.That(house.Occupant.IsNone, Is.True, "homes are handed out at dawn");

            w.Buildings.AtDawn(w.Settlement, 0, w.Living);

            var first = w.Settlement.Members.Select(m => w.World.Households.Of(m)).OfType<Household>().First();
            Assert.Multiple(() =>
            {
                Assert.That(house.Occupant, Is.EqualTo(first.Id));
                Assert.That(w.Buildings.HomeOf(first.Id), Is.SameAs(house));
            });
        }

        [Test]
        public void A_household_that_dissolves_leaves_its_house_empty()
        {
            var w = new BuildingsWorld();
            w.Wood(100);
            var house = w.BuildNext();
            w.Buildings.AtDawn(w.Settlement, 0, w.Living);
            var household = house.Occupant;

            w.World.Bus.Publish(DomainEventKind.HouseholdDissolved, household, EntityId.None);

            Assert.Multiple(() =>
            {
                Assert.That(house.Occupant.IsNone, Is.True);
                Assert.That(w.Buildings.HomeOf(household), Is.Null);
            });
        }

        [Test]
        public void A_hungry_village_builds_a_barn_after_its_first_house_then_fields_around_it()
        {
            // All plains: no bushes in reach, so food is short once the
            // stores hold less than a year.
            var w = new BuildingsWorld();
            w.Wood(500);

            var house = w.BuildNext();
            var barn = w.BuildNext();
            var field = w.BuildNext();

            Assert.Multiple(() =>
            {
                Assert.That(house.Kind, Is.EqualTo(BuildingKind.House));
                Assert.That(barn.Kind, Is.EqualTo(BuildingKind.Barn));
                Assert.That(field.Kind, Is.EqualTo(BuildingKind.Field));
                Assert.That(field.Barn, Is.EqualTo(barn.Id));
                Assert.That(field.Stage, Is.EqualTo(FieldStage.Tending));
                Assert.That(System.Math.Abs(field.Anchor.X - barn.Anchor.X), Is.LessThanOrEqualTo(Buildings.FieldRadius));
                Assert.That(System.Math.Abs(field.Anchor.Y - barn.Anchor.Y), Is.LessThanOrEqualTo(Buildings.FieldRadius));
            });
        }

        [Test]
        public void A_barn_holds_only_so_many_fields()
        {
            // Forty people: short of food until some ten fields stand.
            var w = new BuildingsWorld(people: 40);
            w.Wood(1000);
            w.BuildNext();
            var barn = w.BuildNext();

            for (var i = 0; i < Buildings.FieldsPerBarn; i++)
            {
                w.BuildNext();
            }

            var second = w.BuildNext();

            Assert.Multiple(() =>
            {
                Assert.That(w.Buildings.All.Count(b => b.Barn == barn.Id), Is.EqualTo(Buildings.FieldsPerBarn));
                Assert.That(second.Kind, Is.EqualTo(BuildingKind.Barn));
            });
        }

        [Test]
        public void A_village_with_a_year_in_store_is_not_short_of_food()
        {
            var w = new BuildingsWorld();
            var need = Buildings.YearlyNeed(w.Living);
            Assert.That(w.Buildings.IsFoodShort(w.Settlement, w.Living), Is.True, "no bushes, no fields");

            w.Stores.Gather(ResourceKind.Grain, (int)(need / PrimitiveTier.MealsPerGrain));

            Assert.That(w.Buildings.IsFoodShort(w.Settlement, w.Living), Is.False);
        }

        [Test]
        public void Bushes_in_reach_are_counted_as_a_years_picking()
        {
            var w = new BuildingsWorld(paint: grid =>
            {
                for (var x = 0; x < 5; x++)
                {
                    grid.Set(new WorldPosition(24 + x, 20), TerrainKind.Scrub);
                }
            });

            var perBush = (long)w.World.Land.BushPicks
                * (PrimitiveTier.Forage.Outputs[0].Quantity
                    + PrimitiveTier.ForageIn(Season.Summer).Outputs[0].Quantity
                    + PrimitiveTier.ForageIn(Season.Autumn).Outputs[0].Quantity);

            Assert.Multiple(() =>
            {
                Assert.That(w.Buildings.BushYearlyFood, Is.EqualTo(perBush));
                Assert.That(w.Buildings.ForageInReach(w.Settlement.Position, w.Settlement.Id), Is.EqualTo(5 * perBush));
            });
        }

        [Test]
        public void Builders_claim_shares_and_clearing_fells_trees_for_their_wood()
        {
            // No plains anywhere a house could go: a checkerboard of forest
            // and scrub, so any footprint has standing trees and bushes on it.
            var w = new BuildingsWorld(paint: grid =>
            {
                for (var y = 0; y < BuildingsWorld.Size; y++)
                {
                    for (var x = 0; x < BuildingsWorld.Size; x++)
                    {
                        grid.Set(new WorldPosition(x, y), (x + y) % 2 == 0 ? TerrainKind.Forest : TerrainKind.Scrub);
                    }
                }

                grid.Set(BuildingsWorld.Centre, TerrainKind.Plains);
            });
            w.Wood(100);
            w.Dawn();

            var house = w.Buildings.All.Single();
            var cells = house.Width * house.Height;
            var trees = 0;

            for (var dy = 0; dy < house.Height; dy++)
            {
                for (var dx = 0; dx < house.Width; dx++)
                {
                    trees += w.World.Grid[new WorldPosition(house.Anchor.X + dx, house.Anchor.Y + dy)] == TerrainKind.Forest ? 1 : 0;
                }
            }

            Assert.That(trees, Is.GreaterThan(0));
            var expected = (trees * Buildings.FellTreeTicks) + ((cells - trees) * Buildings.ClearTicksPerCell);

            Assert.That(house.ClearTicks, Is.EqualTo(expected));
            Assert.That(w.Buildings.ShareOf(house, JobKind.Builder), Is.EqualTo(Buildings.ShareTicks));

            var woodBefore = w.Stores.Available(ResourceKind.Wood);
            w.Finish(house);

            Assert.Multiple(() =>
            {
                Assert.That(house.Cleared, Is.True);
                Assert.That(house.IsComplete, Is.True);
                Assert.That(w.Stores.Available(ResourceKind.Wood) - woodBefore, Is.EqualTo(trees * Buildings.WoodPerFelledTree));

                for (var dy = 0; dy < house.Height; dy++)
                {
                    for (var dx = 0; dx < house.Width; dx++)
                    {
                        var at = new WorldPosition(house.Anchor.X + dx, house.Anchor.Y + dy);
                        Assert.That(w.World.Grid[at], Is.EqualTo(TerrainKind.Plains), at.ToString());
                        Assert.That(w.World.Land.StateAt(w.World.Grid.IndexOf(at)), Is.Zero, at.ToString());
                    }
                }
            });
        }

        [Test]
        public void Open_plains_are_preferred_to_clearing()
        {
            // Scrub right up to the camp, plains a little further out.
            var w = new BuildingsWorld(paint: grid =>
            {
                for (var y = 14; y <= 26; y++)
                {
                    for (var x = 14; x <= 26; x++)
                    {
                        grid.Set(new WorldPosition(x, y), TerrainKind.Scrub);
                    }
                }

                grid.Set(BuildingsWorld.Centre, TerrainKind.Plains);
            });
            w.Wood(100);
            w.Dawn();

            Assert.That(w.Buildings.All.Single().ClearTicks, Is.Zero);
        }

        [Test]
        public void An_unclaimed_trip_gives_its_hours_back()
        {
            var w = new BuildingsWorld();
            w.Wood(100);
            w.Dawn();
            var house = w.Buildings.All.Single();

            w.Buildings.Claim(house, JobKind.Builder, Buildings.ShareTicks);
            Assert.That(w.Buildings.TryBuildWork(w.Settlement.Id, out _, out var shortBy, out var target), Is.True);
            Assert.That(shortBy, Is.EqualTo(house.LabourTicks - Buildings.ShareTicks));
            Assert.That(target, Is.EqualTo(house.LabourTicks * Buildings.NeedScale));

            w.Buildings.Unclaim(house.Anchor, JobKind.Builder, Buildings.ShareTicks);
            Assert.That(house.Claimed, Is.Zero);
        }

        [Test]
        public void A_field_counts_one_day_a_day_then_harvests_grain_and_is_sown_again()
        {
            var w = new BuildingsWorld();
            w.Wood(500);
            w.BuildNext();
            w.BuildNext();
            var field = w.BuildNext();
            var day = SimulationTime.TicksPerDay;

            void WorkTheDay()
            {
                for (var share = 0; share < Buildings.FieldDayTicks / Buildings.ShareTicks; share++)
                {
                    Assert.That(w.Buildings.TryFieldWork(w.Settlement.Id, out var f, out _, out _), Is.True);
                    Assert.That(f, Is.SameAs(field));
                    var ticks = w.Buildings.ShareOf(field, JobKind.Farmer);
                    w.Buildings.Claim(field, JobKind.Farmer, ticks);
                    w.Buildings.Credit(field.Anchor, JobKind.Farmer, ticks, w.Stores);
                }

                Assert.That(w.Buildings.TryFieldWork(w.Settlement.Id, out _, out _, out _), Is.False, "a day's hours are all done");
            }

            for (var i = 0; i < Buildings.TendingDays; i++)
            {
                WorkTheDay();
                w.World.Clock.AdvanceTo(w.World.Now.Plus(day), w.World.Router);
            }

            Assert.That(field.Stage, Is.EqualTo(FieldStage.Harvesting));
            var grain = w.Stores.Available(ResourceKind.Grain);

            for (var i = 0; i < Buildings.HarvestDays; i++)
            {
                WorkTheDay();
                w.World.Clock.AdvanceTo(w.World.Now.Plus(day), w.World.Router);
            }

            Assert.Multiple(() =>
            {
                Assert.That(field.Stage, Is.EqualTo(FieldStage.Tending));
                Assert.That(field.DaysDone, Is.Zero);
                Assert.That(w.Stores.Flows(ResourceKind.Grain).Gathered - grain, Is.EqualTo(Buildings.HarvestDays * Buildings.GrainPerHarvestDay));
                Assert.That(w.Heard.Count(e => e.Kind == DomainEventKind.FieldHarvested && e.PrimaryEntity == field.Id), Is.EqualTo(1));
            });
        }

        [Test]
        public void Nobody_works_a_field_in_winter()
        {
            var w = new BuildingsWorld();
            w.Wood(500);
            w.BuildNext();
            w.BuildNext();
            w.BuildNext();

            w.World.Clock.AdvanceTo(new SimulationTime(3L * SimulationTime.DaysPerSeason * SimulationTime.TicksPerDay), w.World.Router);

            Assert.That(w.World.Now.Season, Is.EqualTo(Season.Winter));
            Assert.That(w.Buildings.TryFieldWork(w.Settlement.Id, out _, out _, out _), Is.False);
        }

        [Test]
        public void A_band_settles_only_in_early_spring_with_a_season_of_bushes_and_room_for_a_barn()
        {
            // Enough bushes in reach for a season and a half of twelve people,
            // at 42 Food a bush a year (7 picks of 2 in each fruiting season).
            var bushes = (int)(((Buildings.YearlyNeed(12) * 3 / 2 / SimulationTime.SeasonsPerYear) / 42) + 1);
            var w = new BuildingsWorld(paint: grid =>
            {
                for (var i = 0; i < bushes; i++)
                {
                    grid.Set(new WorldPosition(5 + (i % 30), 5 + (i / 30)), TerrainKind.Scrub);
                }
            });
            var at = BuildingsWorld.Centre;
            var map = w.Settlement.Id;

            Assert.That(w.Buildings.CanSettle(at, map, 12), Is.True, "spring day 0");
            Assert.That(w.Buildings.CanSettle(at, map, 1000), Is.False, "too many mouths");

            w.World.Clock.AdvanceTo(new SimulationTime(((SimulationTime.DaysPerSeason / 2) - 1) * SimulationTime.TicksPerDay), w.World.Router);
            Assert.That(w.Buildings.CanSettle(at, map, 12), Is.True, "the last day of early spring");

            w.World.Clock.AdvanceTo(new SimulationTime((SimulationTime.DaysPerSeason / 2) * SimulationTime.TicksPerDay), w.World.Router);
            Assert.That(w.Buildings.CanSettle(at, map, 12), Is.False, "late spring");

            w.World.Clock.AdvanceTo(new SimulationTime(SimulationTime.DaysPerSeason * SimulationTime.TicksPerDay), w.World.Router);
            Assert.That(w.Buildings.CanSettle(at, map, 12), Is.False, "the first day of summer");
        }

        [Test]
        public void A_dawn_needs_a_settlement()
        {
            var w = new BuildingsWorld();

            Assert.That(() => w.Buildings.AtDawn(null!, 1, 1), Throws.ArgumentNullException);
        }

        [Test]
        public void No_demand_no_building()
        {
            // Everyone housed and a year in store: nothing is wanted.
            var w = new BuildingsWorld();
            w.Wood(1000);
            w.Stores.Gather(ResourceKind.Grain, (int)(Buildings.YearlyNeed(w.Living) / PrimitiveTier.MealsPerGrain));

            while (w.Settlement.Members.Any(m => w.World.Households.Of(m) is Household h && w.Buildings.HomeOf(h.Id) is null))
            {
                w.BuildNext();
                w.Buildings.AtDawn(w.Settlement, 0, w.Living);
            }

            var count = w.Buildings.All.Count;
            w.Dawn();

            Assert.Multiple(() =>
            {
                Assert.That(w.Buildings.All.Count, Is.EqualTo(count));
                Assert.That(w.Buildings.All.All(b => b.Kind == BuildingKind.House), Is.True);
            });
        }

        [Test]
        public void No_room_no_building_and_no_settling()
        {
            // Rocks everywhere but the camp: no footprint fits anywhere.
            var w = new BuildingsWorld(paint: grid =>
            {
                for (var y = 0; y < BuildingsWorld.Size; y++)
                {
                    for (var x = 0; x < BuildingsWorld.Size; x++)
                    {
                        grid.Set(new WorldPosition(x, y), (x + y) % 2 == 0 ? TerrainKind.Rocks : TerrainKind.Scrub);
                    }
                }

                grid.Set(BuildingsWorld.Centre, TerrainKind.Plains);
            });
            w.Wood(100);
            var wood = w.Stores.Available(ResourceKind.Wood);

            w.Dawn();

            Assert.Multiple(() =>
            {
                Assert.That(w.Buildings.All, Is.Empty);
                Assert.That(w.Stores.Available(ResourceKind.Wood), Is.EqualTo(wood), "nothing embodied");
                Assert.That(w.Buildings.ForageInReach(BuildingsWorld.Centre, w.Settlement.Id), Is.GreaterThan(Buildings.YearlyNeed(1)), "bushes aplenty");
                Assert.That(w.Buildings.CanSettle(BuildingsWorld.Centre, w.Settlement.Id, 1), Is.False, "but no ground for a barn");
            });
        }

        [Test]
        public void No_building_under_way_is_no_building_work()
        {
            var w = new BuildingsWorld();

            Assert.That(w.Buildings.TryBuildWork(w.Settlement.Id, out var project, out _, out _), Is.False);
            Assert.That(project, Is.Null);
        }

        [Test]
        public void The_last_share_is_what_is_left()
        {
            var w = new BuildingsWorld();
            w.Wood(100);
            w.Dawn();
            var house = w.Buildings.All.Single();
            var allButAnHour = house.LabourTicks - Hour;

            w.Buildings.Claim(house, JobKind.Builder, allButAnHour);

            Assert.That(w.Buildings.ShareOf(house, JobKind.Builder), Is.EqualTo(Hour));
        }

        [Test]
        public void Claims_and_credits_refuse_work_that_is_not_there()
        {
            var w = new BuildingsWorld();
            w.Wood(500);
            var house = w.BuildNext();
            w.BuildNext();
            w.Dawn();
            var field = w.Buildings.All.Last();
            var b = w.Buildings;

            Assert.Multiple(() =>
            {
                Assert.That(field.Kind, Is.EqualTo(BuildingKind.Field));
                Assert.That(field.IsComplete, Is.False);

                // The wrong job for the building, or no building job at all.
                Assert.That(() => b.Claim(house, JobKind.Builder, Hour), Throws.ArgumentException, "a finished house");
                Assert.That(() => b.Claim(house, JobKind.Farmer, Hour), Throws.ArgumentException, "a house is no field");
                Assert.That(() => b.Claim(field, JobKind.Farmer, Hour), Throws.ArgumentException, "a field still going up");
                Assert.That(() => b.Claim(field, JobKind.Forager, Hour), Throws.ArgumentException);
                Assert.That(() => b.Claim(field, (JobKind)99, Hour), Throws.ArgumentException);
                Assert.That(() => b.ShareOf(house, JobKind.Builder), Throws.ArgumentException);
                Assert.That(() => b.Claim(null!, JobKind.Builder, Hour), Throws.ArgumentNullException);

                // Out of range.
                Assert.That(() => b.Claim(field, JobKind.Builder, 0L), Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(() => b.Claim(field, JobKind.Builder, field.LabourTicks + 1L), Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(() => b.Credit(field.Anchor, JobKind.Builder, Hour, w.Stores), Throws.TypeOf<System.ArgumentOutOfRangeException>(), "nothing claimed");
                Assert.That(() => b.Unclaim(field.Anchor, JobKind.Builder, Hour), Throws.TypeOf<System.ArgumentOutOfRangeException>(), "nothing claimed");

                // Nowhere.
                Assert.That(() => b.Credit(new WorldPosition(0, 0), JobKind.Builder, Hour, w.Stores), Throws.InvalidOperationException);
                Assert.That(() => b.Unclaim(new WorldPosition(0, 0), JobKind.Builder, Hour), Throws.InvalidOperationException);
                Assert.That(() => b.Credit(field.Anchor, JobKind.Builder, Hour, null!), Throws.ArgumentNullException);
            });

            // A refused write changed nothing.
            Assert.That((field.Claimed, field.Worked), Is.EqualTo((0L, 0L)));

            b.Claim(field, JobKind.Builder, Hour);
            Assert.That(() => b.Credit(field.Anchor, JobKind.Builder, Hour + 1L, w.Stores), Throws.TypeOf<System.ArgumentOutOfRangeException>(), "more than was claimed");
            Assert.That((field.Claimed, field.Worked), Is.EqualTo((Hour, 0L)));
        }

        [Test]
        public void A_farmer_who_never_came_home_gives_back_today_s_claim_but_not_yesterday_s()
        {
            var w = new BuildingsWorld();
            w.Wood(500);
            w.BuildNext();
            w.BuildNext();
            var field = w.BuildNext();

            w.Buildings.Claim(field, JobKind.Farmer, Buildings.ShareTicks);
            w.Buildings.Unclaim(field.Anchor, JobKind.Farmer, Buildings.ShareTicks);
            Assert.That(field.ClaimedToday, Is.Zero);

            w.Buildings.Claim(field, JobKind.Farmer, Buildings.ShareTicks);
            w.World.Clock.AdvanceTo(w.World.Now.Plus(SimulationTime.TicksPerDay), w.World.Router);
            w.Buildings.Unclaim(field.Anchor, JobKind.Farmer, Buildings.ShareTicks);

            Assert.Multiple(() =>
            {
                Assert.That(field.ClaimedToday, Is.EqualTo(Buildings.ShareTicks), "yesterday's count, left as it was");
            });

            // Asking about today turns the count over.
            w.Buildings.ShareOf(field, JobKind.Farmer);

            Assert.Multiple(() =>
            {
                Assert.That(field.CountsDay, Is.EqualTo(w.World.Now.DayNumber));
                Assert.That(field.ClaimedToday, Is.Zero, "today starts afresh");
            });
        }

        [Test]
        public void Grain_in_store_is_food()
        {
            var w = new BuildingsWorld();
            var before = w.World.Hunger.DaysOfFood(w.Settlement);
            w.Stores.Gather(ResourceKind.Grain, w.Living);

            Assert.That(w.World.Hunger.DaysOfFood(w.Settlement), Is.EqualTo(before + PrimitiveTier.MealsPerGrain));
        }

        // Nobody has any skill at all.
        private sealed class NoSkills : ISkillSource
        {
            public SkillTier BestIn(ICommunity community, Capability capability) => SkillTier.None;
        }
    }
}
