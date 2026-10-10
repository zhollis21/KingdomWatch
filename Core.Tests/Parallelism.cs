using NUnit.Framework;

// Fixtures run side by side; the tests inside one still run one at a time.
// Every World owns its own clock, ids, RNG and stores, and Core keeps no
// mutable static state, so worlds in different fixtures cannot see each
// other - the world-run fixtures are most of the suite's time, and they are
// many. A fixture that shares mutable state with another, or a test bounded
// by wall time too tightly to share the machine, must opt out with
// [NonParallelizable].
[assembly: Parallelizable(ParallelScope.Fixtures)]
