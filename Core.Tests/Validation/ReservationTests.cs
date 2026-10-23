using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Construction;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Tests.Construction;
using KingdomWatch.Core.Traversal;
using KingdomWatch.Core.Work;
using KingdomWatch.Harness;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Validation
{
    // Section 5's "all resource reservations have an owner" (#24): every claim
    // a task or a building holds is checked against its owner, from both
    // ends, and each way of breaking it is shown being caught.
    [TestFixture]
    public sealed class ReservationTests
    {
        private static readonly long Hour = SimulationTime.TicksPerHour;

        // A settled village with bushes and a wood beside it, short of food,
        // wood and buildings, so every kind of trip goes out.
        private static BuildingsWorld Busy()
        {
            var w = new BuildingsWorld(people: 20, paint: grid =>
            {
                for (var y = 12; y < 29; y++)
                {
                    for (var x = 26; x < 31; x++)
                    {
                        if (y < 18 || y > 22)
                        {
                            grid.Set(new WorldPosition(x, y), y < 18 ? TerrainKind.Scrub : TerrainKind.Forest);
                        }
                    }
                }
            });

            w.World.Jobs.Track(w.Settlement);
            w.Wood(Jobs.WoodReserve);

            // The map has no rocks, and since #178 the base store holds less
            // stone than a house and a barn take: without the barn's, no
            // field goes in and no Farmer goes out.
            w.Stone(BuildingTable.Of(BuildingKind.Barn).Stone);
            return w;
        }

        private static WorldValidator Check(BuildingsWorld w)
        {
            var communities = new List<ICommunity>();
            w.World.CopyCommunitiesTo(communities);

            return new WorldValidator().CheckReservations(
                w.World.Jobs, w.Buildings, w.World.Land, w.World.Grid, communities, w.World.Clock);
        }

        private static List<ValidationRule> Rules(BuildingsWorld w) => Check(w).Findings.Select(f => f.Rule).ToList();

        // The first worker out on this job, after stepping to mid-morning
        // until one is.
        private static PersonHandle OutOn(BuildingsWorld w, JobKind job)
        {
            for (var hour = 0; hour < 24 * 30; hour++)
            {
                w.World.Advance(Hour);
                var onJob = w.Settlement.Members.Where(m => w.World.Jobs.HasTask(m) && w.World.Jobs.TaskOf(m).Job == job).ToList();

                if (onJob.Count > 0)
                {
                    return onJob[0];
                }
            }

            Assert.Fail("nobody went out on " + job + " in thirty days");
            return PersonHandle.None;
        }

        [Test]
        public void A_village_at_work_holds_only_what_its_trips_and_buildings_own()
        {
            var w = Busy();
            var seen = new HashSet<JobKind>();
            var findings = new List<ValidationFinding>();

            for (var hour = 0; hour < 24 * 30; hour++)
            {
                w.World.Advance(Hour);
                findings.AddRange(Check(w).Findings);

                foreach (var member in w.Settlement.Members.Where(w.World.Jobs.HasTask))
                {
                    seen.Add(w.World.Jobs.TaskOf(member).Job);
                }
            }

            Assert.Multiple(() =>
            {
                Assert.That(findings, Is.Empty, string.Join("\n", findings));
                Assert.That(seen, Is.SupersetOf(new[] { JobKind.Forager, JobKind.Woodcutter, JobKind.Builder, JobKind.Farmer }),
                    "a check nobody was out for proves nothing");
            });
        }

        [Test]
        public void A_village_planting_holds_only_what_its_Foresters_own()
        {
            var tree = new WorldPosition(BuildingsWorld.Centre.X + Buildings.StreetReach + 2, BuildingsWorld.Centre.Y + 7);
            var w = new BuildingsWorld(paint: grid => grid.Set(tree, TerrainKind.Forest), size: 60);
            w.World.Jobs.Track(w.Settlement);

            OutOn(w, JobKind.Forester);

            Assert.That(Check(w).Findings, Is.Empty, Check(w).Report(0UL));
        }

        [Test]
        public void Trips_vacated_by_the_death_cascade_leave_nothing_held()
        {
            // The issue's "reservations cancel when the agent dies": the
            // cascade's Jobs.Vacate gives each claim back (#52).
            var w = Busy();
            var builder = OutOn(w, JobKind.Builder);
            var forager = OutOn(w, JobKind.Forager);

            w.World.Jobs.Vacate(builder);
            w.World.Jobs.Vacate(forager);

            Assert.That(Check(w).Findings, Is.Empty, Check(w).Report(0UL));
        }

        [Test]
        public void Hours_claimed_on_a_building_with_nobody_out_belong_to_nobody()
        {
            var w = new BuildingsWorld();
            w.Wood(100);
            w.Dawn();

            w.Buildings.Claim(w.Buildings.All.Single(), JobKind.Builder, Hour);

            Assert.That(Rules(w), Does.Contain(ValidationRule.ReservationUnowned));
        }

        [Test]
        public void A_Builder_out_whose_hours_were_given_back_is_caught()
        {
            var w = Busy();
            var builder = OutOn(w, JobKind.Builder);
            var task = w.World.Jobs.TaskOf(builder);

            Assume.That(Check(w).IsClean, Check(w).Report(0UL));
            w.Buildings.Unclaim(task.Destination, JobKind.Builder, task.WorkTicks);

            Assert.That(Rules(w), Does.Contain(ValidationRule.ReservationLost));
        }

        [Test]
        public void A_forager_out_whose_harvest_was_given_back_is_caught()
        {
            var w = Busy();
            var forager = OutOn(w, JobKind.Forager);
            var destination = w.World.Jobs.TaskOf(forager).Destination;

            // Every claim on the bush given back, so the trips out on it
            // outnumber what it holds however many it had given before.
            Assume.That(Check(w).IsClean, Check(w).Report(0UL));
            var cell = w.World.Grid.IndexOf(destination);

            while (w.World.Land.ClaimsAt(cell) > 0)
            {
                w.World.Land.Return(destination);
            }

            Assert.That(Rules(w), Does.Contain(ValidationRule.ReservationLost));
        }

        [Test]
        public void Stock_reserved_or_in_process_with_no_task_behind_it_belongs_to_nobody()
        {
            var reserved = new BuildingsWorld();
            reserved.Stores.Reserve(ResourceKind.Stone, 1);

            var milling = new BuildingsWorld();
            milling.Stores.Gather(ResourceKind.Grain, 1);
            milling.Stores.BeginRecipe(PrimitiveTier.Mill);

            Assert.Multiple(() =>
            {
                Assert.That(Rules(reserved), Does.Contain(ValidationRule.ReservationUnowned));
                Assert.That(Rules(milling), Does.Contain(ValidationRule.ReservationUnowned));
            });
        }

        // Some of what a claim is kept in - the ground a building holds, the
        // tree a Forester planted, the building under a cell - lives in
        // private maps no public operation can put out of step with the rest;
        // reached by reflection so each rule is seen to fire at all.
        private static Dictionary<int, TValue> Private<TValue>(object owner, string name)
        {
            if (owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(owner) is Dictionary<int, TValue> map)
            {
                return map;
            }

            Assert.Fail(owner.GetType().Name + " keeps no " + name + " any more; this test needs another way in.");
            return new Dictionary<int, TValue>();
        }

        private static Dictionary<int, Building> LanesOf(Buildings buildings) => Private<Building>(buildings, "_laneOf");

        [Test]
        public void A_Forester_out_whose_tree_is_no_longer_growing_is_caught()
        {
            // Vacate's Unplant would throw on it, mid-cascade.
            var tree = new WorldPosition(BuildingsWorld.Centre.X + Buildings.StreetReach + 2, BuildingsWorld.Centre.Y + 7);
            var w = new BuildingsWorld(paint: grid => grid.Set(tree, TerrainKind.Forest), size: 60);
            w.World.Jobs.Track(w.Settlement);
            var forester = OutOn(w, JobKind.Forester);
            var plot = w.World.Grid.IndexOf(w.World.Jobs.TaskOf(forester).Destination);

            Assume.That(Check(w).IsClean, Check(w).Report(0UL));
            Private<long>(w.World.Land, "_planted").Remove(plot);

            Assert.That(Rules(w), Does.Contain(ValidationRule.ReservationLost));
        }

        [Test]
        public void A_Builder_out_on_ground_no_building_covers_is_caught()
        {
            var w = Busy();
            var builder = OutOn(w, JobKind.Builder);
            var at = w.World.Grid.IndexOf(w.World.Jobs.TaskOf(builder).Destination);

            Assume.That(Check(w).IsClean, Check(w).Report(0UL));
            Private<Building>(w.Buildings, "_onCell").Remove(at);

            Assert.That(Rules(w), Does.Contain(ValidationRule.ReservationLost));
        }

        [Test]
        public void Checking_reservations_refuses_a_missing_system()
        {
            var w = new BuildingsWorld();
            var communities = new List<ICommunity>();
            var world = w.World;
            var validator = new WorldValidator();

            Assert.Multiple(() =>
            {
                Assert.That(() => validator.CheckReservations(null!, w.Buildings, world.Land, world.Grid, communities, world.Clock), Throws.ArgumentNullException);
                Assert.That(() => validator.CheckReservations(world.Jobs, null!, world.Land, world.Grid, communities, world.Clock), Throws.ArgumentNullException);
                Assert.That(() => validator.CheckReservations(world.Jobs, w.Buildings, null!, world.Grid, communities, world.Clock), Throws.ArgumentNullException);
                Assert.That(() => validator.CheckReservations(world.Jobs, w.Buildings, world.Land, null!, communities, world.Clock), Throws.ArgumentNullException);
                Assert.That(() => validator.CheckReservations(world.Jobs, w.Buildings, world.Land, world.Grid, null!, world.Clock), Throws.ArgumentNullException);
                Assert.That(() => validator.CheckReservations(world.Jobs, w.Buildings, world.Land, world.Grid, communities, null!), Throws.ArgumentNullException);
            });
        }

        [Test]
        public void Ground_held_back_for_no_building_still_clearing_belongs_to_nobody()
        {
            var w = new BuildingsWorld();
            w.Wood(100);
            w.Dawn();
            var house = w.Buildings.All.Single();

            Assume.That(Check(w).IsClean, Check(w).Report(0UL));
            LanesOf(w.Buildings).Add(w.World.Grid.IndexOf(new WorldPosition(1, 1)), house);

            Assert.That(Rules(w), Does.Contain(ValidationRule.ReservationUnowned));
        }

        [Test]
        public void A_lane_still_to_clear_opened_to_gatherers_is_caught()
        {
            var w = new BuildingsWorld(paint: grid =>
            {
                for (var y = 0; y < BuildingsWorld.Size; y++)
                {
                    for (var x = 0; x < BuildingsWorld.Size; x++)
                    {
                        grid.Set(new WorldPosition(x, y), TerrainKind.Scrub);
                    }
                }
            });
            w.Wood(100);
            w.Dawn();
            var house = w.Buildings.All.Single();

            Assume.That(house.Cleared, Is.False, "on scrub, a house has ground to clear");
            Assume.That(house.Lane.Count + house.Square.Count, Is.GreaterThan(0));
            Assume.That(Check(w).IsClean, Check(w).Report(0UL));

            var held = house.Lane.Count > 0 ? house.Lane[0] : house.Square[0];
            LanesOf(w.Buildings).Remove(w.World.Grid.IndexOf(held));

            Assert.That(Rules(w), Does.Contain(ValidationRule.ReservationLost));
        }
    }
}
