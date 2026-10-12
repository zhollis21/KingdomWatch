using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Land;

namespace KingdomWatch.Harness
{
    /// <summary>
    /// The headless entry point. By default it runs the M1 world (#17) for
    /// one seed and prints its chronicle; with <c>--seeds</c> it sweeps that
    /// many seeds and prints one line each; with <c>--layout</c> it draws
    /// the town planner's villages (<see cref="TownLayouts"/>); with <c>--soak</c> it runs the
    /// <see cref="SchedulerSoak"/> the harness ran before there was a world,
    /// the throughput figure #58 asks for.
    /// </summary>
    /// <remarks>
    /// Timing wraps the whole run and nothing inside it reads a clock, so the
    /// simulation does exactly the same thing whether or not anyone looks at
    /// the numbers. That is the constraint both #58 and #59 put on any
    /// measurement: it must never influence what the tick loop does.
    /// </remarks>
    internal static class Program
    {
        private const int DefaultYears = 200;

        private static int Main(string[] args)
        {
            var years = DefaultYears;
            var seed = 1;
            var seeds = 0;
            var soakEntities = 0;
            var bushPicks = LandCover.DefaultBushPicks;
            string? layout = null;

            // Options come in pairs; an odd count means a flag without its value.
            if (args.Length % 2 != 0)
            {
                return Usage();
            }

            // Range checks live with what runs; this only rejects what is not
            // a number.
            for (var i = 0; i < args.Length; i += 2)
            {
                if (args[i] == "--layout")
                {
                    layout = args[i + 1];
                    continue;
                }

                var parsed = int.TryParse(
                    args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value);

                switch (args[i])
                {
                    case "--years" when parsed && value > 0:
                        years = value;
                        break;
                    case "--seed" when parsed && value > 0:
                        seed = value;
                        break;
                    case "--seeds" when parsed && value > 0:
                        seeds = value;
                        break;
                    case "--soak" when parsed && value > 0:
                        soakEntities = value;
                        break;
                    case "--bush-picks" when parsed && value > 0 && value <= LandCover.MaxClaims:
                        bushPicks = value;
                        break;
                    default:
                        return Usage();
                }
            }

            var core = typeof(EntityId).Assembly.GetName();
            Console.WriteLine("Kingdom Watch harness");
            Console.WriteLine($"  Core: {core.Name} {core.Version}");

            if (soakEntities > 0)
            {
                return Soak(soakEntities, years);
            }

            if (layout != null)
            {
                return TownLayouts.Write(layout, unchecked((ulong)seed), years, bushPicks);
            }

            return seeds > 0 ? Sweep(seeds, years, bushPicks) : RunChronicle(unchecked((ulong)seed), years, bushPicks);
        }

        private static int RunChronicle(ulong seed, int years, int bushPicks)
        {
            Console.WriteLine($"  World: seed {seed}, {N(years)} years, {WorldRun.Width}x{WorldRun.Height} placeholder map, {N(bushPicks)} trips a bush");
            Console.WriteLine();

            var stopwatch = Stopwatch.StartNew();
            var run = Run(seed, years, bushPicks);
            stopwatch.Stop();

            Chronicle.Write(run, Console.Out);
            Console.WriteLine();
            Console.WriteLine($"  Hash: {run.Hash():x16}");
            Console.WriteLine($"  Wall: {N(stopwatch.ElapsedMilliseconds)} ms");
            return run.Held ? 0 : 1;
        }

        private static int Sweep(int seeds, int years, int bushPicks)
        {
            Console.WriteLine($"  Sweep: {N(seeds)} seeds, {N(years)} years each, {N(bushPicks)} trips a bush");
            var failed = 0;
            var stopwatch = Stopwatch.StartNew();

            for (var seed = 1UL; seed <= (ulong)seeds; seed++)
            {
                var run = Run(seed, years, bushPicks);
                Console.WriteLine(
                    $"  seed {seed}: {Villages(run)}, "
                    + $"settled {Year(run.FirstSettlement)}, hash {run.Hash():x16}"
                    + DiedOut(run)
                    + (run.IsClean ? string.Empty : " BROKEN " + run.Failure));

                if (!run.Held)
                {
                    failed++;
                }
            }

            stopwatch.Stop();
            Console.WriteLine($"  Wall: {N(stopwatch.ElapsedMilliseconds)} ms");
            return failed == 0 ? 0 : 1;
        }

        private static int Soak(int entities, int years)
        {
            Console.WriteLine($"  Workload: scheduler soak, {N(entities)} entities, {N(years)} years");

            var clock = new SimulationClock(new IdAllocator());
            var soak = new SchedulerSoak(clock, entities);

            var stopwatch = Stopwatch.StartNew();
            soak.RunDays(years * SimulationTime.DaysPerYear);
            stopwatch.Stop();

            var seconds = stopwatch.Elapsed.TotalSeconds;
            Console.WriteLine($"  Events dispatched: {N(soak.EventsDispatched)} ({N(soak.Cancellations)} cancelled)");
            Console.WriteLine($"  Wall: {N(stopwatch.ElapsedMilliseconds)} ms");
            Console.WriteLine($"  Throughput: {F(years / seconds)} sim-years/s, {N((long)(soak.EventsDispatched / seconds))} events/s");
            Console.WriteLine($"  Peak pending: {N(soak.PeakPending)}");
            return 0;
        }

        // The world with its bushes set, so a sweep can try another number of
        // trips a bush without a rebuild (#26).
        private static WorldRun Run(ulong seed, int years, int bushPicks)
        {
            var run = new WorldRun(seed);
            run.World.Land.BushPicks = bushPicks;
            return run.RunYears(years);
        }

        // Each village the run saw: where it started, where it ended, and the
        // fewest and most it held in between.
        private static string Villages(WorldRun run)
        {
            var villages = run.Years.Count == 0 ? run.Founding : run.Years[run.Years.Count - 1].Villages;
            var parts = new List<string>(villages.Count);

            for (var v = 0; v < villages.Count; v++)
            {
                var range = run.RangeOf(villages[v].Village);
                parts.Add($"{range.Village} {range.First}->{range.Last} [{range.Low}..{range.High}]");
            }

            return string.Join(", ", parts);
        }

        private static string DiedOut(WorldRun run)
        {
            var text = string.Empty;

            for (var i = 0; i < run.DiedOut.Count; i++)
            {
                text += " DIED OUT " + run.DiedOut[i].Village + " y" + run.DiedOut[i].Year;
            }

            return text;
        }

        private static string Year(SimulationTime? time) => time is null ? "never" : "y" + time.Value.YearNumber;

        private static int Usage()
        {
            Console.Error.WriteLine("Usage: KingdomWatch.Harness [--seed N | --seeds N] [--years N] [--bush-picks N]");
            Console.Error.WriteLine("       KingdomWatch.Harness --soak ENTITIES [--years N]");
            Console.Error.WriteLine("       KingdomWatch.Harness --layout DIR [--seed N] [--years N] [--bush-picks N]");
            return 2;
        }

        private static string N(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

        private static string F(double value) => value.ToString("N1", CultureInfo.InvariantCulture);
    }
}
