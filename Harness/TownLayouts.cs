using System;
using System.Collections.Generic;
using System.IO;
using KingdomWatch.Core;
using KingdomWatch.Core.Construction;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Settlements;
using KingdomWatch.Core.Traversal;

namespace KingdomWatch.Harness
{
    /// <summary>
    /// <c>--layout DIR</c>: draws the town planner's villages (#23) as SVGs,
    /// two ways. <b>As run</b>: the seed's world run as the chronicle runs
    /// it, each settlement drawn in the year it had the most buildings - on
    /// today's tuning a handful, since villages die young (#147, #149).
    /// <b>Fed</b>: the same world with each settlement's wood and a month's
    /// food topped up every day, so nobody starves or freezes and the
    /// village grows as far as its households and hunger ask - a grown town
    /// to judge the planner by. Both runs are validated every year.
    /// </summary>
    internal static class TownLayouts
    {
        // Cells drawn each way from the camp.
        private const int Radius = 28;

        // Years the fed run lasts, at most.
        private const int FedYears = 60;

        // What the fed run keeps in every settlement's store.
        private const int FedWood = 600;
        private const int FedFoodDays = 30;

        public static int Write(string directory, ulong seed, int years, int bushPicks)
        {
            Directory.CreateDirectory(directory);
            Console.WriteLine($"  Layout: seed {seed}, as run for {TownPicture.N(years)} years and fed for {TownPicture.N(Math.Min(years, FedYears))}, into {directory}");

            var real = AsRun(directory, seed, years, bushPicks);
            var fed = Fed(directory, seed, Math.Min(years, FedYears), bushPicks);
            return real && fed ? 0 : 1;
        }

        private static bool AsRun(string directory, ulong seed, int years, int bushPicks)
        {
            var run = new WorldRun(seed);
            run.World.Land.BushPicks = bushPicks;
            var best = new Dictionary<EntityId, (int Count, long Year, string Svg)>();

            for (var year = 0; year < years && run.IsClean; year++)
            {
                run.RunYears(1);

                foreach (var settlement in run.World.Founding.All)
                {
                    var count = Count(run.World, settlement);

                    if (count > 0 && (!best.TryGetValue(settlement.Id, out var held) || count > held.Count))
                    {
                        var title = $"Seed {seed}, as run: {settlement.Id}, year {run.World.Now.YearNumber}, {Summary(run.World, settlement)}";
                        best[settlement.Id] = (count, run.World.Now.YearNumber, TownPicture.Draw(run.World, settlement, Radius, title));
                    }
                }
            }

            var index = 0;

            foreach (var settlement in run.World.Founding.All)
            {
                index++;

                if (best.TryGetValue(settlement.Id, out var drawn))
                {
                    var path = Path.Combine(directory, $"seed{seed}-asrun-{index}.svg");
                    File.WriteAllText(path, drawn.Svg);
                    Console.WriteLine($"    as run {settlement.Id}: most buildings in year {drawn.Year} -> {path}");
                }
            }

            Report("as run", run);
            return run.Held;
        }

        private static bool Fed(string directory, ulong seed, int years, int bushPicks)
        {
            var run = new WorldRun(seed);
            run.World.Land.BushPicks = bushPicks;
            run.RunYears(years, TopUp);

            var index = 0;

            foreach (var settlement in run.World.Founding.All)
            {
                index++;
                var title = $"Seed {seed}, fed: {settlement.Id}, year {run.World.Now.YearNumber}, {Summary(run.World, settlement)}";
                var path = Path.Combine(directory, $"seed{seed}-fed-{index}.svg");
                File.WriteAllText(path, TownPicture.Draw(run.World, settlement, Radius, title));
                Console.WriteLine($"    fed {settlement.Id}: {Summary(run.World, settlement)} -> {path}");
            }

            Report("fed", run);
            return run.Held;
        }

        // Wood to build and burn, and a month's food: never a year's, so a
        // village is still short of food and builds its barns and fields.
        private static void TopUp(World world)
        {
            foreach (var settlement in world.Founding.All)
            {
                var stores = settlement.SharedSupplies;
                var food = (long)settlement.Members.Count * FedFoodDays;
                var wood = stores.Available(ResourceKind.Wood);

                if (wood < FedWood)
                {
                    stores.Gather(ResourceKind.Wood, FedWood - wood);
                }

                var meals = stores.Available(ResourceKind.Food);

                if (meals < food)
                {
                    stores.Gather(ResourceKind.Food, (int)(food - meals));
                }
            }
        }

        private static int Count(World world, Settlement settlement)
        {
            var count = 0;

            foreach (var building in world.Buildings.All)
            {
                count += building.Settlement == settlement.Id ? 1 : 0;
            }

            return count;
        }

        private static string Summary(World world, Settlement settlement)
        {
            int houses = 0, barns = 0, fields = 0, lanes = 0;

            foreach (var building in world.Buildings.All)
            {
                if (building.Settlement != settlement.Id)
                {
                    continue;
                }

                lanes += building.Lane.Count;
                houses += building.Kind == BuildingKind.House ? 1 : 0;
                barns += building.Kind == BuildingKind.Barn ? 1 : 0;
                fields += building.Kind == BuildingKind.Field ? 1 : 0;
            }

            return $"{settlement.Members.Count} people, {houses} houses, {barns} barns, {fields} fields, {lanes} lane cells";
        }

        private static void Report(string name, WorldRun run)
        {
            var roads = 0;
            var grid = run.World.Grid;

            for (var y = 0; y < grid.Height; y++)
            {
                for (var x = 0; x < grid.Width; x++)
                {
                    roads += grid.RoadAt(new WorldPosition(x, y)) != RoadGrade.None ? 1 : 0;
                }
            }

            Console.WriteLine($"    {name}: {TownPicture.N(roads)} road cells, hash {run.Hash():x16}, {(run.IsClean ? "clean" : "BROKEN " + run.Failure)}");
        }
    }
}
