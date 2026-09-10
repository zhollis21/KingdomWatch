using System;
using KingdomWatch.Core.Data;

namespace KingdomWatch.Harness
{
    /// <summary>
    /// The headless entry point. Empty of simulation on purpose - the world and
    /// the clock arrive with the rest of M1. This exists so the console seam is
    /// real from the first commit rather than being invented under pressure
    /// once there is something to run.
    /// </summary>
    internal static class Program
    {
        private static int Main()
        {
            // Referenced through a real Core type, so the seam is checked at
            // compile time. Replace this with an actual run as soon as there is
            // a world to build.
            var core = typeof(EntityId).Assembly.GetName();

            Console.WriteLine("Kingdom Watch harness");
            Console.WriteLine($"  Core: {core.Name} {core.Version}");
            Console.WriteLine("  No simulation yet - see milestone M1.");

            return 0;
        }
    }
}
