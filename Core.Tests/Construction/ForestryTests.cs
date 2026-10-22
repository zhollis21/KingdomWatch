using System;
using System.Collections.Generic;
using System.Linq;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Construction;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
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
        public void Open_plains_past_the_town_are_plantable()
        {
            var w = new BuildingsWorld(size: PastTheTown);
            var at = new WorldPosition(BuildingsWorld.Centre.X + Buildings.StreetReach + 1, BuildingsWorld.Centre.Y + 6);

            Assert.That(Plantable(w, at), Is.True);
        }

        [Test]
        public void Open_plains_in_the_town_are_not_plantable_out_to_the_end_of_its_spokes()
        {
            var w = new BuildingsWorld(size: PastTheTown);
            var centre = BuildingsWorld.Centre;

            Assert.Multiple(() =>
            {
                Assert.That(Plantable(w, centre), Is.False, "the yard");
                Assert.That(Plantable(w, new WorldPosition(centre.X + Buildings.StreetRing, centre.Y + 3)), Is.False, "the ring street");
                Assert.That(Plantable(w, new WorldPosition(centre.X + 5, centre.Y + 6)), Is.False, "between the streets");
                Assert.That(Plantable(w, new WorldPosition(centre.X + Buildings.StreetReach, centre.Y + 6)), Is.False, "level with a spoke's end");
                Assert.That(Plantable(w, new WorldPosition(centre.X - 3, centre.Y + Buildings.StreetReach)), Is.False, "beside a spoke's end");
                Assert.That(Plantable(w, new WorldPosition(centre.X + Buildings.StreetReach, centre.Y + Buildings.StreetReach)), Is.False, "the town's corner");
                Assert.That(Plantable(w, new WorldPosition(centre.X + Buildings.StreetReach + 1, centre.Y + Buildings.StreetReach + 1)), Is.True, "just past the corner");
                Assert.That(Plantable(w, new WorldPosition(centre.X - 3, centre.Y + Buildings.StreetReach + 1)), Is.True, "just past a spoke's end");
            });
        }

        [Test]
        public void Every_settlements_town_is_kept_free_of_planting()
        {
            var w = new BuildingsWorld(size: PastTheTown);
            var other = new WorldPosition(50, 50);
            w.World.Founding.Found(
                w.World.AddBand(4, other),
                new Reasons(ReasonCode.PopulationPressure, ReasonCode.LandSuitable));

            Assert.Multiple(() =>
            {
                Assert.That(Plantable(w, new WorldPosition(other.X + 5, other.Y + 6)), Is.False, "in the other town");
                Assert.That(Plantable(w, new WorldPosition(other.X - Buildings.StreetReach, other.Y)), Is.False, "at its edge");
            });
        }

        [Test]
        public void Roads_and_ground_other_than_plains_are_not_plantable()
        {
            var w = new BuildingsWorld(size: PastTheTown, paint: grid =>
            {
                grid.Set(new WorldPosition(50, 5), TerrainKind.Forest);
                grid.Set(new WorldPosition(51, 5), TerrainKind.Scrub);
                grid.Set(new WorldPosition(52, 5), TerrainKind.Rocks);
            });
            var road = new WorldPosition(50, 30);
            w.World.Grid.SetRoad(road, RoadGrade.Track);

            Assert.Multiple(() =>
            {
                Assert.That(Plantable(w, new WorldPosition(53, 5)), Is.True, "open plains beside them");
                Assert.That(Plantable(w, road), Is.False, "a road");
                Assert.That(Plantable(w, new WorldPosition(50, 5)), Is.False, "forest");
                Assert.That(Plantable(w, new WorldPosition(51, 5)), Is.False, "scrub");
                Assert.That(Plantable(w, new WorldPosition(52, 5)), Is.False, "rocks");
            });
        }

        [Test]
        public void Ground_kept_for_a_building_and_its_lane_is_not_plantable()
        {
            var w = new BuildingsWorld();
            w.Wood(100);
            w.Dawn();
            var house = w.Buildings.All.Single();

            // A house stands in its own town; with no settlements' towns to
            // keep, only its footprint and lane are left keeping the ground.
            w.Buildings.Camps = Array.Empty<ICommunity>();
            Assert.That(Plantable(w, new WorldPosition(BuildingsWorld.Centre.X + 5, BuildingsWorld.Centre.Y + 6)), Is.True, "open plains in what was the town");

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
            var tree = new WorldPosition(50, 50);
            var w = new BuildingsWorld(paint: grid => grid.Set(tree, TerrainKind.Forest), size: PastTheTown);
            var beside = w.Buildings.BesideForest;
            var grid = w.World.Grid;

            Assert.Multiple(() =>
            {
                Assert.That(beside.Accepts(grid.IndexOf(new WorldPosition(51, 51))), Is.True, "diagonal");
                Assert.That(beside.Accepts(grid.IndexOf(new WorldPosition(49, 50))), Is.True, "edge");
                Assert.That(beside.Accepts(grid.IndexOf(new WorldPosition(52, 50))), Is.False, "two away");
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
        public void A_village_with_no_forest_wants_Foresters_for_twice_its_hearths_and_one_more()
        {
            var w = new BuildingsWorld();

            Assert.That(w.Buildings.TryPlantWork(w.Settlement, 3, out var shortBy, out var target), Is.True);

            var burn = 4L * Warmth.FuelPerFire * SimulationTime.DaysPerSeason;

            Assert.Multiple(() =>
            {
                Assert.That(target, Is.EqualTo(burn * Buildings.PlantHeadroomNumerator));
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
        public void A_planting_counts_whole_out_to_the_near_walk_half_out_to_dusk_and_nothing_past()
        {
            var b = new BuildingsWorld().Buildings;
            var whole = b.WoodPerPlanting;

            Assert.Multiple(() =>
            {
                Assert.That(b.WoodPerPlantingAt(0L), Is.EqualTo(whole), "underfoot");
                Assert.That(b.WoodPerPlantingAt(Buildings.WoodFullReach), Is.EqualTo(whole), "at the near walk");
                Assert.That(b.WoodPerPlantingAt(Buildings.WoodFullReach + 1L), Is.EqualTo(b.WoodFromTrees(1L) / 2L), "just past it");
                Assert.That(b.WoodPerPlantingAt(Buildings.WoodReach), Is.EqualTo(b.WoodFromTrees(1L) / 2L), "at dusk's reach");
                Assert.That(b.WoodPerPlantingAt(Buildings.WoodReach + 1L), Is.Zero, "past it");
                Assert.That(b.WoodPerPlantingAt(long.MaxValue), Is.Zero, "the longest walk");
                Assert.That(b.WoodFromTrees(1L) / 2L, Is.GreaterThan(0L).And.LessThan(whole), "half is something, and less than whole");
                Assert.That(() => b.WoodPerPlantingAt(-1L), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => b.WoodPerPlantingAt(long.MinValue), Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void Saplings_count_toward_what_Foresters_plant_for()
        {
            var w = new BuildingsWorld();
            w.Buildings.TryPlantWork(w.Settlement, 0, out var before, out _);
            w.World.Land.Plant(new WorldPosition(BuildingsWorld.Centre.X + 5, BuildingsWorld.Centre.Y + 6));

            w.Buildings.TryPlantWork(w.Settlement, 0, out var after, out _);

            Assert.That(before - after, Is.EqualTo(w.Buildings.WoodPerPlanting * Buildings.PlantHeadroomDenominator));
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

            // Only the house's own stone, so what the rocks pay has room.
            w.StoneLeft(BuildingTable.Of(BuildingKind.House).Stone);
            w.DawnAsIs();
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
            var tree = new WorldPosition(BuildingsWorld.Centre.X + Buildings.StreetReach + 2, BuildingsWorld.Centre.Y + 7);
            var w = new BuildingsWorld(paint: grid => grid.Set(tree, TerrainKind.Forest), size: PastTheTown);
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
        public void A_Forester_is_drawn_walking_onto_the_plot_at_the_pace_its_plains_were_charged()
        {
            // The tree goes in as the trip sets out (#155), turning the plot
            // to forest; the walk there was charged on plains, and is paced
            // that way (#25): the last step takes what a plains step takes.
            var tree = new WorldPosition(BuildingsWorld.Centre.X + Buildings.StreetReach + 2, BuildingsWorld.Centre.Y + 7);
            var w = new BuildingsWorld(paint: grid => grid.Set(tree, TerrainKind.Forest), size: PastTheTown);
            w.World.Jobs.Track(w.Settlement);
            w.World.AdvanceTo(SimulationTime.FromDays(1).Plus(Jobs.Dawn + 1L));

            var forester = w.Settlement.Members.First(m => w.World.People.GetJob(m) == JobKind.Forester);
            var task = w.World.Jobs.TaskOf(forester);
            var route = w.World.Jobs.RouteOf(forester).ToArray();
            var plot = route[route.Length - 1];
            var before = route[route.Length - 2];
            var diagonal = plot.X != before.X && plot.Y != before.Y;
            var lastStep = 0L;
            for (var t = 0L; t < task.TravelTicks; t++)
            {
                if (w.World.Jobs.PlaceAt(forester, task.Start.Plus(t)).From == before)
                {
                    lastStep++;
                }
            }

            Assert.Multiple(() =>
            {
                Assert.That(w.World.Grid[plot], Is.EqualTo(TerrainKind.Forest), "planted as it set out");
                Assert.That(lastStep, Is.EqualTo((diagonal ? 140L : 100L) * Jobs.TicksPerCostUnit).Within(1L));
            });
        }

        [Test]
        public void A_planting_trip_vacated_leaves_no_tree_and_one_that_ends_leaves_it_growing()
        {
            var tree = new WorldPosition(BuildingsWorld.Centre.X + Buildings.StreetReach + 2, BuildingsWorld.Centre.Y + 7);
            var w = new BuildingsWorld(paint: grid => grid.Set(tree, TerrainKind.Forest), size: PastTheTown);
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
            // Foresters, however many have nothing better to do. A sapling
            // past the town counts for half a tree, both in the dawn's sweep
            // and as each one goes in: two halves short sends two. A small
            // village, so its saplings fit just past the town.
            var w = new BuildingsWorld(people: 3, size: PastTheTown);
            var hearths = Warmth.CountHearths(w.Settlement.Members, w.World.People, new List<EntityId>());
            w.Buildings.TryPlantWork(w.Settlement, hearths, out var shortBy, out _);
            var perTree = w.Buildings.WoodPerPlantingAt(Buildings.WoodReach) * Buildings.PlantHeadroomDenominator;
            var saplings = (int)((shortBy - perTree - 1L) / perTree);
            var cells = Enumerable.Range(0, w.World.Grid.CellCount)
                .Where(w.Buildings.Plantable.Accepts)
                .Select(w.World.Grid.PositionAt)
                .OrderBy(at => Chebyshev(at, BuildingsWorld.Centre))
                .Take(saplings)
                .ToList();

            foreach (var at in cells)
            {
                w.World.Land.Plant(at);
            }

            w.World.Jobs.Track(w.Settlement);
            w.Buildings.TryPlantWork(w.Settlement, hearths, out var left, out _);
            var wanted = (int)((left + perTree - 1L) / perTree);

            // The first dawn alone: a second would sweep again.
            w.World.AdvanceTo(SimulationTime.Zero.Plus(Jobs.Dawn + 1L));

            Assert.Multiple(() =>
            {
                Assert.That(cells, Has.Count.EqualTo(saplings), "room for the saplings");
                Assert.That(wanted, Is.EqualTo(2), "two saplings short");
                Assert.That(Growing(w), Has.Count.EqualTo(saplings + wanted));
            });
        }

        // A map wide enough east and south to have plains past the town,
        // which covers all of the default one.
        private const int PastTheTown = 60;

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
