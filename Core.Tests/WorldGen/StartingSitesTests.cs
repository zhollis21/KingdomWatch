using System;
using System.Collections.Generic;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Construction;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Lifecycle;
using KingdomWatch.Core.Rng;
using KingdomWatch.Core.Traversal;
using KingdomWatch.Core.WorldGen;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.WorldGen
{
    [TestFixture]
    public sealed class StartingSitesTests
    {
        private const int Seeds = 3;

        // An M1 map with nothing on it yet: the picker runs before anyone
        // exists, so that is the world it is handed.
        private static World Empty(ulong seed) => new World(
            seed,
            PlaceholderMap.Generate(World.M1Width, World.M1Height, new DeterministicRng(seed)),
            DemographicSettings.Default);

        private static IReadOnlyList<WorldPosition> PickM1(World world) =>
            StartingSites.Pick(world, World.M1VillagesPerSide, World.M1VillageSize, World.M1VillageSpacing);

        private static bool[] AllKnown(World world)
        {
            var known = new bool[world.Grid.CellCount];
            Array.Fill(known, true);
            return known;
        }

        [Test]
        public void The_same_seed_gives_the_same_sites_and_the_world_founds_its_villages_on_them()
        {
            var sites = PickM1(Empty(1UL));
            var world = World.M1(1UL);

            Assert.Multiple(() =>
            {
                Assert.That(PickM1(Empty(1UL)), Is.EqualTo(sites));
                Assert.That(PickM1(Empty(2UL)), Is.Not.EqualTo(sites), "another seed, another map");
                Assert.That(world.Founding.All, Has.Count.EqualTo(sites.Count));

                for (var i = 0; i < sites.Count; i++)
                {
                    Assert.That(world.Founding.All[i].Position, Is.EqualTo(sites[i]), "founded in pick order");
                }
            });
        }

        [Test]
        public void Every_seed_s_sites_are_spread_out_split_by_the_river_and_could_be_settled()
        {
            Assert.Multiple(() =>
            {
                for (var seed = 1UL; seed <= Seeds; seed++)
                {
                    var world = Empty(seed);
                    var known = AllKnown(world);
                    var sites = PickM1(world);

                    Assert.That(sites, Has.Count.EqualTo(2 * World.M1VillagesPerSide), "seed " + seed);

                    for (var i = 0; i < sites.Count; i++)
                    {
                        var at = sites[i];
                        var west = at.X < StartingSites.RiverColumn(world.Grid, at.Y);
                        Assert.That(west, Is.EqualTo(i < World.M1VillagesPerSide), "seed " + seed + " site " + i + ": west first");
                        Assert.That(world.Buildings.CanSettle(at, known, World.M1VillageSize), Is.True, "seed " + seed + " site " + i);
                        Assert.That(world.Buildings.ForageInReach(at, known), Is.GreaterThanOrEqualTo(Buildings.YearlyNeed(World.M1VillageSize)), "seed " + seed + " site " + i + ": a year of bushes");

                        for (var j = 0; j < i; j++)
                        {
                            long dx = at.X - sites[j].X;
                            long dy = at.Y - sites[j].Y;
                            Assert.That(dx * dx + dy * dy, Is.GreaterThanOrEqualTo((long)World.M1VillageSpacing * World.M1VillageSpacing), "seed " + seed + " sites " + j + " and " + i);
                        }
                    }
                }
            });
        }

        [Test]
        public void Each_side_s_first_site_has_the_most_wood_of_any_candidate_that_could_be_settled()
        {
            // Wood ranks; a year of bushes and the founding check gate: every
            // lattice cell on a side with more wood in reach than that side's
            // first pick fails one or the other.
            var world = Empty(1UL);
            var known = AllKnown(world);
            var sites = PickM1(world);

            Assert.Multiple(() =>
            {
                foreach (var first in new[] { sites[0], sites[World.M1VillagesPerSide] })
                {
                    var wood = world.Buildings.WoodInReach(first, known);
                    var west = first.X < StartingSites.RiverColumn(world.Grid, first.Y);

                    for (var y = StartingSites.Lattice / 2; y < world.Grid.Height; y += StartingSites.Lattice)
                    {
                        var river = StartingSites.RiverColumn(world.Grid, y);

                        for (var x = StartingSites.Lattice / 2; x < world.Grid.Width; x += StartingSites.Lattice)
                        {
                            var at = new WorldPosition(x, y);

                            if (x == river || x < river != west || !world.Jobs.CanStandAt(at)
                                || world.Buildings.WoodInReach(at, known) <= wood)
                            {
                                continue;
                            }

                            var fits = world.Buildings.ForageInReach(at, known) >= Buildings.YearlyNeed(World.M1VillageSize)
                                && world.Buildings.CanSettle(at, known, World.M1VillageSize);
                            Assert.That(fits, Is.False, at + " has more wood than " + first);
                        }
                    }
                }
            });
        }

        [Test]
        public void A_map_with_no_room_for_the_villages_is_refused()
        {
            // Plains and a river: no bushes to feed anyone, so nowhere is a
            // site.
            var grid = new TerrainGrid(40, 40, TerrainKind.Plains);

            for (var y = 0; y < grid.Height; y++)
            {
                grid.Set(new WorldPosition(20, y), TerrainKind.SmallRiver);
            }

            var bare = new World(1UL, grid, DemographicSettings.Default);
            var riverless = new World(1UL, new TerrainGrid(40, 40, TerrainKind.Plains), DemographicSettings.Default);

            Assert.Multiple(() =>
            {
                Assert.That(() => StartingSites.Pick(bare, 1, 20, 0), Throws.InvalidOperationException.With.Message.Contains("room for 0"));
                Assert.That(() => StartingSites.Pick(riverless, 1, 20, 0), Throws.InvalidOperationException.With.Message.Contains("no river"));
            });
        }

        [Test]
        public void Sites_are_picked_only_when_a_village_founded_on_them_could_settle()
        {
            // The founding check is a gate of its own, beyond the bushes: out
            // of early spring it passes nowhere, so a world picking its sites
            // in summer has none.
            var world = Empty(1UL);
            world.AdvanceTo(new SimulationTime(SimulationTime.DaysPerSeason * SimulationTime.TicksPerDay));

            Assert.That(() => PickM1(world), Throws.InvalidOperationException.With.Message.Contains("room for 0"));
        }

        [Test]
        public void Pick_refuses_arguments_that_describe_no_villages()
        {
            var world = new World(1UL, new TerrainGrid(8, 8, TerrainKind.Plains), DemographicSettings.Default);

            Assert.Multiple(() =>
            {
                Assert.That(() => StartingSites.Pick(null!, 1, 20, 0), Throws.ArgumentNullException);
                Assert.That(() => StartingSites.Pick(world, 0, 20, 0), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => StartingSites.Pick(world, 1, 1, 0), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => StartingSites.Pick(world, 1, 20, -1), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => StartingSites.RiverColumn(null!, 0), Throws.ArgumentNullException);
            });
        }

        [Test]
        public void A_starting_village_is_a_settlement_that_knows_its_land_and_never_wandered()
        {
            var world = World.M1(1UL);
            var village = world.Founding.All[0];

            Assert.Multiple(() =>
            {
                Assert.That(world.Nomads.TrackedCount, Is.Zero);
                Assert.That(world.KnownMaps.Knows(village.Id, village.Position), Is.True);
                Assert.That(world.Buildings.WoodInReach(village), Is.EqualTo(world.Buildings.WoodInReach(village.Position, AllKnown(world))), "it knows all its reach");
                Assert.That(world.Jobs.IsTracked(village) && world.Hunger.IsTracked(village) && world.Matchmaking.IsTracked(village), Is.True);
                Assert.That(() => world.AddVillage(20, new WorldPosition(-1, 0)), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => world.AddVillage(20, new WorldPosition(StartingSites.RiverColumn(world.Grid, 0), 0)), Throws.TypeOf<ArgumentOutOfRangeException>(), "on the river");
                Assert.That(() => world.AddVillage(1, village.Position), Throws.TypeOf<ArgumentOutOfRangeException>(), "a village is at least a couple");
                Assert.That(world.Founding.All, Has.Count.EqualTo(2 * World.M1VillagesPerSide), "a refusal founds nothing");
            });
        }
    }
}
