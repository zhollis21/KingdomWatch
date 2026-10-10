using System.Linq;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Construction;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Land;
using KingdomWatch.Core.Lifecycle;
using KingdomWatch.Core.Needs;
using KingdomWatch.Core.Traversal;
using KingdomWatch.Core.Work;
using KingdomWatch.Harness;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Construction
{
    // Buildings driven by the real work day: Builders and Farmers picked by
    // Jobs, meals milled by Hunger, and what the hash and the validator
    // make of it all (#100).
    [TestFixture]
    public sealed class BuildingWorkTests
    {
        private static readonly long Day = SimulationTime.TicksPerDay;

        // A settled world left to run: stocked so that only building and
        // farming are short, on plains with no bushes, so food is short too.
        private static World Working(out KingdomWatch.Core.Settlements.Settlement settlement)
        {
            var w = new BuildingsWorld(people: 20);
            w.World.Jobs.Track(w.Settlement);
            w.Wood(KingdomWatch.Core.Work.Jobs.WoodCap + 2000);
            w.Stores.Gather(ResourceKind.Stone, KingdomWatch.Core.Work.Jobs.StoneCap);
            w.Stores.Gather(ResourceKind.Food, w.Living * 60);
            settlement = w.Settlement;
            return w.World;
        }

        [Test]
        public void Builders_put_up_a_house_and_a_barn_and_Farmers_bring_in_grain()
        {
            var world = Working(out var settlement);

            world.Advance(60L * Day);

            var mine = world.Buildings.All.Where(b => b.Settlement == settlement.Id).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(mine.Any(b => b.Kind == BuildingKind.House && b.IsComplete), Is.True);
                Assert.That(mine.Any(b => b.Kind == BuildingKind.Barn && b.IsComplete), Is.True);
                Assert.That(mine.Any(b => b.Kind == BuildingKind.Field && b.IsComplete), Is.True);
                Assert.That(settlement.SharedSupplies.Flows(ResourceKind.Grain).Gathered, Is.GreaterThan(0));
            });
        }

        [Test]
        public void Building_work_keeps_every_rule_and_the_ledger_audits()
        {
            var world = Working(out var settlement);
            var validator = new WorldValidator();

            for (var i = 0; i < 12; i++)
            {
                world.Advance(5L * Day);
                validator.CheckBuildings(world.Buildings, world.Founding, world.Households, world.Grid, world.Clock);
            }

            Assert.Multiple(() =>
            {
                Assert.That(validator.Findings, Is.Empty);
                Assert.That(settlement.SharedSupplies.AuditBalances(), Is.True);
            });
        }

        [Test]
        public void The_same_seed_builds_the_same_village_and_the_hash_sees_buildings()
        {
            var a = Working(out _);
            var b = Working(out _);
            var before = a.Hash();

            a.Advance(20L * Day);
            b.Advance(20L * Day);

            Assert.Multiple(() =>
            {
                Assert.That(a.Buildings.All, Is.Not.Empty);
                Assert.That(a.Hash(), Is.EqualTo(b.Hash()));
                Assert.That(a.Hash(), Is.Not.EqualTo(before));
            });
        }

        [Test]
        public void The_buildings_section_moves_with_each_building_s_work()
        {
            var w = new BuildingsWorld();
            w.Wood(100);
            w.Dawn();
            var house = w.Buildings.All.Single();
            var before = new KingdomWatch.Core.Validation.WorldHash().AddBuildings(w.Buildings).Value;

            w.Buildings.Claim(house, JobKind.Builder, SimulationTime.TicksPerHour);
            var claimed = new KingdomWatch.Core.Validation.WorldHash().AddBuildings(w.Buildings).Value;
            w.Buildings.Credit(house.Anchor, JobKind.Builder, SimulationTime.TicksPerHour, w.Stores);
            var worked = new KingdomWatch.Core.Validation.WorldHash().AddBuildings(w.Buildings).Value;

            Assert.Multiple(() =>
            {
                Assert.That(claimed, Is.Not.EqualTo(before));
                Assert.That(worked, Is.Not.EqualTo(before), "worked, with nothing left claimed");
                Assert.That(worked, Is.Not.EqualTo(claimed));
            });
        }

        [Test]
        public void A_meal_with_no_food_mills_grain_five_meals_to_one()
        {
            var w = new BuildingsWorld(people: 3);
            w.Stores.Consume(ResourceKind.Food, w.Stores.Available(ResourceKind.Food));
            w.Stores.Gather(ResourceKind.Grain, 1);

            Assert.That(Hunger.MealsInStore(w.Stores), Is.EqualTo(PrimitiveTier.MealsPerGrain));

            w.World.Advance(Day);

            Assert.Multiple(() =>
            {
                Assert.That(w.Stores.Flows(ResourceKind.Grain).Consumed, Is.EqualTo(1));
                Assert.That(w.Stores.Flows(ResourceKind.Food).Produced, Is.EqualTo(PrimitiveTier.MealsPerGrain));
                Assert.That(w.Stores.Available(ResourceKind.Food), Is.EqualTo(PrimitiveTier.MealsPerGrain - w.Living));
                Assert.That(w.World.Hunger.IsInFamine(w.Settlement), Is.False);
            });
        }

        [Test]
        public void The_validator_names_a_road_under_a_building()
        {
            var w = new BuildingsWorld();
            w.Wood(100);
            w.Dawn();
            var house = w.Buildings.All.Single();
            var validator = new WorldValidator();

            w.World.Grid.SetRoad(house.Anchor, RoadGrade.Track);
            validator.CheckBuildings(w.Buildings, w.World.Founding, w.World.Households, w.World.Grid, w.World.Clock);

            Assert.That(validator.Findings.Select(f => f.Rule), Is.EqualTo(new[] { ValidationRule.BuildingLane }));
        }

        [Test]
        public void The_validator_names_a_cleared_lane_that_is_not_road_over_cleared_ground()
        {
            // Forest round the camp, so the house's lane runs out through it.
            var w = new BuildingsWorld(paint: grid =>
            {
                for (var y = 0; y < BuildingsWorld.Size; y++)
                {
                    for (var x = 0; x < BuildingsWorld.Size; x++)
                    {
                        var ring = System.Math.Max(System.Math.Abs(x - BuildingsWorld.Centre.X), System.Math.Abs(y - BuildingsWorld.Centre.Y));

                        if (ring > Buildings.CampYardRadius && ring <= Buildings.CampYardRadius + 4)
                        {
                            grid.Set(new WorldPosition(x, y), TerrainKind.Forest);
                        }
                    }
                }
            });
            w.Wood(100);
            w.Dawn();
            var house = w.Buildings.All.Single();
            Assert.That(house.Lane, Has.Count.GreaterThanOrEqualTo(2), "a lane to break");
            w.Finish(house);
            var validator = new WorldValidator();

            validator.CheckBuildings(w.Buildings, w.World.Founding, w.World.Households, w.World.Grid, w.World.Clock);
            Assert.That(validator.Findings, Is.Empty, "a laid lane is sound");

            // One cell lifted, another planted over.
            w.World.Grid.SetRoad(house.Lane[0], RoadGrade.None);
            w.World.Grid.Set(house.Lane[1], TerrainKind.Forest);
            validator.Reset().CheckBuildings(w.Buildings, w.World.Founding, w.World.Households, w.World.Grid, w.World.Clock);

            Assert.That(validator.Findings.Select(f => f.Rule), Is.EqualTo(new[] { ValidationRule.BuildingLane, ValidationRule.BuildingLane }));
        }

        [Test]
        public void The_validator_names_a_cleared_square_that_is_not_road_over_cleared_ground()
        {
            var bush = new WorldPosition(BuildingsWorld.Centre.X + 1, BuildingsWorld.Centre.Y);
            var rock = new WorldPosition(BuildingsWorld.Centre.X - 1, BuildingsWorld.Centre.Y);
            var w = new BuildingsWorld(paint: grid =>
            {
                grid.Set(bush, TerrainKind.Scrub);
                grid.Set(rock, TerrainKind.Rocks);
            });
            w.Wood(100);
            w.Dawn();
            var house = w.Buildings.All.Single();
            Assert.That(house.Square, Is.EquivalentTo(new[] { bush, rock }));
            w.Finish(house);
            var validator = new WorldValidator();

            validator.CheckBuildings(w.Buildings, w.World.Founding, w.World.Households, w.World.Grid, w.World.Clock);
            Assert.That(validator.Findings, Is.Empty, "a paved square is sound");

            // A bush grown back and an outcrop risen again, under the road.
            w.World.Grid.Set(bush, TerrainKind.Scrub);
            w.World.Grid.Set(rock, TerrainKind.Rocks);
            validator.Reset().CheckBuildings(w.Buildings, w.World.Founding, w.World.Households, w.World.Grid, w.World.Clock);

            Assert.That(validator.Findings.Select(f => f.Rule), Is.EqualTo(new[] { ValidationRule.BuildingLane, ValidationRule.BuildingLane }));
        }

        [Test]
        public void The_validator_names_cleared_ground_that_is_not_clear()
        {
            var w = new BuildingsWorld();
            w.Wood(100);
            w.Dawn();
            var house = w.Buildings.All.Single();
            var validator = new WorldValidator();

            // The terrain is anyone's to rewrite; a forest planted on a
            // building's cleared ground is a world that has gone wrong.
            w.World.Grid.Set(house.Anchor, TerrainKind.Forest);
            validator.CheckBuildings(w.Buildings, w.World.Founding, w.World.Households, w.World.Grid, w.World.Clock);

            Assert.That(validator.Findings.Select(f => f.Rule), Is.EqualTo(new[] { ValidationRule.BuildingFootprint }));
        }

        [Test]
        public void The_validator_names_a_building_whose_settlement_it_cannot_find()
        {
            var w = new BuildingsWorld();
            // A world that has founded nothing.
            var stranger = new World(1UL, new TerrainGrid(4, 4, TerrainKind.Plains), DemographicSettings.Default);
            w.Wood(100);
            w.Dawn();
            var validator = new WorldValidator();

            validator.CheckBuildings(w.Buildings, stranger.Founding, w.World.Households, w.World.Grid, w.World.Clock);

            Assert.That(validator.Findings.Any(f => f.Rule == ValidationRule.BuildingReference), Is.True);
        }

        [Test]
        public void Nobody_gathers_on_ground_approved_for_a_building()
        {
            // The #148 review: approved ground stayed ordinary scrub until it
            // was cleared, so foragers picked it while it waited, racing the
            // Builders. Rocks everywhere but the camp, one patch of scrub
            // beside it - the only ground a house fits, and the nearest
            // forage - and another further out.
            var w = new BuildingsWorld(paint: grid =>
            {
                for (var y = 0; y < BuildingsWorld.Size; y++)
                {
                    for (var x = 0; x < BuildingsWorld.Size; x++)
                    {
                        var near = x >= 24 && x <= 28 && y >= 20 && y <= 24;
                        var far = x >= 33 && x <= 37 && y >= 20 && y <= 24;
                        grid.Set(new WorldPosition(x, y), near || far ? TerrainKind.Scrub : TerrainKind.Rocks);
                    }
                }

                grid.Set(BuildingsWorld.Centre, TerrainKind.Plains);

                // A lane across the camp yard to the near patch.
                for (var x = BuildingsWorld.Centre.X + 1; x < 24; x++)
                {
                    grid.Set(new WorldPosition(x, BuildingsWorld.Centre.Y), TerrainKind.Plains);
                }

                // A strip under the near patch, back to the yard, for the
                // house's door and its lane (#23).
                for (var x = BuildingsWorld.Centre.X; x <= 28; x++)
                {
                    grid.Set(new WorldPosition(x, 25), TerrainKind.Plains);
                }

                grid.Set(new WorldPosition(BuildingsWorld.Centre.X, 24), TerrainKind.Plains);
            });
            w.Wood(KingdomWatch.Core.Work.Jobs.WoodCap + 2000);
            w.Stores.Gather(ResourceKind.Stone, KingdomWatch.Core.Work.Jobs.StoneCap);
            w.Dawn();
            var house = w.Buildings.All.Single();
            Assert.That(house.ClearTicks, Is.GreaterThan(0L));
            w.Stores.Consume(ResourceKind.Food, w.Stores.Available(ResourceKind.Food));

            w.World.Jobs.Track(w.Settlement);
            w.World.AdvanceTo(new SimulationTime(7L * SimulationTime.TicksPerHour));

            var foraging = w.Settlement.Members.Where(m => w.World.Jobs.HasTask(m) && w.World.Jobs.TaskOf(m).Job == JobKind.Forager).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(foraging, Is.Not.Empty, "food is short, so people forage");
                Assert.That(foraging.Select(m => w.World.Jobs.TaskOf(m).Destination).Where(house.Covers), Is.Empty);
                Assert.That(w.World.Buildings.IsReserved(house.Anchor), Is.True);
            });
        }

        [Test]
        public void Foragers_out_when_ground_is_approved_find_somewhere_else_next_trip()
        {
            // Approval runs after the dawn picks, so the day's forage site
            // was found before the ground under it was reserved.
            var w = new BuildingsWorld(paint: grid =>
            {
                for (var y = 0; y < BuildingsWorld.Size; y++)
                {
                    for (var x = 0; x < BuildingsWorld.Size; x++)
                    {
                        var near = x >= 24 && x <= 28 && y >= 20 && y <= 24;
                        var far = x >= 33 && x <= 37 && y >= 20 && y <= 24;
                        grid.Set(new WorldPosition(x, y), near || far ? TerrainKind.Scrub : TerrainKind.Rocks);
                    }
                }

                grid.Set(BuildingsWorld.Centre, TerrainKind.Plains);

                // A lane across the camp yard to the near patch.
                for (var x = BuildingsWorld.Centre.X + 1; x < 24; x++)
                {
                    grid.Set(new WorldPosition(x, BuildingsWorld.Centre.Y), TerrainKind.Plains);
                }

                // A strip under the near patch, back to the yard, for the
                // house's door and its lane (#23).
                for (var x = BuildingsWorld.Centre.X; x <= 28; x++)
                {
                    grid.Set(new WorldPosition(x, 25), TerrainKind.Plains);
                }

                grid.Set(new WorldPosition(BuildingsWorld.Centre.X, 24), TerrainKind.Plains);
            });
            w.Wood(KingdomWatch.Core.Work.Jobs.WoodCap + 2000);
            w.Stores.Gather(ResourceKind.Stone, KingdomWatch.Core.Work.Jobs.StoneCap);
            // Bushes that never run out: only the reservation can move a
            // forager off the site found at dawn.
            w.World.Land.BushPicks = LandCover.MaxClaims;
            w.Stores.Consume(ResourceKind.Food, w.Stores.Available(ResourceKind.Food));
            w.World.Jobs.Track(w.Settlement);

            w.World.AdvanceTo(new SimulationTime(KingdomWatch.Core.Work.Jobs.Dawn + 1L));
            Assert.That(w.World.Jobs.SiteFor(w.Settlement, JobKind.Forager).X, Is.InRange(24, 28), "the near patch, before approval");

            w.Dawn();
            var house = w.Buildings.All.Single();
            var approved = w.World.Now;
            w.World.AdvanceTo(new SimulationTime(15L * SimulationTime.TicksPerHour));

            var later = w.Settlement.Members
                .Where(m => w.World.Jobs.HasTask(m) && w.World.Jobs.TaskOf(m).Job == JobKind.Forager && w.World.Jobs.TaskOf(m).Start > approved)
                .Select(m => w.World.Jobs.TaskOf(m).Destination)
                .ToList();

            Assert.Multiple(() =>
            {
                Assert.That(later, Is.Not.Empty, "trips set out after approval");
                Assert.That(later.Where(house.Covers), Is.Empty);
            });
        }

        [Test]
        public void Grain_in_store_keeps_foragers_home()
        {
            var w = new KingdomWatch.Core.Tests.Work.WorkWorld();
            var band = w.NewBand(KingdomWatch.Core.Tests.Work.WorkWorld.Camp, 0);
            KingdomWatch.Core.Tests.Work.WorkWorld.FillWoodAndStone(band);
            w.JoinAdults(band, 2);

            // Food enough, as Grain: ten days and more, milled.
            band.SharedSupplies.Gather(ResourceKind.Grain, 2 * Hunger.DailyRation * 20 / PrimitiveTier.MealsPerGrain);
            w.AdvanceToDawn();

            Assert.That(w.Jobs.OnDuty(band, JobKind.Forager), Is.Zero);
        }

        [Test]
        public void A_woodcutter_whose_tree_was_cleared_looks_again_rather_than_cut_plains()
        {
            var w = new KingdomWatch.Core.Tests.Work.WorkWorld();
            w.Land.TreeCuts = LandCover.MaxClaims;
            var band = w.NewBand(KingdomWatch.Core.Tests.Work.WorkWorld.Camp, KingdomWatch.Core.Tests.Work.WorkWorld.PlentifulFood(1));
            KingdomWatch.Core.Tests.Work.WorkWorld.FillStone(band);
            var worker = w.JoinAdults(band, 1)[0];
            w.AdvanceToDawn();

            Assert.That(w.Jobs.TaskOf(worker).Destination, Is.EqualTo(KingdomWatch.Core.Tests.Work.WorkWorld.ForestCell));

            // While the first trip is out, the only tree is cleared for good.
            // Home at midday, the woodcutter picks again - from the site found
            // at dawn, which is now plains, and plains count as workable.
            w.Land.Clear(KingdomWatch.Core.Tests.Work.WorkWorld.ForestCell);
            w.AdvanceTo(w.Jobs.TaskOf(worker).End);

            Assert.Multiple(() =>
            {
                Assert.That(w.Jobs.HasTask(worker), Is.False, "nothing left to cut, and nothing else needed");
                Assert.That(w.Jobs.HasSite(band, JobKind.Woodcutter), Is.False);
            });
        }
    }
}
