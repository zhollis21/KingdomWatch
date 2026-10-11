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

        // What a village founded at a place knows: the box a band's first
        // camp is shown (NomadicBands.RevealRadius), and no more. One mask,
        // moved from place to place.
        private sealed class Sight
        {
            private readonly World _world;
            private readonly bool[] _known;
            private WorldPosition? _shown;

            public Sight(World world)
            {
                _world = world;
                _known = new bool[world.Grid.CellCount];
            }

            public bool[] Around(WorldPosition at)
            {
                if (_shown is WorldPosition old)
                {
                    Mark(old, false);
                }

                Mark(at, true);
                _shown = at;
                return _known;
            }

            private void Mark(WorldPosition centre, bool known)
            {
                var grid = _world.Grid;
                var radius = KingdomWatch.Core.Nomadic.NomadicBands.RevealRadius;

                for (var y = Math.Max(0, centre.Y - radius); y <= Math.Min(grid.Height - 1, centre.Y + radius); y++)
                {
                    for (var x = Math.Max(0, centre.X - radius); x <= Math.Min(grid.Width - 1, centre.X + radius); x++)
                    {
                        _known[grid.IndexOf(new WorldPosition(x, y))] = known;
                    }
                }
            }
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
                    var sight = new Sight(world);
                    var sites = PickM1(world);

                    Assert.That(sites, Has.Count.EqualTo(2 * World.M1VillagesPerSide), "seed " + seed);

                    for (var i = 0; i < sites.Count; i++)
                    {
                        var at = sites[i];
                        var west = at.X < StartingSites.RiverColumn(world.Grid, at.Y);
                        Assert.That(west, Is.EqualTo(i < World.M1VillagesPerSide), "seed " + seed + " site " + i + ": west first");
                        Assert.That(world.Buildings.CanSettle(at, sight.Around(at), World.M1VillageSize), Is.True, "seed " + seed + " site " + i);
                        Assert.That(world.Buildings.ForageInReach(at, sight.Around(at)), Is.GreaterThanOrEqualTo(Buildings.YearlyNeed(World.M1VillageSize)), "seed " + seed + " site " + i + ": a year of bushes");

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
        public void Each_side_s_first_site_has_the_most_estimated_wood_of_any_candidate_that_could_be_settled()
        {
            // Estimated wood ranks; any forest walked to, a year of bushes and
            // the founding check gate: every lattice cell on a side with more
            // estimated wood than that side's first pick fails one of them.
            var world = Empty(1UL);
            var sight = new Sight(world);
            var sites = PickM1(world);
            var forest = new ForestSums(world.Grid);
            var box = (int)Math.Min(KingdomWatch.Core.Nomadic.NomadicBands.RevealRadius, Buildings.WoodReach / 100L);
            var near = (int)Math.Min(box, Buildings.WoodFullReach / 100L);
            long Estimate(WorldPosition at) => world.Buildings.WoodEstimate(forest.InBox(at, near), forest.InBox(at, box));

            Assert.Multiple(() =>
            {
                foreach (var first in new[] { sites[0], sites[World.M1VillagesPerSide] })
                {
                    var wood = Estimate(first);
                    var west = first.X < StartingSites.RiverColumn(world.Grid, first.Y);

                    for (var y = StartingSites.Lattice / 2; y < world.Grid.Height; y += StartingSites.Lattice)
                    {
                        var river = StartingSites.RiverColumn(world.Grid, y);

                        for (var x = StartingSites.Lattice / 2; x < world.Grid.Width; x += StartingSites.Lattice)
                        {
                            var at = new WorldPosition(x, y);

                            if (x == river || x < river != west || !world.Jobs.CanStandAt(at) || Estimate(at) <= wood)
                            {
                                continue;
                            }

                            var fits = world.Buildings.WoodInReach(at, sight.Around(at)) > 0
                                && world.Buildings.ForageInReach(at, sight.Around(at)) >= Buildings.YearlyNeed(World.M1VillageSize)
                                && world.Buildings.CanSettle(at, sight.Around(at), World.M1VillageSize);
                            Assert.That(fits, Is.False, at + " has more estimated wood than " + first);
                        }
                    }
                }
            });
        }

        // Forest cells in any box, by brute count: the test's own reading of
        // the counts the estimate is made from.
        private sealed class ForestSums
        {
            private readonly TerrainGrid _grid;

            public ForestSums(TerrainGrid grid) => _grid = grid;

            public int InBox(WorldPosition at, int radius)
            {
                var count = 0;

                for (var y = Math.Max(0, at.Y - radius); y <= Math.Min(_grid.Height - 1, at.Y + radius); y++)
                {
                    for (var x = Math.Max(0, at.X - radius); x <= Math.Min(_grid.Width - 1, at.X + radius); x++)
                    {
                        count += _grid[new WorldPosition(x, y)] == TerrainKind.Forest ? 1 : 0;
                    }
                }

                return count;
            }
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
        public void A_site_with_no_forest_in_reach_is_never_picked()
        {
            // Bushes enough for a year, room for a barn, and not a tree: the
            // village would freeze its first winter, so it is no site.
            var grid = new TerrainGrid(80, 40, TerrainKind.Plains);

            for (var y = 0; y < grid.Height; y++)
            {
                grid.Set(new WorldPosition(40, y), TerrainKind.SmallRiver);

                for (var x = 0; x < grid.Width; x++)
                {
                    if (x != 40 && (x + y) % 3 == 0 && Math.Abs(y - 20) > 4)
                    {
                        grid.Set(new WorldPosition(x, y), TerrainKind.Scrub);
                    }
                }
            }

            var world = new World(1UL, grid, DemographicSettings.Default);
            var west = new WorldPosition(20, 20);

            Assert.Multiple(() =>
            {
                Assert.That(world.Buildings.ForageInReach(west, ReadOnlySpan<bool>.Empty), Is.GreaterThanOrEqualTo(Buildings.YearlyNeed(20)), "bushes enough");
                Assert.That(world.Buildings.CanSettle(west, ReadOnlySpan<bool>.Empty, 20), Is.True, "passes the founding check");
                Assert.That(() => StartingSites.Pick(world, 1, 20, 0), Throws.InvalidOperationException.With.Message.Contains("room for 0"));
            });
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
                Assert.That(world.Buildings.WoodInReach(village), Is.EqualTo(world.Buildings.WoodInReach(village.Position, new Sight(world).Around(village.Position))), "it knows what a band's first camp shows");
                Assert.That(world.Jobs.IsTracked(village) && world.Hunger.IsTracked(village) && world.Matchmaking.IsTracked(village), Is.True);
                Assert.That(() => world.AddVillage(20, new WorldPosition(-1, 0)), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => world.AddVillage(20, new WorldPosition(StartingSites.RiverColumn(world.Grid, 0), 0)), Throws.TypeOf<ArgumentOutOfRangeException>(), "on the river");
                Assert.That(() => world.AddVillage(1, village.Position), Throws.TypeOf<ArgumentOutOfRangeException>(), "a village is at least a couple");
                Assert.That(world.Founding.All, Has.Count.EqualTo(2 * World.M1VillagesPerSide), "a refusal founds nothing");
            });
        }
    }
}
