using System;
using System.Diagnostics;
using System.Globalization;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;

namespace KingdomWatch.Harness
{
    /// <summary>
    /// The headless entry point. Until #17 delivers a world to run, it runs
    /// the <see cref="SchedulerSoak"/> and reports how fast the scheduler
    /// goes - the seam #58 asks for, so the throughput figure exists from the
    /// first system onward rather than being discovered at M2.
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

        // Section 18's peak stepped-agent count for one focused settlement.
        private const int DefaultEntities = 300;

        private const long DaysPerYear = 365L;

        private static int Main(string[] args)
        {
            var years = DefaultYears;
            var entities = DefaultEntities;

            // Options come in pairs; an odd count means a flag without its value.
            if (args.Length % 2 != 0)
            {
                return Usage();
            }

            // Range checks live with the soak itself, whose messages say why
            // a value is refused; this only rejects what is not a number.
            for (var i = 0; i < args.Length; i += 2)
            {
                var parsed = int.TryParse(
                    args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value);

                switch (args[i])
                {
                    case "--years" when parsed:
                        years = value;
                        break;
                    case "--entities" when parsed:
                        entities = value;
                        break;
                    default:
                        return Usage();
                }
            }

            var core = typeof(EntityId).Assembly.GetName();
            Console.WriteLine("Kingdom Watch harness");
            Console.WriteLine($"  Core: {core.Name} {core.Version}");
            Console.WriteLine($"  Workload: scheduler soak, {N(entities)} entities, {N(years)} years (no world yet - see #17)");

            var clock = new SimulationClock(new IdAllocator());
            var soak = new SchedulerSoak(clock, entities);

            var stopwatch = Stopwatch.StartNew();
            soak.RunDays(years * DaysPerYear);
            stopwatch.Stop();

            var seconds = stopwatch.Elapsed.TotalSeconds;
            Console.WriteLine($"  Events dispatched: {N(soak.EventsDispatched)} ({N(soak.Cancellations)} cancelled)");
            Console.WriteLine($"  Wall: {N(stopwatch.ElapsedMilliseconds)} ms");
            Console.WriteLine($"  Throughput: {F(years / seconds)} sim-years/s, {N((long)(soak.EventsDispatched / seconds))} events/s");
            Console.WriteLine($"  Peak pending: {N(soak.PeakPending)}");

            return 0;
        }

        private static int Usage()
        {
            Console.Error.WriteLine("Usage: KingdomWatch.Harness [--years N] [--entities N]");
            return 2;
        }

        private static string N(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

        private static string F(double value) => value.ToString("N1", CultureInfo.InvariantCulture);
    }
}
