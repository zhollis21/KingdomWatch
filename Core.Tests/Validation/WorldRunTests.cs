using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Lifecycle;
using KingdomWatch.Core.Traversal;
using KingdomWatch.Core.Work;
using KingdomWatch.Harness;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Validation
{
    /// <summary>
    /// The M1 world (#17), run the way the harness runs it. Section 19 asks
    /// for population stability and milestone firing; section 5 for a world
    /// that holds its invariants across seeds.
    /// </summary>
    /// <remarks>
    /// A century rather than the harness's two: every homeland that died out
    /// in #17's tuning runs did so inside the first ten years, and past a
    /// century the population has no ceiling yet (#69, #26), so the second
    /// hundred years is minutes of CI that finds nothing the first did not.
    /// The full run is <c>KingdomWatch.Harness --seeds 8 --years 200</c>.
    ///
    /// "Stable" is survival only. Neither homeland may die out; there is no
    /// upper bound to assert until housing or land can limit growth.
    ///
    /// This replaced the 30-year fixture sweeps: the validator checks every
    /// rule they checked, after every year, on the world the harness runs.
    /// </remarks>
    [TestFixture]
    public sealed class WorldRunTests
    {
        private const int Seeds = 8;
        private const long Years = 100L;

        // Each seed run once for the fixture: the tests over them read the
        // same runs, and a century of eight worlds is the costly part.
        private static readonly List<WorldRun> Runs = new List<WorldRun>();

        [OneTimeSetUp]
        public void RunEverySeed()
        {
            if (Runs.Count > 0)
            {
                return;
            }

            for (var seed = 1UL; seed <= Seeds; seed++)
            {
                Runs.Add(new WorldRun(seed).RunYears(Years));
            }
        }

        [Test]
        public void Every_seed_holds_its_invariants_every_year()
        {
            var report = new StringBuilder();

            foreach (var run in Runs)
            {
                if (!run.IsClean)
                {
                    report.AppendLine(run.Failure);
                }
            }

            Assert.That(report.ToString(), Is.Empty);
        }

        [Test]
        public void Neither_homeland_dies_out_on_any_seed()
        {
            var report = new StringBuilder();

            foreach (var run in Runs)
            {
                if (run.WestDiedOut != null || run.EastDiedOut != null)
                {
                    report.AppendLine(
                        "seed " + run.World.Seed + ": west died out " + (run.WestDiedOut?.ToString() ?? "never")
                        + ", east " + (run.EastDiedOut?.ToString() ?? "never"));
                }
            }

            Assert.That(report.ToString(), Is.Empty);
        }

        [Test]
        public void The_milestones_M1_can_reach_fire_on_every_seed()
        {
            // Economy ladder section 9's first milestone, and first settlement
            // standing in for the rest until buildings exist (#100).
            Assert.Multiple(() =>
            {
                foreach (var run in Runs)
                {
                    Assert.That(run.FirstCamp, Is.EqualTo(SimulationTime.Zero), "seed " + run.World.Seed + ": camp at world start");
                    Assert.That(run.FirstSettlement, Is.Not.Null, "seed " + run.World.Seed + ": never settled");
                }
            });
        }

        [Test]
        public void The_same_seed_reaches_the_same_hash_and_another_does_not()
        {
            var first = new WorldRun(3UL).RunYears(10L).Hash();
            var again = new WorldRun(3UL).RunYears(10L).Hash();
            var other = new WorldRun(4UL).RunYears(10L).Hash();

            Assert.Multiple(() =>
            {
                Assert.That(again, Is.EqualTo(first));
                Assert.That(other, Is.Not.EqualTo(first));
            });
        }

        [Test]
        public void The_run_s_hash_sees_a_wandering_band_s_stores_and_its_map()
        {
            // Before either band settles, a band's own state is most of the
            // world. Copilot's review of #103: the run's hash left it out.
            var run = new WorldRun(1UL);
            var bands = new List<ICommunity>();
            run.World.Nomads.CopyTrackedTo(bands);
            var band = (MobileGroup)bands[0];
            var before = run.Hash();

            band.SharedSupplies.Gather(ResourceKind.Wood, 1);
            var afterStores = run.Hash();
            run.World.KnownMaps.Reveal(band.Id, new WorldPosition(0, 0), 0);
            var afterMap = run.Hash();

            Assert.Multiple(() =>
            {
                Assert.That(afterStores, Is.Not.EqualTo(before), "stores");
                Assert.That(afterMap, Is.Not.EqualTo(afterStores), "map");
            });
        }

        [Test]
        public void The_run_s_hash_sees_the_terrain_and_the_next_ids()
        {
            var run = new WorldRun(1UL);
            var before = run.Hash();

            run.World.Grid.Set(new WorldPosition(0, 0), TerrainKind.Hills);
            var afterTerrain = run.Hash();
            run.World.Ids.Next(EntityKind.Animal);

            Assert.Multiple(() =>
            {
                Assert.That(afterTerrain, Is.Not.EqualTo(before), "terrain");
                Assert.That(run.Hash(), Is.Not.EqualTo(afterTerrain), "next ids");
            });
        }

        [Test]
        public void The_bands_start_one_each_side_of_the_river_where_they_can_stand()
        {
            var world = World.TwoBands(5UL, WorldRun.Width, WorldRun.Height, WorldRun.WestSize, WorldRun.EastSize);
            var bands = new List<ICommunity>();
            world.CopyCommunitiesTo(bands);

            Assert.That(bands, Has.Count.EqualTo(2));
            var river = RiverColumn(world.Grid, bands[0].Position.Y);

            Assert.Multiple(() =>
            {
                Assert.That(bands[0].Position.Y, Is.EqualTo(bands[1].Position.Y), "both on the middle row");
                Assert.That(bands[0].Position.X, Is.LessThan(river));
                Assert.That(bands[1].Position.X, Is.GreaterThan(river));
                Assert.That(bands[0].Members, Has.Count.EqualTo(WorldRun.WestSize));
                Assert.That(bands[1].Members, Has.Count.EqualTo(WorldRun.EastSize));
                Assert.That(world.Pathfinder.IsPassable(bands[0].Position, Jobs.Mover), Is.True);
                Assert.That(world.Pathfinder.IsPassable(bands[1].Position, Jobs.Mover), Is.True);
            });
        }

        [Test]
        public void The_bookings_gathered_cover_every_stream()
        {
            // The #97 review note on #17: the hash folds in whichever bookings
            // its caller gathers, so the world's gathering must reach every
            // stream. A day in, both bands are wandering and every stream has
            // booked something.
            var world = World.TwoBands(1UL, WorldRun.Width, WorldRun.Height, WorldRun.WestSize, WorldRun.EastSize);
            world.Advance(SimulationTime.TicksPerDay);
            var bookings = new List<PendingBooking>();
            world.CopyBookingsTo(bookings);

            var kinds = new HashSet<ScheduledEventKind>();

            foreach (var booking in bookings)
            {
                kinds.Add(booking.Kind);
            }

            Assert.That(kinds, Is.SupersetOf(new[]
            {
                ScheduledEventKind.MealDue,
                ScheduledEventKind.WarmthDue,
                ScheduledEventKind.WorkDayDue,
                ScheduledEventKind.CouncilDue,
                ScheduledEventKind.BirthCheck,
                ScheduledEventKind.CourtshipDue,
            }));
        }

        [Test]
        public void A_run_refuses_a_missing_world_or_one_with_no_river_to_split()
        {
            var riverless = new World(1UL, new TerrainGrid(8, 8, TerrainKind.Plains), DemographicSettings.Default);
            riverless.AddBand(12, new WorldPosition(3, 3));

            Assert.Multiple(() =>
            {
                Assert.That(() => new WorldRun(null!), Throws.ArgumentNullException);
                Assert.That(() => new WorldRun(riverless), Throws.InvalidOperationException, "no homelands to count");
            });
        }

        [Test]
        public void A_run_refuses_a_negative_length_or_one_past_the_end_of_time_before_advancing()
        {
            var run = new WorldRun(1UL);
            var start = run.World.Now;

            Assert.Multiple(() =>
            {
                Assert.That(() => run.RunYears(-1L), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => run.RunYears(long.MaxValue / SimulationTime.TicksPerYear + 1L), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(run.World.Now, Is.EqualTo(start), "refused before the first year, not part-way");
                Assert.That(run.Years, Is.Empty);
                Assert.That(run.RunYears(0L).Years, Is.Empty, "zero is a valid, empty run");
            });
        }

        [TestCase(0, TestName = "A west that dies out is a failed run even when it validates clean")]
        [TestCase(1, TestName = "An east that dies out is a failed run even when it validates clean")]
        public void A_homeland_that_dies_out_is_a_failed_run_even_when_it_validates_clean(int side)
        {
            // The #103 review: the harness exited 0 on a world where a
            // homeland had died out, because it read only the validator.
            // Everyone on one side of the river dies before the first year.
            var run = new WorldRun(1UL);
            var communities = new List<ICommunity>();
            run.World.Nomads.CopyTrackedTo(communities);
            var band = (MobileGroup)communities[side];

            foreach (var member in new List<PersonHandle>(band.Members))
            {
                run.World.Deaths.Die(member, new Reasons(ReasonCode.Illness));
            }

            run.RunYears(2L);
            var text = new StringWriter();
            Chronicle.Write(run, text);
            var died = side == 0 ? run.WestDiedOut : run.EastDiedOut;
            var lived = side == 0 ? run.EastDiedOut : run.WestDiedOut;

            Assert.Multiple(() =>
            {
                Assert.That(run.IsClean, Is.True, "a world can die out without breaking a rule");
                Assert.That(died, Is.EqualTo(1L), "the first year that ended with nobody there");
                Assert.That(lived, Is.Null);
                Assert.That(run.Held, Is.False);
                Assert.That(text.ToString(), Does.Contain((side == 0 ? "West" : "East") + " died out in year 1."));
            });
        }
        [Test]
        public void The_run_s_years_and_the_validator_s_findings_cannot_be_written_through()
        {
            // The #103 review: a list handed out as IReadOnlyList can be cast
            // back and edited, erasing years the chronicle and the sweep read.
            // Wrapped, the way DrawCollisionDetector and Founding hand theirs out.
            var run = new WorldRun(1UL).RunYears(1L);
            var validator = new WorldValidator();

            Assert.Multiple(() =>
            {
                Assert.That(run.Years, Is.Not.InstanceOf<List<YearSummary>>());
                Assert.That(validator.Findings, Is.Not.InstanceOf<List<ValidationFinding>>());
            });
        }

        [Test]
        public void The_chronicle_prints_each_year_and_each_band_s_first_camp_once()
        {
            var run = new WorldRun(1UL).RunYears(3L);
            var text = new StringWriter();

            Chronicle.Write(run, text);
            var written = text.ToString();

            Assert.Multiple(() =>
            {
                Assert.That(written, Does.StartWith("Year 0: west " + WorldRun.WestSize + ", east " + WorldRun.EastSize));
                Assert.That(written, Does.Contain("Year 1: ").And.Contain("Year 2: ").And.Contain("Year 3: "));
                Assert.That(Occurrences(written, "pitched its first camp"), Is.EqualTo(2), "one per band, not one per move");
                Assert.That(written, Does.Not.Contain("PersonBorn").And.Not.Contain("PersonDied"), "folded into the year lines");
                Assert.That(written, Does.Not.Contain(" other"), "every death this world publishes has a named cause");
                Assert.That(() => Chronicle.Write(null!, text), Throws.ArgumentNullException);
                Assert.That(() => Chronicle.Write(run, null!), Throws.ArgumentNullException);
            });
        }

        [Test]
        public void The_chronicle_prints_an_event_at_the_final_year_boundary()
        {
            // AdvanceTo runs everything due on or before its target, so an
            // event at exactly the first tick of a year belongs to the year
            // just run - a founder's birthday death, and what it dissolves,
            // lands there. Published by hand at that instant to stand for one.
            var run = new WorldRun(1UL).RunYears(1L);
            var bus = run.World.Bus;
            bus.Publish(DomainEventKind.FamineStarted, new EntityId(EntityKind.Settlement, 999L), EntityId.None);
            var text = new StringWriter();

            Chronicle.Write(run, text);

            Assert.That(text.ToString(), Does.Contain("y1 d0 famine in Settlement#999"));
        }

        private static int Occurrences(string text, string of)
        {
            var count = 0;

            for (var at = text.IndexOf(of, StringComparison.Ordinal); at >= 0; at = text.IndexOf(of, at + 1, StringComparison.Ordinal))
            {
                count++;
            }

            return count;
        }

        private static int RiverColumn(TerrainGrid grid, int row)
        {
            for (var x = 0; x < grid.Width; x++)
            {
                if (grid[new WorldPosition(x, row)] == TerrainKind.SmallRiver)
                {
                    return x;
                }
            }

            return -1;
        }
    }
}
