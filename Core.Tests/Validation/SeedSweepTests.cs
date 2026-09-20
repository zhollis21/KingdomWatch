using System.Collections.Generic;
using System.Text;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Lifecycle;
using KingdomWatch.Core.Tests.Lifecycle;
using KingdomWatch.Core.Tests.Work;
using KingdomWatch.Core.Traversal;
using KingdomWatch.Harness;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Validation
{
    /// <summary>
    /// Section 5's *"then fuzz thousands of seeds"*, at the scale a test suite
    /// can afford. Runs a world per seed, validating after every simulated
    /// year, so a break is reported as the seed and the year it happened in.
    /// </summary>
    /// <remarks>
    /// Here rather than in the harness because the harness has no world to
    /// run: <c>Harness/Program.cs</c> drives <see cref="SchedulerSoak"/>,
    /// which has no people in it, until #17 lands the two-band run. The
    /// composition roots that do assemble a world - <see cref="DemographicWorld"/>
    /// and <see cref="WorkWorld"/> - live in this assembly. When #17 arrives,
    /// this sweep moves to the harness and keeps its rules.
    ///
    /// Both worlds are swept because they cover different ground:
    /// <see cref="DemographicWorld"/> drives births, deaths, courtship and
    /// conception, and <see cref="WorkWorld"/> is the only one with
    /// settlements, bands and work tasks - so it is the only one that can
    /// break the community, job and supply rules at all.
    ///
    /// Seed count is deliberately modest. The point of a sweep in CI is that
    /// a break in one seed is not hidden by another seed passing, not that it
    /// is exhaustive; the exhaustive run belongs in the harness where it can
    /// take minutes.
    /// </remarks>
    [TestFixture]
    public sealed class SeedSweepTests
    {
        private const int Seeds = 24;
        private const long Years = 30L;
        private const int BandSize = 45;

        [Test]
        public void A_demographic_world_holds_its_invariants_across_seeds()
        {
            Sweep(static (seed, validator, report) =>
            {
                var world = new DemographicWorld(
                    DemographicSettings.Default, seed, new TerrainGrid(16, 16, TerrainKind.Plains));

                var band = world.NewBand();

                foreach (var member in world.Generator.Generate(BandSize, new WorldPosition(4, 4)).Members)
                {
                    band.AddMember(member);
                }

                world.Matchmaking.Track(band);

                var spatial = new List<ICommunity> { band };
                var tracked = new List<ICommunity>();
                var bookings = new List<PendingBooking>();
                var scratch = new List<PendingBooking>();

                for (var year = 1L; year <= Years; year++)
                {
                    world.AdvanceYears(1L);

                    validator
                        .Reset()
                        .CheckPeople(world.People, world.Clock, world.Settings)
                        .CheckHouseholds(world.Households, world.People, world.Clock)
                        .CheckGenealogy(world.Genealogy, world.People, world.Clock)
                        .CheckSchedule(world.Clock, world.People, spatial, world.Households)
                        .CheckCommunities(spatial, world.People, world.Clock);

                    // Each system's own tracked set, read back through the
                    // surface it exposes for exactly this. A community one
                    // system still has booked events for and another has let
                    // go has no other symptom.
                    world.Hunger.CopyTrackedTo(tracked);
                    validator.CheckTracked(tracked, world.People, world.Clock);
                    world.Fertility.CopyTrackedTo(tracked);
                    validator.CheckTracked(tracked, world.People, world.Clock);
                    world.Matchmaking.CopyTrackedTo(tracked);
                    validator.CheckTracked(tracked, world.People, world.Clock);

                    // Every booking every system is holding, against the
                    // queue that is supposed to be carrying them.
                    bookings.Clear();
                    Gather(bookings, scratch, world.Hunger.CopyBookingsTo);
                    Gather(bookings, scratch, world.Fertility.CopyBookingsTo);
                    Gather(bookings, scratch, world.Matchmaking.CopyBookingsTo);
                    validator.CheckBookings(bookings, world.Clock);

                    if (!validator.IsClean)
                    {
                        report.AppendLine("year " + year + ", " + validator.Report(seed));
                        return;
                    }
                }
            });
        }

        [Test]
        public void A_working_world_holds_its_invariants_across_seeds()
        {
            Sweep(static (seed, validator, report) =>
            {
                var world = new WorkWorld(seed, WorkWorld.DefaultMap());
                var band = world.NewWanderingBand(WorkWorld.Camp, WorkWorld.PlentifulFood(12));

                world.JoinAdults(band, 12);

                var spatial = new List<ICommunity>();
                var tracked = new List<ICommunity>();
                var bookings = new List<PendingBooking>();
                var scratch = new List<PendingBooking>();

                for (var year = 1L; year <= Years; year++)
                {
                    world.Advance(SimulationTime.TicksPerYear);

                    // Where people physically are: the bands still wandering,
                    // read from the system that owns them rather than from a
                    // list this test kept, plus every settlement founded so
                    // far. Nobody may be in two of these.
                    world.Nomads.CopyTrackedTo(spatial);

                    for (var i = 0; i < world.Founding.All.Count; i++)
                    {
                        spatial.Add(world.Founding.All[i]);
                    }

                    validator
                        .Reset()
                        .CheckPeople(world.People, world.Clock, world.Demographics.Settings)
                        .CheckHouseholds(world.Demographics.Households, world.People, world.Clock)
                        .CheckSchedule(
                            world.Clock, world.People, spatial, world.Demographics.Households)
                        .CheckJobs(world.Jobs, world.People, world.Clock)
                        .CheckCommunities(spatial, world.People, world.Clock)
                        .CheckSupplies(band.SharedSupplies, band.Id, world.Clock);

                    world.Jobs.CopyTrackedTo(tracked);
                    validator.CheckTracked(tracked, world.People, world.Clock);
                    world.Hunger.CopyTrackedTo(tracked);
                    validator.CheckTracked(tracked, world.People, world.Clock);

                    bookings.Clear();
                    Gather(bookings, scratch, world.Jobs.CopyBookingsTo);
                    Gather(bookings, scratch, world.Hunger.CopyBookingsTo);
                    Gather(bookings, scratch, world.Nomads.CopyBookingsTo);
                    Gather(bookings, scratch, world.Demographics.Fertility.CopyBookingsTo);
                    Gather(bookings, scratch, world.Demographics.Matchmaking.CopyBookingsTo);
                    validator.CheckBookings(bookings, world.Clock);

                    for (var i = 0; i < world.Founding.All.Count; i++)
                    {
                        validator.CheckSupplies(
                            world.Founding.All[i].SharedSupplies, world.Founding.All[i].Id, world.Clock);
                    }

                    if (!validator.IsClean)
                    {
                        report.AppendLine("year " + year + ", " + validator.Report(seed));
                        return;
                    }
                }
            });
        }

        // CopyBookingsTo fills a list rather than appending to one, since
        // each system owns its own answer; gathering several means one
        // scratch buffer and a copy across.
        private static void Gather(
            List<PendingBooking> into, List<PendingBooking> scratch, System.Action<List<PendingBooking>> copy)
        {
            copy(scratch);
            into.AddRange(scratch);
        }

        // One validator and one report across every seed: a sweep that stopped
        // at the first bad seed would hide whether the break is one seed or
        // all of them, and that is the first thing anyone wants to know.
        private static void Sweep(System.Action<ulong, WorldValidator, StringBuilder> run)
        {
            var validator = new WorldValidator();
            var report = new StringBuilder();

            for (var seed = 1UL; seed <= Seeds; seed++)
            {
                run(seed, validator, report);
            }

            Assert.That(report.ToString(), Is.Empty);
        }
    }
}
