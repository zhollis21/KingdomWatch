using System;
using System.Diagnostics;
using System.Globalization;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;

namespace KingdomWatch.Harness
{
    /// <summary>
    /// The headless entry point. By default it runs the M1 world (#17) for
    /// one seed and prints its chronicle; with <c>--seeds</c> it sweeps that
    /// many seeds and prints one line each; with <c>--soak</c> it runs the
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

            // Options come in pairs; an odd count means a flag without its value.
            if (args.Length % 2 != 0)
            {
                return Usage();
            }

            // Range checks live with what runs; this only rejects what is not
            // a number.
            for (var i = 0; i < args.Length; i += 2)
            {
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

            return seeds > 0 ? Sweep(seeds, years) : RunChronicle(unchecked((ulong)seed), years);
        }

        private static int RunChronicle(ulong seed, int years)
        {
            Console.WriteLine($"  World: seed {seed}, {N(years)} years, {WorldRun.Width}x{WorldRun.Height} placeholder map");
            Console.WriteLine();

            var stopwatch = Stopwatch.StartNew();
            var run = new WorldRun(seed).RunYears(years);
            stopwatch.Stop();

            Chronicle.Write(run, Console.Out);
            Console.WriteLine();
            Console.WriteLine($"  Hash: {run.Hash():x16}");
            Console.WriteLine($"  Wall: {N(stopwatch.ElapsedMilliseconds)} ms");
            return run.IsClean ? 0 : 1;
        }

        private static int Sweep(int seeds, int years)
        {
            Console.WriteLine($"  Sweep: {N(seeds)} seeds, {N(years)} years each");
            var broken = 0;
            var stopwatch = Stopwatch.StartNew();

            for (var seed = 1UL; seed <= (ulong)seeds; seed++)
            {
                var run = new WorldRun(seed).RunYears(years);
                var last = run.Years[run.Years.Count - 1];
                var (westLow, westHigh, eastLow, eastHigh) = Range(run);
                Console.WriteLine(
                    $"  seed {seed}: west {run.FoundingWest}->{last.West} [{westLow}..{westHigh}], "
                    + $"east {run.FoundingEast}->{last.East} [{eastLow}..{eastHigh}], "
                    + $"settled {Year(run.FirstSettlement)}, hash {run.Hash():x16}"
                    + (run.IsClean ? string.Empty : " BROKEN " + run.Failure));

                if (!run.IsClean)
                {
                    broken++;
                }
            }

            stopwatch.Stop();
            Console.WriteLine($"  Wall: {N(stopwatch.ElapsedMilliseconds)} ms");
            return broken == 0 ? 0 : 1;
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

        private static (int, int, int, int) Range(WorldRun run)
        {
            int westLow = run.FoundingWest, westHigh = run.FoundingWest;
            int eastLow = run.FoundingEast, eastHigh = run.FoundingEast;

            for (var i = 0; i < run.Years.Count; i++)
            {
                westLow = Math.Min(westLow, run.Years[i].West);
                westHigh = Math.Max(westHigh, run.Years[i].West);
                eastLow = Math.Min(eastLow, run.Years[i].East);
                eastHigh = Math.Max(eastHigh, run.Years[i].East);
            }

            return (westLow, westHigh, eastLow, eastHigh);
        }

        private static string Year(SimulationTime? time) => time is null ? "never" : "y" + time.Value.YearNumber;

        private static int Usage()
        {
            Console.Error.WriteLine("Usage: KingdomWatch.Harness [--seed N | --seeds N] [--years N]");
            Console.Error.WriteLine("       KingdomWatch.Harness --soak ENTITIES [--years N]");
            return 2;
        }

        private static string N(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

        private static string F(double value) => value.ToString("N1", CultureInfo.InvariantCulture);
    }
}
