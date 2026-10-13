using System;
using System.Collections.Generic;
using System.Linq;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Construction;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Land;
using KingdomWatch.Core.Needs;
using KingdomWatch.Core.Traversal;
using KingdomWatch.Core.Work;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Construction
{
    // Foresters (#155): where a tree may go, what a village counts of the
    // trees it planted, when it wants more, and what clearing pays now that a
    // building stands on any ground.
    [TestFixture]
    public sealed class ForestryTests
    {
        [Test]
        public void Open_plains_away_from_the_village_are_plantable()
        {
            var w = new BuildingsWorld();
            var at = new WorldPosition(BuildingsWorld.Centre.X + 5, BuildingsWorld.Centre.Y + 6);

            Assert.That(w.Buildings.Plantable.Accepts(w.World.Grid.IndexOf(at)), Is.True);
        }

        [Test]
        public void The_yard_planned_streets_roads_and_other_ground_are_not_plantable()
        {
            var w = new BuildingsWorld(paint: grid =>
            {
                grid.Set(new WorldPosition(5, 5), TerrainKind.Forest);
                grid.Set(new WorldPosition(6, 5), TerrainKind.Scrub);
                grid.Set(new WorldPosition(7, 5), TerrainKind.Rocks);
            });
            var grid = w.World.Grid;
            var road = new WorldPosition(3, 30);
            grid.SetRoad(road, RoadGrade.Track);
            var centre = BuildingsWorld.Centre;

            Assert.Multiple(() =>
            {
                Assert.That(Plantable(w, centre), Is.False, "the yard");
                Assert.That(Plantable(w, new WorldPosition(centre.X + Buildings.CampYardRadius, centre.Y + 1)), Is.False, "the yard's edge");
                Assert.That(Plantable(w, new WorldPosition(centre.X + Buildings.StreetRing, centre.Y + 3)), Is.False, "the ring street");
                Assert.That(Plantable(w, new WorldPosition(centre.X, centre.Y + Buildings.CampYardRadius + 1)), Is.False, "a spoke");
                Assert.That(Plantable(w, road), Is.False, "a road");
                Assert.That(Plantable(w, new WorldPosition(5, 5)), Is.False, "forest");
                Assert.That(Plantable(w, new WorldPosition(6, 5)), Is.False, "scrub");
                Assert.That(Plantable(w, new WorldPosition(7, 5)), Is.False, "rocks");
            });
        }

        [Test]
        public void Ground_kept_for_a_building_and_its_lane_is_not_plantable()
        {
            var w = new BuildingsWorld();
            w.Wood(100);
            w.Dawn();
            var house = w.Buildings.All.Single();

            Assert.Multiple(() =>
            {
                Assert.That(Plantable(w, house.Anchor), Is.False, "the footprint");

                foreach (var at in house.Lane)
                {
                    Assert.That(Plantable(w, at), Is.False, "the lane at " + at);
                }
            });
        }

        [Test]
        public void Beside_forest_takes_only_plantable_cells_touching_a_tree()
        {
            var tree = new WorldPosition(5, 5);
            var w = new BuildingsWorld(paint: grid => grid.Set(tree, TerrainKind.Forest));
            var beside = w.Buildings.BesideForest;
            var grid = w.World.Grid;

            Assert.Multiple(() =>
            {
                Assert.That(beside.Accepts(grid.IndexOf(new WorldPosition(6, 6))), Is.True, "diagonal");
                Assert.That(beside.Accepts(grid.IndexOf(new WorldPosition(4, 5))), Is.True, "edge");
                Assert.That(beside.Accepts(grid.IndexOf(new WorldPosition(7, 5))), Is.False, "two away");
                Assert.That(beside.Accepts(grid.IndexOf(tree)), Is.False, "the tree itself");
            });
        }

        [Test]
        public void A_planted_tree_is_left_out_of_the_wood_in_reach_until_it_has_grown()
        {
            var w = new BuildingsWorld();
            var at = new WorldPosition(BuildingsWorld.Centre.X + 5, BuildingsWorld.Centre.Y + 6);
            var before = w.Buildings.WoodInReach(w.Settlement);

            w.World.Land.Plant(at);

            Assert.Multiple(() =>
            {
                Assert.That(w.Buildings.WoodInReach(w.Settlement), Is.EqualTo(before), "a sapling is no wood yet");
                Assert.That(w.Buildings.WoodPlantedInReach(w.Settlement), Is.EqualTo(before + w.Buildings.WoodPerPlanting), "but it is what Foresters plant toward");
            });

            w.World.AdvanceTo(SimulationTime.FromDays(LandCover.RegrowDays));

            Assert.That(w.Buildings.WoodInReach(w.Settlement), Is.EqualTo(before + w.Buildings.WoodPerPlanting), "grown");
        }

        [Test]
        public void One_planted_tree_near_the_village_gives_what_one_whole_tree_does()
        {
            var w = new BuildingsWorld();

            Assert.That(
                w.Buildings.WoodPerPlanting,
                Is.EqualTo(w.World.Land.TreeCuts * Buildings.WoodPerCut * SimulationTime.DaysPerYear / LandCover.RegrowDays));
        }

        [Test]
        public void A_village_with_no_forest_wants_Foresters_for_its_hearths_and_one_more()
        {
            var w = new BuildingsWorld();

            Assert.That(w.Buildings.TryPlantWork(w.Settlement, 3, out var shortBy, out var target), Is.True);

            var burn = 4L * Warmth.FuelPerFire * SimulationTime.DaysPerSeason;

            Assert.Multiple(() =>
            {
                Assert.That(target, Is.EqualTo(burn * Buildings.WoodHeadroomNumerator));
                Assert.That(shortBy, Is.EqualTo(target), "no wood at all");
            });
        }

        [Test]
        public void A_village_deep_in_forest_wants_no_Foresters()
        {
            var w = new BuildingsWorld(paint: grid =>
            {
                for (var y = 0; y < BuildingsWorld.Size; y++)
                {
                    for (var x = 0; x < BuildingsWorld.Size; x++)
                    {
                        if (Chebyshev(new WorldPosition(x, y), BuildingsWorld.Centre) > Buildings.CampYardRadius)
                        {
                            grid.Set(new WorldPosition(x, y), TerrainKind.Forest);
                        }
                    }
                }
            });

            Assert.Multiple(() =>
            {
                Assert.That(w.Buildings.TryPlantWork(w.Settlement, 3, out var shortBy, out _), Is.False);
                Assert.That(shortBy, Is.Zero);
            });
        }

        [Test]
        public void Saplings_count_toward_what_Foresters_plant_for()
        {
            var w = new BuildingsWorld();
            w.Buildings.TryPlantWork(w.Settlement, 0, out var before, out _);
            w.World.Land.Plant(new WorldPosition(BuildingsWorld.Centre.X + 5, BuildingsWorld.Centre.Y + 6));

            w.Buildings.TryPlantWork(w.Settlement, 0, out var after, out _);

            Assert.That(before - after, Is.EqualTo(w.Buildings.WoodPerPlanting * Buildings.WoodHeadroomDenominator));
        }

        [Test]
        public void Plant_work_refuses_a_missing_settlement_or_negative_hearths()
        {
            var w = new BuildingsWorld();

            Assert.Multiple(() =>
            {
                Assert.That(() => w.Buildings.TryPlantWork(null!, 0, out _, out _), Throws.ArgumentNullException);
                Assert.That(() => w.Buildings.TryPlantWork(w.Settlement, -1, out _, out _), Throws.InstanceOf<ArgumentOutOfRangeException>());
                Assert.That(() => w.Buildings.WoodPlantedInReach(null!), Throws.ArgumentNullException);
            });
        }

        [Test]
        public void A_house_goes_on_scrub_beside_the_yard_and_its_bushes_pay_their_fruit_and_brushwood()
        {
            // Scrub all round the yard: the best spot for a house is on it,
            // where it used to be passed over for plains further out.
            var w = new BuildingsWorld(paint: grid => Ring(grid, Buildings.CampYardRadius + 1, Buildings.CampYardRadius + 8, TerrainKind.Scrub));
            w.Wood(100);
            w.Dawn();
            var house = w.Buildings.All.Single();
            var bushes = CellsOf(house).Where(at => w.World.Grid[at] == TerrainKind.Scrub).ToList();
            Assert.That(bushes, Is.Not.Empty, "the house stands on scrub");

            var picks = bushes.Sum(at => w.World.Land.PicksLeft(at));
            var food = w.Stores.Available(ResourceKind.Food);
            var wood = w.Stores.Available(ResourceKind.Wood);
            w.Finish(house);

            Assert.Multiple(() =>
            {
                Assert.That(picks, Is.EqualTo(bushes.Count * w.World.Land.BushPicks), "spring, untouched");
                Assert.That(w.Stores.Available(ResourceKind.Food) - food, Is.EqualTo(picks * Buildings.FoodPerPick(Season.Spring)));
                Assert.That(w.Stores.Available(ResourceKind.Wood) - wood, Is.EqualTo(bushes.Count * Buildings.WoodPerBush));
                Assert.That(bushes.All(at => w.World.Grid[at] == TerrainKind.Plains), Is.True);
            });
        }

        [Test]
        public void A_bare_bush_cleared_pays_brushwood_and_no_fruit()
        {
            var w = new BuildingsWorld(paint: grid => Ring(grid, Buildings.CampYardRadius + 1, Buildings.CampYardRadius + 8, TerrainKind.Scrub));
            w.Wood(100);
            w.Dawn();
            var house = w.Buildings.All.Single();
            var bushes = CellsOf(house).Where(at => w.World.Grid[at] == TerrainKind.Scrub).ToList();

            foreach (var at in bushes)
            {
                while (w.World.Land.IsWorkable(at))
                {
                    w.World.Land.Take(at);
                }
            }

            var food = w.Stores.Available(ResourceKind.Food);
            var wood = w.Stores.Available(ResourceKind.Wood);
            w.Finish(house);

            Assert.Multiple(() =>
            {
                Assert.That(w.Stores.Available(ResourceKind.Food), Is.EqualTo(food));
                Assert.That(w.Stores.Available(ResourceKind.Wood) - wood, Is.EqualTo(bushes.Count * Buildings.WoodPerBush));
            });
        }

        [Test]
        public void A_house_goes_on_rocks_beside_the_yard_and_they_are_quarried_for_their_stone()
        {
            var w = new BuildingsWorld(paint: grid => Ring(grid, Buildings.CampYardRadius + 1, Buildings.CampYardRadius + 8, TerrainKind.Rocks));
            w.Wood(100);
            w.Dawn();
            var house = w.Buildings.All.Single();
            var underfoot = Footprint(house).Where(at => w.World.Grid[at] == TerrainKind.Rocks).ToList();
            var rocks = CellsOf(house).Count(at => w.World.Grid[at] == TerrainKind.Rocks);

            Assert.Multiple(() =>
            {
                Assert.That(underfoot, Is.Not.Empty, "the house stands on rocks");
                Assert.That(house.ClearRocks, Is.EqualTo(rocks));
                Assert.That(house.ClearTicks, Is.EqualTo(rocks * Buildings.TicksPerRock));
            });

            var stone = w.Stores.Available(ResourceKind.Stone);
            w.Finish(house);

            Assert.Multiple(() =>
            {
                Assert.That(w.Stores.Available(ResourceKind.Stone) - stone, Is.EqualTo(rocks * Buildings.StonePerRock));
                Assert.That(underfoot.All(at => w.World.Grid[at] == TerrainKind.Plains), Is.True, "quarried away");
            });
        }

        [Test]
        public void A_village_with_no_forest_sends_a_Forester_to_plant_beside_the_nearest_wood()
        {
            var tree = new WorldPosition(BuildingsWorld.Centre.X + 6, BuildingsWorld.Centre.Y + 7);
            var w = new BuildingsWorld(paint: grid => grid.Set(tree, TerrainKind.Forest));
            w.World.Jobs.Track(w.Settlement);

            w.World.AdvanceTo(SimulationTime.FromDays(1).Plus(Jobs.Dawn + 1L));

            var planted = Enumerable.Range(0, w.World.Grid.CellCount).Where(w.World.Land.IsGrowing).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(planted, Is.Not.Empty, "a tree went in at the first dawn");
                Assert.That(planted.Select(w.World.Grid.PositionAt).Any(at => Chebyshev(at, tree) == 1), Is.True, "beside the wood first");
                Assert.That(w.Settlement.Members.Any(m => w.World.People.GetJob(m) == JobKind.Forester), Is.True);
            });
        }

        [Test]
        public void A_planting_trip_vacated_leaves_no_tree_and_one_that_ends_leaves_it_growing()
        {
            var tree = new WorldPosition(BuildingsWorld.Centre.X + 6, BuildingsWorld.Centre.Y + 7);
            var w = new BuildingsWorld(paint: grid => grid.Set(tree, TerrainKind.Forest));
            w.World.Jobs.Track(w.Settlement);
            w.World.AdvanceTo(SimulationTime.FromDays(1).Plus(Jobs.Dawn + 1L));

            var foresters = w.Settlement.Members.Where(m => w.World.People.GetJob(m) == JobKind.Forester).ToList();
            var before = Growing(w).Count;
            w.World.Jobs.Vacate(foresters[0]);
            var vacated = Growing(w);

            w.World.AdvanceTo(SimulationTime.FromDays(1).Plus(Jobs.Dawn + Buildings.PlantTicks + SimulationTime.TicksPerHour));

            Assert.Multiple(() =>
            {
                Assert.That(vacated, Has.Count.EqualTo(before - 1), "the vacated trip's tree is not planted");
                Assert.That(Growing(w), Is.SupersetOf(vacated), "the trees that got their hour stay in");
                Assert.That(vacated.All(cell => w.World.Grid[w.World.Grid.PositionAt(cell)] == TerrainKind.Forest), Is.True);
            });
        }

        [Test]
        public void Foresters_go_out_for_the_trees_the_dawn_shortfall_wants_and_no_more()
        {
            // The shortfall is swept at dawn, and each tree planted since
            // comes off it: a village a tree or two short sends that many
            // Foresters, however many have nothing better to do. (A sapling
            // at the edge of reach counts for less than a whole tree.)
            var w = new BuildingsWorld();
            var hearths = Warmth.CountHearths(w.Settlement.Members, w.World.People, new List<EntityId>());
            w.Buildings.TryPlantWork(w.Settlement, hearths, out var shortBy, out _);
            var perTree = w.Buildings.WoodPerPlanting * Buildings.WoodHeadroomDenominator;
            var saplings = (int)((shortBy - 1L) / perTree);
            var cells = Enumerable.Range(0, w.World.Grid.CellCount)
                .Where(w.Buildings.Plantable.Accepts)
                .Select(w.World.Grid.PositionAt)
                .Where(at => Chebyshev(at, BuildingsWorld.Centre) <= 8)
                .Take(saplings)
                .ToList();

            foreach (var at in cells)
            {
                w.World.Land.Plant(at);
            }

            w.World.Jobs.Track(w.Settlement);
            w.Buildings.TryPlantWork(w.Settlement, hearths, out var left, out _);
            var wanted = (int)((left + perTree - 1L) / perTree);
            w.World.AdvanceTo(SimulationTime.FromDays(1).Plus(Jobs.Dawn + 1L));

            Assert.Multiple(() =>
            {
                Assert.That(cells, Has.Count.EqualTo(saplings), "room for the saplings");
                Assert.That(wanted, Is.InRange(1L, 2L), "a sapling or so short");
                Assert.That(Growing(w), Has.Count.EqualTo(saplings + wanted));
            });
        }

        private static List<int> Growing(BuildingsWorld w) =>
            Enumerable.Range(0, w.World.Grid.CellCount).Where(w.World.Land.IsGrowing).ToList();

        private static bool Plantable(BuildingsWorld w, WorldPosition at) =>
            w.Buildings.Plantable.Accepts(w.World.Grid.IndexOf(at));

        private static void Ring(TerrainGrid grid, int from, int to, TerrainKind kind)
        {
            for (var y = 0; y < grid.Height; y++)
            {
                for (var x = 0; x < grid.Width; x++)
                {
                    var ring = Chebyshev(new WorldPosition(x, y), BuildingsWorld.Centre);

                    if (ring >= from && ring <= to)
                    {
                        grid.Set(new WorldPosition(x, y), kind);
                    }
                }
            }
        }

        private static IEnumerable<WorldPosition> Footprint(Building building)
        {
            for (var dy = 0; dy < building.Height; dy++)
            {
                for (var dx = 0; dx < building.Width; dx++)
                {
                    yield return new WorldPosition(building.Anchor.X + dx, building.Anchor.Y + dy);
                }
            }
        }

        private static IEnumerable<WorldPosition> CellsOf(Building building) =>
            Footprint(building).Concat(building.Lane).Concat(building.Square).Distinct();

        private static int Chebyshev(WorldPosition a, WorldPosition b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }
}
