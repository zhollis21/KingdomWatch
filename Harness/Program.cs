using System;
using System.Reflection;

namespace KingdomWatch.Harness
{
    /// <summary>
    /// The headless entry point. Empty of simulation on purpose - the world,
    /// clock and entity model arrive with the rest of M1. This exists so the
    /// console seam is real from the first commit rather than being invented
    /// under pressure once there is something to run.
    /// </summary>
    internal static class Program
    {
        private static int Main()
        {
            // Core has no public types yet, so loading it by name is the only way
            // to confirm the reference actually resolves at runtime. Replace this
            // with real work as soon as there is a world to build.
            var core = Assembly.Load(new AssemblyName("KingdomWatch.Core")).GetName();

            Console.WriteLine("Kingdom Watch harness");
            Console.WriteLine($"  Core: {core.Name} {core.Version}");
            Console.WriteLine("  No simulation yet - see milestone M1.");

            return 0;
        }
    }
}
