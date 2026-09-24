using System;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Harness;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Validation
{
    /// <summary>
    /// Section 4's LOD equivalence, as far as M1 has two things to compare
    /// (#14): compression aggregates activity, never identity, so how a run
    /// is cut into advances must not change where it ends up.
    /// </summary>
    /// <remarks>
    /// There is one simulation path today. Stepped local detail and extreme
    /// compression are both <see cref="SimulationClock.AdvanceTo"/>, with a
    /// near target or a distant one, so "scheduled versus compressed" is "many
    /// small advances versus one large one". The M1 rule is therefore the
    /// strictest one there is: the same seed reaches an identical world -
    /// the same canonical hash and the same history, event for event - however
    /// the run is chunked.
    ///
    /// What this guards against is a system that does work per call rather
    /// than per scheduled event: a top-up at the end of each advance, a check
    /// that runs "when the driver hands back control". Either is invisible in
    /// a run advanced one way and turns a player's speed setting into an input
    /// to the simulation. Stepped movement (M3) is the likeliest source.
    ///
    /// Section 4's other half - outcomes that may legitimately differ, within
    /// defined rules, where detailed positioning matters (a battle seen versus
    /// one resolved offscreen) - needs a second path to exist first, and is
    /// tracked separately.
    ///
    /// The journal is compared event by event as well as through the hash,
    /// which folds in only its digest (#104). A digest says two histories
    /// differ; the comparison says at which event, which is where a failure
    /// here has to be read from.
    /// </remarks>
    [TestFixture]
    public sealed class LodEquivalenceTests
    {
        private const long Years = 10L;

        private static readonly ulong[] Seeds = { 1UL, 2UL };

        [TestCaseSource(nameof(Seeds))]
        public void However_a_run_is_chunked_it_reaches_the_same_world(ulong seed)
        {
            var jump = Run(seed, OneJump);
            var yearly = Run(seed, EveryYear);
            var daily = Run(seed, EveryDay);
            var ragged = Run(seed, Ragged);

            Assert.Multiple(() =>
            {
                // A run that produced nobody and nothing would agree with
                // itself however it was chunked.
                Assert.That(jump.World.People.Count, Is.GreaterThan(0), "the run produced no people to compare");
                Assert.That(jump.World.Journal.Count, Is.GreaterThan(100), "the run barely did anything");

                foreach (var (name, other) in new[] { ("yearly", yearly), ("daily", daily), ("ragged", ragged) })
                {
                    Assert.That(other.World.Now, Is.EqualTo(jump.World.Now), name + ": ended at a different time");
                    Assert.That(other.Hash(), Is.EqualTo(jump.Hash()), name + ": world hash");
                    Assert.That(FirstJournalDifference(other.World, jump.World), Is.Null, name + ": history");
                }
            });
        }

        [Test]
        public void Work_done_per_advance_is_caught_by_that_comparison()
        {
            // The comparison with teeth. Without this, the test above has
            // never been seen to fail and could be passing because the two
            // sides are equally blind.
            //
            // The meddling is the failure the test exists for: something the
            // driver does each time it gets control back. It writes to a
            // counter the simulation never reads, so what is caught is the
            // write itself rather than a cascade it set off.
            var jump = Run(1UL, OneJump);
            var meddled = Run(1UL, run => EveryDay(run, () => run.World.Ids.Next(EntityKind.Animal)));

            Assert.That(meddled.Hash(), Is.Not.EqualTo(jump.Hash()));
        }

        [Test]
        public void A_different_history_is_caught_by_the_journal_comparison()
        {
            // The same, for the journal comparison: it is the diagnostic that
            // names the first event two runs disagree on, so it has to be seen
            // to fail on its own rather than only alongside the hash.
            var first = Run(1UL, OneJump);
            var second = Run(2UL, OneJump);

            // A run that goes on a year further has the shorter one's history
            // as its opening: every shared event agrees, and only the length
            // tells them apart.
            var longer = Run(1UL, run => run.World.Advance((Years + 1L) * SimulationTime.TicksPerYear));

            Assert.Multiple(() =>
            {
                Assert.That(FirstJournalDifference(first.World, second.World), Is.Not.Null, "another seed");
                Assert.That(FirstJournalDifference(first.World, longer.World), Is.Not.Null, "a longer run");
            });
        }

        private static WorldRun Run(ulong seed, Action<WorldRun> advance)
        {
            var run = new WorldRun(seed);
            advance(run);
            return run;
        }

        private static void OneJump(WorldRun run) =>
            run.World.Advance(Years * SimulationTime.TicksPerYear);

        private static void EveryYear(WorldRun run)
        {
            for (var i = 0L; i < Years; i++)
            {
                run.World.Advance(SimulationTime.TicksPerYear);
            }
        }

        private static void EveryDay(WorldRun run) => EveryDay(run, null);

        private static void EveryDay(WorldRun run, Action? between)
        {
            for (var i = 0L; i < Years * SimulationTime.DaysPerYear; i++)
            {
                run.World.Advance(SimulationTime.TicksPerDay);
                between?.Invoke();
            }
        }

        // Steps of varying length that line up with no cadence the systems
        // keep - not a day, a meal or a council - so an advance boundary
        // lands mid-task, mid-journey and mid-season.
        private static void Ragged(WorldRun run)
        {
            var left = Years * SimulationTime.TicksPerYear;
            var step = 7919L;

            while (left > 0L)
            {
                var ticks = Math.Min(step, left);
                run.World.Advance(ticks);
                left -= ticks;
                step = (step * 3L % 100003L) + 1L;
            }
        }

        // The first place two histories part, or null when they are the same
        // event for event. Reported as a place rather than a bool so a failure
        // says which event to start from.
        private static string? FirstJournalDifference(World a, World b)
        {
            var left = a.Journal.AsSpan();
            var right = b.Journal.AsSpan();
            var shared = Math.Min(left.Length, right.Length);

            for (var i = 0; i < shared; i++)
            {
                if (!left[i].Equals(right[i]))
                {
                    return "event " + i + ": " + left[i] + " versus " + right[i];
                }
            }

            return left.Length == right.Length
                ? null
                : "one history is " + left.Length + " events long and the other " + right.Length;
        }
    }
}
