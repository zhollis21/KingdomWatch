using System.Collections.Generic;
using System.Linq;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Construction;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Needs;
using KingdomWatch.Core.Traversal;
using KingdomWatch.Core.Work;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Construction
{
    // Storage capacity, and the Granary and Woodshed that raise it (#98).
    [TestFixture]
    public sealed class StorageTests
    {
        [Test]
        public void Each_kind_has_a_base_and_only_its_storage_adds_to_it()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Storage.BaseOf(ResourceKind.Food), Is.EqualTo(Storage.BaseFood));
                Assert.That(Storage.BaseOf(ResourceKind.Grain), Is.EqualTo(Storage.BaseGrain));
                Assert.That(Storage.BaseOf(ResourceKind.Wood), Is.EqualTo(Storage.BaseWood));
                Assert.That(Storage.BaseOf(ResourceKind.Stone), Is.EqualTo(Storage.BaseStone));
                Assert.That(() => Storage.BaseOf(ResourceKind.None), Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(() => Storage.BaseOf((ResourceKind)99), Throws.TypeOf<System.ArgumentOutOfRangeException>());

                Assert.That(Storage.AddedBy(BuildingKind.Granary, ResourceKind.Food), Is.EqualTo(Storage.GranaryFood));
                Assert.That(Storage.AddedBy(BuildingKind.Granary, ResourceKind.Grain), Is.EqualTo(Storage.GranaryGrain));
                Assert.That(Storage.AddedBy(BuildingKind.Granary, ResourceKind.Wood), Is.Zero);
                Assert.That(Storage.AddedBy(BuildingKind.Woodshed, ResourceKind.Wood), Is.EqualTo(Storage.WoodshedWood));
                Assert.That(Storage.AddedBy(BuildingKind.Woodshed, ResourceKind.Food), Is.Zero);
                Assert.That(Storage.AddedBy(BuildingKind.House, ResourceKind.Food), Is.Zero);
                Assert.That(() => Storage.AddedBy(BuildingKind.None, ResourceKind.Food), Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(() => Storage.AddedBy((BuildingKind)99, ResourceKind.Food), Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(() => Storage.AddedBy(BuildingKind.Granary, ResourceKind.None), Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(() => Storage.AddedBy(BuildingKind.Granary, (ResourceKind)99), Throws.TypeOf<System.ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void A_store_within_a_tenth_of_its_capacity_is_full()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Storage.IsFull(449, 500), Is.False);
                Assert.That(Storage.IsFull(450, 500), Is.True);
                Assert.That(Storage.IsFull(89, 100), Is.False);
                Assert.That(Storage.IsFull(90, 100), Is.True);
                Assert.That(Storage.IsFull(600, 500), Is.True);
                Assert.That(Storage.IsFull(0, 0), Is.True, "nothing to hold is full");
                Assert.That(Storage.IsFull(long.MaxValue, long.MaxValue), Is.True, "no overflow at the top of the range (#98 review)");
                Assert.That(Storage.IsFull(long.MaxValue - (long.MaxValue / 10) - 1, long.MaxValue), Is.False);
                Assert.That(Storage.IsFull(long.MaxValue - (long.MaxValue / 10), long.MaxValue), Is.True);
                Assert.That(() => Storage.IsFull(-1, 500), Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(() => Storage.IsFull(0, -1), Throws.TypeOf<System.ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void What_is_past_capacity_is_spilled_and_audited()
        {
            var stores = new ResourceLedger();
            stores.Gather(ResourceKind.Wood, 260);

            Storage.Spill(stores, ResourceKind.Wood, 250);
            Storage.Spill(stores, ResourceKind.Wood, 250);
            Storage.Spill(stores, ResourceKind.Wood, 300);

            Assert.Multiple(() =>
            {
                Assert.That(stores.Available(ResourceKind.Wood), Is.EqualTo(250));
                Assert.That(stores.Flows(ResourceKind.Wood).Destroyed, Is.EqualTo(10L), "once, and nothing from a store with room");
                Assert.That(stores.AuditBalances(), Is.True);
                Assert.That(() => Storage.Spill(null!, ResourceKind.Wood, 0), Throws.ArgumentNullException);
                Assert.That(() => Storage.Spill(stores, (ResourceKind)99, 0), Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(() => Storage.Spill(stores, ResourceKind.Wood, long.MinValue), Throws.TypeOf<System.ArgumentOutOfRangeException>(), "not a silent no-op from overflow (#98 review)");
                Assert.That(stores.Available(ResourceKind.Wood), Is.EqualTo(250), "a refused spill destroys nothing");
            });
        }

        [TestCase(0L, 0, 0L)]
        [TestCase(250L, 0, 0L)]
        [TestCase(251L, 0, 1L)]
        [TestCase(251L, 1, 0L)]
        [TestCase(500L, 0, 250L)]
        [TestCase(501L, 0, 250L)]
        [TestCase(501L, 1, 1L)]
        [TestCase(625L, 1, 125L)]
        [TestCase(long.MaxValue, 0, 250L)]
        [TestCase(long.MaxValue, int.MaxValue, 250L)]
        [TestCase(1000L, int.MaxValue, 0L)]
        public void Wood_past_the_base_fills_the_woodsheds_in_turn(long wood, int order, long held)
        {
            Assert.That(Storage.WoodInShed(wood, order), Is.EqualTo(held));
        }

        [Test]
        public void Woodshed_shares_refuse_negative_wood_or_order()
        {
            Assert.Multiple(() =>
            {
                Assert.That(() => Storage.WoodInShed(-1L, 0), Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(() => Storage.WoodInShed(0L, -1), Throws.TypeOf<System.ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void A_band_has_the_base_and_a_settlement_what_its_finished_storage_adds()
        {
            var w = new BuildingsWorld();
            var band = w.World.AddBand(4, new WorldPosition(5, 5));
            w.Wood(1000);
            w.BuildNext();
            WaitForWoodshed(w);

            Assert.Multiple(() =>
            {
                Assert.That(w.Buildings.CapacityOf(band.Id, ResourceKind.Wood), Is.EqualTo(Storage.BaseWood));
                Assert.That(w.Buildings.CapacityOf(w.Settlement.Id, ResourceKind.Wood), Is.EqualTo(Storage.BaseWood), "not until it stands");
                Assert.That(w.Buildings.CapacityOf(w.Settlement.Id, ResourceKind.Food), Is.EqualTo(Storage.BaseFood));
                Assert.That(() => w.Buildings.CapacityOf(w.Settlement.Id, ResourceKind.None), Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(() => w.Buildings.CapacityOf(band.Id, (ResourceKind)99), Throws.TypeOf<System.ArgumentOutOfRangeException>());
            });

            w.Finish(w.Latest!);

            Assert.That(w.Buildings.CapacityOf(w.Settlement.Id, ResourceKind.Wood), Is.EqualTo(Storage.BaseWood + Storage.WoodshedWood));
        }

        [Test]
        public void A_full_woodpile_short_of_the_winter_wants_a_woodshed_before_more_houses()
        {
            var w = new BuildingsWorld();
            w.Wood(1000);
            var house = w.BuildNext();
            var shed = WaitForWoodshed(w);

            Assert.Multiple(() =>
            {
                Assert.That(house.Kind, Is.EqualTo(BuildingKind.House), "a Woodshed needs a house");
                Assert.That(shed.Kind, Is.EqualTo(BuildingKind.Woodshed));
                Assert.That(w.Stores.Flows(ResourceKind.Wood).Embodied, Is.EqualTo(BuildingTable.Of(BuildingKind.House).Wood + BuildingTable.Of(BuildingKind.Woodshed).Wood));
            });
        }

        [Test]
        public void A_woodpile_with_room_wants_no_woodshed()
        {
            var w = new BuildingsWorld();
            w.Wood(1000);
            w.BuildNext();
            Fed(w);
            w.Stores.Consume(ResourceKind.Wood, w.Stores.Available(ResourceKind.Wood) - ((Storage.BaseWood * 9 / 10) - 1));

            w.Dawn();

            Assert.That(w.Latest?.Kind, Is.Not.EqualTo(BuildingKind.Woodshed));
        }

        [Test]
        public void Storage_is_built_with_the_winters_fuel()
        {
            // Forty people, a full woodpile in summer: the winter's fuel is
            // more than the woodpile less a house, so no house is built, but
            // a Woodshed is - what it is for is the fuel that does not fit.
            var w = new BuildingsWorld(people: 40);
            w.Stores.Gather(ResourceKind.Food, w.Living * 200);
            w.Wood(1000);
            w.BuildNext();

            w.World.AdvanceTo(new SimulationTime(SimulationTime.TicksPerDay * SimulationTime.DaysPerSeason));
            w.Stores.Consume(ResourceKind.Wood, w.Stores.Available(ResourceKind.Wood) - Storage.BaseWood);
            var hearths = Warmth.CountHearths(w.Settlement.Members, w.World.People, new List<EntityId>());
            var reserve = hearths * Warmth.FuelPerFire * (int)SimulationTime.DaysPerSeason;

            w.Dawn();

            Assert.Multiple(() =>
            {
                Assert.That(Storage.BaseWood - reserve, Is.LessThan(BuildingTable.Of(BuildingKind.House).Wood), "a house is not affordable");
                Assert.That(w.Latest?.Kind, Is.EqualTo(BuildingKind.Woodshed));
            });
        }

        [Test]
        public void A_hungry_village_with_its_grain_full_builds_a_granary_not_a_field()
        {
            var w = new BuildingsWorld();
            w.Wood(500);
            w.BuildNext();
            var barn = w.BuildNext();
            w.BuildNext();
            w.Stores.Consume(ResourceKind.Wood, w.Stores.Available(ResourceKind.Wood) - 100);
            w.Stores.Gather(ResourceKind.Grain, Storage.BaseGrain);

            var granary = w.BuildNext();

            Assert.Multiple(() =>
            {
                Assert.That(barn.Kind, Is.EqualTo(BuildingKind.Barn));
                Assert.That(w.Buildings.IsFoodShort(w.Settlement, w.Living), Is.True, "still short: a full Grain store is not a year");
                Assert.That(granary.Kind, Is.EqualTo(BuildingKind.Granary));
                Assert.That(w.Buildings.CapacityOf(w.Settlement.Id, ResourceKind.Grain), Is.EqualTo(Storage.BaseGrain + Storage.GranaryGrain));
            });
        }

        [Test]
        public void A_fed_village_with_its_grain_full_wants_no_granary()
        {
            // Grain has no want of its own: counted as demand, a full Grain
            // store built Granary after Granary for the Farmers to fill.
            var w = new BuildingsWorld();
            w.Wood(500);
            w.BuildNext();
            w.BuildNext();
            Fed(w);
            w.Stores.Consume(ResourceKind.Wood, w.Stores.Available(ResourceKind.Wood) - 100);

            w.Dawn();

            Assert.That(w.Buildings.All.Any(b => b.Kind == BuildingKind.Granary), Is.False);
        }

        [Test]
        public void A_hungry_village_with_its_food_full_builds_a_granary_once_there_is_a_barn()
        {
            // Twenty people want 800 meals going into a winter; the Food
            // holds 500.
            var w = new BuildingsWorld(people: 20);
            w.Wood(1000);
            w.BuildNext();
            w.Stores.Gather(ResourceKind.Food, Storage.BaseFood);

            var barn = w.BuildNext();
            var granary = w.BuildNext();

            Assert.Multiple(() =>
            {
                Assert.That(barn.Kind, Is.EqualTo(BuildingKind.Barn), "no Granary without a barn");
                Assert.That(granary.Kind, Is.EqualTo(BuildingKind.Granary), "rather than a field");
            });
        }

        [Test]
        public void A_fed_village_with_its_food_full_short_of_the_winter_builds_a_granary_before_houses()
        {
            // Fields enough that twenty people are not short, then the Food
            // full: the Food's room alone wants the Granary, ahead of the
            // households still in tents. Wood too little for a Woodshed.
            var w = new BuildingsWorld(people: 20);
            w.Wood(1000);

            while (w.Buildings.IsFoodShort(w.Settlement, w.Living) || w.Buildings.All.Count == 0)
            {
                w.BuildNext();
            }

            w.Stores.Gather(ResourceKind.Food, Storage.BaseFood);
            w.Stores.Consume(ResourceKind.Wood, w.Stores.Available(ResourceKind.Wood) - 100);
            var granary = w.BuildNext();

            Assert.Multiple(() =>
            {
                Assert.That(w.Buildings.All, Has.None.Matches<Building>(b => b.Kind == BuildingKind.Granary && b != granary));
                Assert.That(granary.Kind, Is.EqualTo(BuildingKind.Granary));
            });
        }

        [Test]
        public void A_granary_waits_for_its_stone()
        {
            // #166: storage takes any wood in stock, but its stone it must
            // have, as any building.
            var w = new BuildingsWorld(people: 20);
            w.Wood(1000);

            while (w.Buildings.IsFoodShort(w.Settlement, w.Living) || w.Buildings.All.Count == 0)
            {
                w.BuildNext();
            }

            w.Stores.Gather(ResourceKind.Food, Storage.BaseFood);
            w.Stores.Consume(ResourceKind.Wood, w.Stores.Available(ResourceKind.Wood) - 100);
            var spec = BuildingTable.Of(BuildingKind.Granary);
            w.StoneLeft(spec.Stone - 1);
            var count = w.Buildings.All.Count;

            w.DawnAsIs();
            Assert.That(w.Buildings.All.Count, Is.EqualTo(count), "one stone short");

            w.Stone(1);
            w.DawnAsIs();
            Assert.That(w.Latest?.Kind, Is.EqualTo(BuildingKind.Granary));
        }

        [Test]
        public void Farmers_leave_the_fields_while_the_grain_is_full()
        {
            var w = new BuildingsWorld();
            w.Wood(500);
            w.BuildNext();
            w.BuildNext();
            w.BuildNext();

            var before = w.Buildings.TryFieldWork(w.Settlement.Id, out _, out _, out _);
            w.Stores.Gather(ResourceKind.Grain, Storage.BaseGrain);
            var full = w.Buildings.TryFieldWork(w.Settlement.Id, out var field, out _, out _);

            Assert.Multiple(() =>
            {
                Assert.That(before, Is.True);
                Assert.That(full, Is.False);
                Assert.That(field, Is.Null);
                Assert.That(w.Buildings.TryFieldWork(new EntityId(EntityKind.Settlement, 999UL), out _, out _, out _), Is.False, "a settlement with no fields, known or not");
            });
        }

        [Test]
        public void A_harvest_day_past_the_grains_capacity_is_partly_lost()
        {
            var w = new BuildingsWorld();
            w.Stores.Gather(ResourceKind.Food, w.Living * 100);
            w.Wood(500);
            w.BuildNext();
            w.BuildNext();
            var field = w.BuildNext();
            WorkFieldDays(w, field, Buildings.TendingDays);
            w.Stores.Gather(ResourceKind.Grain, Storage.BaseGrain - 1);

            WorkFieldDays(w, field, 1);

            Assert.Multiple(() =>
            {
                Assert.That(w.Stores.Available(ResourceKind.Grain), Is.EqualTo(Storage.BaseGrain));
                Assert.That(w.Stores.Flows(ResourceKind.Grain).Destroyed, Is.EqualTo(Buildings.GrainPerHarvestDay - 1L));
                Assert.That(w.Stores.AuditBalances(), Is.True);
            });
        }

        private static void WorkFieldDays(BuildingsWorld w, Building field, int days)
        {
            for (var i = 0; i < days; i++)
            {
                w.Buildings.Claim(field, JobKind.Farmer, Buildings.FieldDayTicks);
                w.Buildings.Credit(field.Anchor, JobKind.Farmer, Buildings.FieldDayTicks, w.Stores);
                w.World.Clock.AdvanceTo(w.World.Now.Plus(SimulationTime.TicksPerDay), w.World.Router);
            }
        }

        [Test]
        public void Clearing_wood_past_capacity_is_lost()
        {
            var w = new BuildingsWorld(paint: grid =>
            {
                for (var y = 0; y < BuildingsWorld.Size; y++)
                {
                    for (var x = 0; x < BuildingsWorld.Size; x++)
                    {
                        grid.Set(new WorldPosition(x, y), TerrainKind.Forest);
                    }
                }
            });
            w.Wood(Storage.BaseWood);
            w.Dawn();
            var house = w.Buildings.All.Single();

            w.Finish(house);

            Assert.Multiple(() =>
            {
                Assert.That(house.ClearCuts, Is.GreaterThan(0));
                Assert.That(w.Stores.Available(ResourceKind.Wood), Is.EqualTo(Storage.BaseWood));
                Assert.That(w.Stores.Flows(ResourceKind.Wood).Destroyed, Is.GreaterThan(0L));
                Assert.That(w.Stores.AuditBalances(), Is.True);
            });
        }

        // A year of food in store, as Grain: not short, and nothing else wanted of it.
        [TestCase(true)]
        [TestCase(false)]
        public void A_woodshed_stands_by_the_forest_its_woodcutters_work(bool east)
        {
            // A wood on one side of the village, as far out as the barns'
            // ring: the shed goes to it, not among the houses (#167).
            var w = new BuildingsWorld(paint: grid => PaintWood(grid, east));
            w.Wood(1000);
            w.BuildNext();
            var shed = WaitForWoodshed(w);
            var door = Buildings.DoorOf(BuildingKind.Woodshed, shed.Anchor);
            var wood = new WorldPosition(east ? WoodEast : WoodWest, BuildingsWorld.Centre.Y);

            Assert.Multiple(() =>
            {
                Assert.That(shed.Kind, Is.EqualTo(BuildingKind.Woodshed));
                Assert.That(Chebyshev(door, wood), Is.LessThan(Chebyshev(door, BuildingsWorld.Centre)), shed + " is nearer the wood than the centre");
                Assert.That(east ? door.X > BuildingsWorld.Centre.X : door.X < BuildingsWorld.Centre.X, shed + " is on the wood's side");
            });
        }

        [Test]
        public void A_woodshed_with_no_forest_in_reach_goes_on_the_barns_ring()
        {
            var w = new BuildingsWorld();
            w.Wood(1000);
            w.BuildNext();
            var shed = WaitForWoodshed(w);

            Assert.Multiple(() =>
            {
                Assert.That(shed.Kind, Is.EqualTo(BuildingKind.Woodshed));
                Assert.That(Chebyshev(Buildings.DoorOf(BuildingKind.Woodshed, shed.Anchor), BuildingsWorld.Centre), Is.GreaterThanOrEqualTo(Buildings.MinimumRing - 1), shed + " is on the outskirts");
            });
        }

        [Test]
        public void A_granary_stands_by_the_barn_not_on_the_ring_the_houses_have_pushed_out()
        {
            // A barn on the ring of the first house, then houses further
            // out, then a Granary: by the barn, not on the ring as it now
            // stands (#167).
            var w = new BuildingsWorld(people: 30, size: 60);
            w.Wood(5000);
            w.BuildNext();
            var barn = w.BuildNext();
            Fed(w);

            for (var i = 0; i < 6; i++)
            {
                w.BuildNext();
            }

            w.Stores.Consume(ResourceKind.Grain, w.Stores.Available(ResourceKind.Grain));
            w.Stores.Consume(ResourceKind.Wood, w.Stores.Available(ResourceKind.Wood) - 100);
            w.Stores.Gather(ResourceKind.Grain, Storage.BaseGrain);

            var granary = w.BuildNext();
            var door = Buildings.DoorOf(BuildingKind.Granary, granary.Anchor);
            var barnSpec = BuildingTable.Of(BuildingKind.Barn);
            var barnMiddle = new WorldPosition(barn.Anchor.X + (barnSpec.Width / 2), barn.Anchor.Y + (barnSpec.Height / 2));

            Assert.Multiple(() =>
            {
                Assert.That(barn.Kind, Is.EqualTo(BuildingKind.Barn));
                Assert.That(granary.Kind, Is.EqualTo(BuildingKind.Granary));
                Assert.That(Chebyshev(door, barnMiddle), Is.LessThanOrEqualTo(barnSpec.Width), granary + " stands by " + barn);
            });
        }

        [Test]
        public void A_second_granary_joins_the_first_in_one_yard()
        {
            var w = new BuildingsWorld(people: 30, size: 60);
            w.Wood(5000);
            w.BuildNext();
            var barn = w.BuildNext();
            w.Stores.Consume(ResourceKind.Wood, w.Stores.Available(ResourceKind.Wood) - 100);
            w.Stores.Gather(ResourceKind.Grain, Storage.BaseGrain - w.Stores.Available(ResourceKind.Grain));
            var first = w.BuildNext();
            w.Stores.Gather(ResourceKind.Grain, Storage.GranaryGrain);

            var second = w.BuildNext();
            var spec = BuildingTable.Of(BuildingKind.Granary);
            var door = Buildings.DoorOf(BuildingKind.Granary, second.Anchor);
            var firstMiddle = new WorldPosition(first.Anchor.X + (spec.Width / 2), first.Anchor.Y + (spec.Height / 2));

            Assert.Multiple(() =>
            {
                Assert.That(barn.Kind, Is.EqualTo(BuildingKind.Barn));
                Assert.That(first.Kind, Is.EqualTo(BuildingKind.Granary));
                Assert.That(second.Kind, Is.EqualTo(BuildingKind.Granary));
                Assert.That(Chebyshev(door, firstMiddle), Is.LessThanOrEqualTo(spec.Width + 1), second + " stands beside " + first);
            });
        }

        // A wood five cells deep across the map, its near edge as far from
        // the centre as the barns' ring.
        private const int WoodEast = 33;
        private const int WoodWest = 6;

        private static void PaintWood(TerrainGrid grid, bool east)
        {
            for (var y = 0; y < grid.Height; y++)
            {
                for (var dx = 0; dx < 5; dx++)
                {
                    grid.Set(new WorldPosition(east ? WoodEast + dx : WoodWest - dx, y), TerrainKind.Forest);
                }
            }
        }

        private static int Chebyshev(WorldPosition a, WorldPosition b) =>
            System.Math.Max(System.Math.Abs(a.X - b.X), System.Math.Abs(a.Y - b.Y));

        private static void Fed(BuildingsWorld w) =>
            w.Stores.Gather(ResourceKind.Grain, (int)(Buildings.YearlyNeed(w.Living) / PrimitiveTier.MealsPerGrain));

        // Fed, so no barn comes first, and approves what is next: the
        // Woodshed a full woodpile and the households' hearths want.
        private static Building WaitForWoodshed(BuildingsWorld w)
        {
            Fed(w);
            w.Dawn();
            return w.Latest!;
        }
    }
}
