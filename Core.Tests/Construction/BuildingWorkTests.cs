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
