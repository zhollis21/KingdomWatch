using System.Linq;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Construction;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Traversal;
using KingdomWatch.Core.Work;
using KingdomWatch.Harness;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Construction
{
    // A lane or square cell too far from its footprint to clear from there
    // is cleared by trips of its own, so the walk out to it counts (#180).
    [TestFixture]
    public sealed class FarClearingTests
    {
        // Every house beyond a belt of river four wide round the yard, its
        // lane back through a causeway of forest: trees at the lane's far
        // end, past ClearingReach of the house.
        private static BuildingsWorld Causeway(out Building house)
        {
            var w = new BuildingsWorld(paint: grid => BuildingsWorld.PaintBelt(grid, 4, TerrainKind.Forest));
            w.Wood(100);
            w.Dawn();
            house = w.Buildings.All.Single();
            return w;
        }

        private static int OutOf(Building building, WorldPosition at) =>
            System.Math.Max(
                System.Math.Max(building.Anchor.X - at.X, at.X - (building.Anchor.X + building.Width - 1)),
                System.Math.Max(building.Anchor.Y - at.Y, at.Y - (building.Anchor.Y + building.Height - 1)));

        [Test]
        public void Lane_trees_past_reach_of_the_footprint_are_spots_of_their_own_and_the_near_ones_are_not()
        {
            var w = Causeway(out var house);
            var land = w.World.Land;
            var trees = house.Lane.Where(at => w.World.Grid[at] == TerrainKind.Forest).ToList();
            var far = trees.Where(at => OutOf(house, at) > Buildings.ClearingReach).ToList();

            Assert.That(far, Is.Not.Empty, "the causeway runs past reach of the house");

            Assert.Multiple(() =>
            {
                Assert.That(house.Spots.Select(s => s.At), Is.EqualTo(far), "in the lane's order, and only cells with something on them");
                Assert.That(house.Spots.All(s => ReferenceEquals(s.Building, house)), Is.True);
                Assert.That(house.Spots.Select(s => s.Ticks), Is.EqualTo(far.Select(at => land.CutsLeft(at) * Buildings.TicksPerCut)));
                Assert.That(house.Spots.Select(s => s.Cuts), Is.EqualTo(far.Select(land.CutsLeft)));
                Assert.That(house.Spots.Sum(s => s.Ticks), Is.LessThanOrEqualTo(house.ClearTicks), "a share of the building's clearing");
                Assert.That(house.Spots.All(s => w.Buildings.SpotAt(s.At) == s), Is.True);
                Assert.That(w.Buildings.SpotAt(house.Anchor), Is.Null);
                Assert.That(w.Buildings.WorkPlaceOf(house), Is.EqualTo(far[0]), "the first trip goes out to the first far tree");
                Assert.That(w.Buildings.ShareOf(house, JobKind.Builder), Is.EqualTo(System.Math.Min(Buildings.ShareTicks, house.Spots[0].Ticks)));
            });
        }

        [Test]
        public void Builders_clear_each_far_cell_where_it_is_then_the_footprint_and_lay_the_lane()
        {
            var w = Causeway(out var house);
            var b = w.Buildings;
            var grid = w.World.Grid;
            var spots = house.Spots.ToList();
            var first = spots[0];
            var wood = w.Stores.Available(ResourceKind.Wood);

            // Claimed a share at a time, the way trips set out: the next trip
            // goes to the next cell once nobody else's hours are left here.
            while (first.Left > 0L)
            {
                Assert.That(b.WorkPlaceOf(house), Is.EqualTo(first.At));
                b.Claim(house, JobKind.Builder, b.ShareOf(house, JobKind.Builder));
            }

            Assert.That(b.WorkPlaceOf(house), Is.EqualTo(spots.Count > 1 ? spots[1].At : house.Anchor));
            Assert.That(house.Claimed, Is.EqualTo(first.Ticks));

            b.Credit(first.At, JobKind.Builder, first.Ticks, w.Stores);

            Assert.Multiple(() =>
            {
                Assert.That(first.IsCleared, Is.True);
                Assert.That(grid[first.At], Is.EqualTo(TerrainKind.Plains), "felled where it stood");
                Assert.That(grid.RoadAt(first.At), Is.EqualTo(RoadGrade.None), "laid with the rest of the lane");
                Assert.That(b.IsReserved(first.At), Is.True, "still the lane's");
                Assert.That(b.SpotAt(first.At), Is.Null);
                Assert.That(w.Stores.Available(ResourceKind.Wood) - wood, Is.EqualTo(first.Cuts * Buildings.WoodPerCut), "its own cuts, paid as it clears");
                Assert.That(house.Cleared, Is.False);
                Assert.That(house.Worked, Is.EqualTo(first.Ticks));
            });

            var cuts = house.ClearCuts;
            w.Finish(house);

            Assert.Multiple(() =>
            {
                Assert.That(house.Cleared, Is.True);
                Assert.That(house.IsComplete, Is.True);
                Assert.That(spots.All(s => s.IsCleared), Is.True);
                Assert.That(w.Stores.Available(ResourceKind.Wood) - wood, Is.EqualTo(cuts * Buildings.WoodPerCut), "every cut paid once");

                foreach (var at in house.Lane)
                {
                    Assert.That(grid.RoadAt(at), Is.EqualTo(RoadGrade.Track), at.ToString());
                    Assert.That(grid[at], Is.EqualTo(TerrainKind.Plains), at.ToString());
                    Assert.That(b.IsReserved(at), Is.False, at.ToString());
                }
            });
        }

        [Test]
        public void A_trip_to_a_far_cell_that_never_comes_home_gives_its_hours_back_to_that_cell()
        {
            var w = Causeway(out var house);
            var b = w.Buildings;
            var spot = house.Spots[0];
            var share = b.ShareOf(house, JobKind.Builder);
            b.Claim(house, JobKind.Builder, share);

            Assert.That(spot.Claimed, Is.EqualTo(share));

            b.Unclaim(spot.At, JobKind.Builder, share);

            Assert.Multiple(() =>
            {
                Assert.That(spot.Claimed, Is.Zero);
                Assert.That(house.Claimed, Is.Zero);
                Assert.That(b.WorkPlaceOf(house), Is.EqualTo(spot.At));
                Assert.That(() => b.Unclaim(spot.At, JobKind.Builder, 1L), Throws.TypeOf<System.ArgumentOutOfRangeException>(), "nothing claimed there");
                Assert.That(() => b.Credit(spot.At, JobKind.Builder, 1L, w.Stores), Throws.TypeOf<System.ArgumentOutOfRangeException>(), "nothing claimed there");
                Assert.That(() => b.Credit(spot.At, JobKind.Farmer, 1L, w.Stores), Throws.ArgumentException, "a Farmer clears nothing");
            });
        }

        [Test]
        public void An_anchor_trip_cannot_come_home_with_hours_claimed_out_at_a_far_cell()
        {
            var w = Causeway(out var house);
            var b = w.Buildings;
            b.Claim(house, JobKind.Builder, b.ShareOf(house, JobKind.Builder));

            Assert.Multiple(() =>
            {
                Assert.That(() => b.Credit(house.Anchor, JobKind.Builder, 1L, w.Stores), Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(() => b.Unclaim(house.Anchor, JobKind.Builder, 1L), Throws.TypeOf<System.ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void A_building_with_nothing_far_to_clear_has_no_spots_and_works_at_its_anchor()
        {
            var w = new BuildingsWorld();
            w.Wood(100);
            w.Dawn();
            var house = w.Buildings.All.Single();

            Assert.Multiple(() =>
            {
                Assert.That(house.Spots, Is.Empty);
                Assert.That(w.Buildings.WorkPlaceOf(house), Is.EqualTo(house.Anchor));
                Assert.That(() => w.Buildings.WorkPlaceOf(null!), Throws.ArgumentNullException);
            });
        }

        [Test]
        public void Builders_set_out_for_the_far_cells_in_a_running_world()
        {
            var w = Causeway(out var house);
            w.Wood(KingdomWatch.Core.Work.Jobs.WoodReserve);
            w.Stores.Gather(ResourceKind.Food, w.Living * 60);
            w.World.Jobs.Track(w.Settlement);
            w.World.AdvanceTo(SimulationTime.Zero.Plus(KingdomWatch.Core.Work.Jobs.Dawn + 1L));

            var places = w.Settlement.Members
                .Where(p => w.World.Jobs.HasTask(p) && w.World.Jobs.TaskOf(p).Job == JobKind.Builder)
                .Select(p => w.World.Jobs.TaskOf(p).Destination)
                .ToList();

            Assert.That(places, Is.Not.Empty, "hands set out to build " + house);
            Assert.That(places, Has.Some.EqualTo(house.Spots[0].At), "walking to the far trees, not standing at the house");
        }

        [Test]
        public void The_hash_sees_where_a_building_s_hours_are_claimed()
        {
            // Two worlds alike but for which far cell holds a claim: the
            // building's totals match, its spots' do not. In one, a trip to
            // the first cell claims all of it; in the other a second trip
            // goes on to the next, and one of the first cell's comes back.
            var a = Causeway(out var houseA);
            var c = Causeway(out var houseC);
            Assume.That(houseA.Spots.Count, Is.GreaterThan(1));

            a.Buildings.Claim(houseA, JobKind.Builder, houseA.Spots[0].Ticks);
            c.Buildings.Claim(houseC, JobKind.Builder, houseC.Spots[0].Ticks);
            c.Buildings.Claim(houseC, JobKind.Builder, 1L);
            c.Buildings.Unclaim(houseC.Spots[0].At, JobKind.Builder, 1L);

            Assert.That(houseC.Claimed, Is.EqualTo(houseA.Claimed));
            Assert.That(houseC.Spots[1].Claimed, Is.EqualTo(1L));

            Assert.That(
                new KingdomWatch.Core.Validation.WorldHash().AddBuildings(a.Buildings).Value,
                Is.Not.EqualTo(new KingdomWatch.Core.Validation.WorldHash().AddBuildings(c.Buildings).Value));
        }

        [Test]
        public void The_validator_names_a_cleared_far_cell_with_something_on_it_again()
        {
            var w = Causeway(out var house);
            var spot = house.Spots[0];
            w.Buildings.Claim(house, JobKind.Builder, spot.Ticks);
            w.Buildings.Credit(spot.At, JobKind.Builder, spot.Ticks, w.Stores);
            var validator = new WorldValidator();

            validator.CheckBuildings(w.Buildings, w.World.Founding, w.World.Households, w.World.Grid, w.World.Clock);
            Assert.That(validator.Findings, Is.Empty);

            w.World.Grid.Set(spot.At, TerrainKind.Forest);
            validator = new WorldValidator();
            validator.CheckBuildings(w.Buildings, w.World.Founding, w.World.Households, w.World.Grid, w.World.Clock);

            Assert.That(validator.Findings.Select(f => f.Rule), Is.EqualTo(new[] { ValidationRule.BuildingFootprint }));
        }
    }
}
