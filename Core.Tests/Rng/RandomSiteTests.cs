using System;
using System.Collections.Generic;
using System.Linq;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Lifecycle;
using KingdomWatch.Core.Nomadic;
using KingdomWatch.Core.Tests.Lifecycle;
using KingdomWatch.Core.Tests.Work;
using KingdomWatch.Core.Rng;
using KingdomWatch.Core.Traversal;
using KingdomWatch.Core.WorldGen;
using KingdomWatch.Harness;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Rng
{
    /// <summary>
    /// Call-site separation (#57). Two decisions that share a domain must not
    /// share a draw, and the thing that guarantees it is that every key names
    /// its <see cref="RandomSite"/> - a check rather than a comment.
    /// </summary>
    [TestFixture]
    public sealed class RandomSiteTests
    {
        private const ulong WorldSeed = 38471928UL;

        // Enough food that the wandering run is about choosing camps rather
        // than about starving.
        private const int BandMembers = 8;

        private static readonly EntityId Household = new EntityId(EntityKind.Household, 12UL);

        [Test]
        public void Two_sites_in_one_domain_key_different_draws()
        {
            // The case the domain alone cannot cover: worldgen legitimately
            // lays down terrain and rivers under one domain, and before sites
            // existed the two were kept apart by a local const nobody checked.
            var rng = new DeterministicRng(WorldSeed);

            var terrain = rng.Key(RandomDomain.WorldGen, RandomSite.Terrain).Mix(4).Mix(9).NextUInt64();
            var river = rng.Key(RandomDomain.WorldGen, RandomSite.RiverDrift).Mix(4).Mix(9).NextUInt64();

            Assert.That(terrain, Is.Not.EqualTo(river));
        }

        [Test]
        public void Every_site_keys_a_different_draw_from_every_other()
        {
            // A duplicated value in the enum - the obvious copy-paste slip -
            // would put two call sites back on one key while still compiling
            // and still reading correctly at both.
            var rng = new DeterministicRng(WorldSeed);
            var byValue = new Dictionary<ulong, RandomSite>();

            foreach (RandomSite site in Enum.GetValues(typeof(RandomSite)))
            {
                var value = rng.Key(RandomDomain.WorldGen, site).Mix(Household).NextUInt64();

                Assert.That(
                    byValue.TryAdd(value, site),
                    Is.True,
                    site + " keys the same draw as " + byValue.GetValueOrDefault(value) + ".");
            }
        }

        [Test]
        public void Site_values_are_unique()
        {
            // Said directly as well as through the draws, so a failure names
            // the cause rather than the symptom.
            var values = Enum.GetValues(typeof(RandomSite)).Cast<RandomSite>().ToArray();

            Assert.That(values.Distinct().Count(), Is.EqualTo(values.Length));
        }

        [Test]
        public void An_undefined_site_is_rejected()
        {
            // A cast slips any int past the enum, and an unrecognised site
            // would key a draw under a call site that does not exist - which
            // is the collision declaring one is meant to prevent.
            var rng = new DeterministicRng(WorldSeed);

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => rng.Key(RandomDomain.WorldGen, (RandomSite)999),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => rng.Key(RandomDomain.WorldGen, (RandomSite)(-1)),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => rng.Key(RandomDomain.WorldGen, (RandomSite)0),
                    Throws.TypeOf<ArgumentOutOfRangeException>(),
                    "RandomSite has no zero member, so a defaulted field must not pass.");
            });
        }

        [Test]
        public void The_site_is_mixed_before_the_callers_own_components()
        {
            // Position is load-bearing. Mixed at a fixed depth, two sites hand
            // the caller two different states to build on; mixed wherever each
            // call site happened to put it, order would decide, and order is
            // exactly what a caller varies.
            var rng = new DeterministicRng(WorldSeed);

            var declared = rng.Key(RandomDomain.WorldGen, RandomSite.Terrain).Mix(7).NextUInt64();
            var foldedInLater = rng.Key(RandomDomain.WorldGen, RandomSite.RiverDrift)
                .Mix(7).Mix((ulong)RandomSite.Terrain).NextUInt64();

            Assert.That(declared, Is.Not.EqualTo(foldedInLater));
        }

        [Test]
        public void An_observer_is_told_which_site_drew_what()
        {
            var seen = new List<string>();
            var rng = new DeterministicRng(WorldSeed, new Recorder(seen));

            var value = rng.Key(RandomDomain.Mortality, RandomSite.LifeTableRoll).Mix(Household).NextUInt64();

            Assert.That(seen, Is.EqualTo(new[] { "Mortality/LifeTableRoll/" + value }));
        }

        [Test]
        public void A_certainty_draws_nothing_and_so_reports_nothing()
        {
            // Chance short-circuits at 0 and at the denominator, deliberately,
            // so there is no draw to report and nothing that could collide.
            var seen = new List<string>();
            var rng = new DeterministicRng(WorldSeed, new Recorder(seen));
            var key = rng.Key(RandomDomain.Conception, RandomSite.ConceptionRoll).Mix(Household);

            key.Chance(0, 10);
            key.Chance(10, 10);

            Assert.That(seen, Is.Empty);
        }

        [Test]
        public void Watching_does_not_change_what_is_drawn()
        {
            // The observer is the one thing here that could quietly make the
            // harness and the device disagree, which is the failure the whole
            // determinism story is built to avoid.
            var watched = new DeterministicRng(WorldSeed, new DrawCollisionDetector());
            var unwatched = new DeterministicRng(WorldSeed);

            var withObserver = PlaceholderMap.Generate(40, 30, watched);
            var without = PlaceholderMap.Generate(40, 30, unwatched);

            for (var y = 0; y < 30; y++)
            {
                for (var x = 0; x < 40; x++)
                {
                    var at = new WorldPosition(x, y);
                    Assert.That(withObserver[at], Is.EqualTo(without[at]), "Terrain differs at " + at + ".");
                }
            }
        }

        [Test]
        public void Watching_a_whole_run_changes_nothing_about_where_it_ends_up()
        {
            // The standing rule for any new IRandomDrawObserver, and the one
            // this file exists to make cheap to copy (AGENTS.md). The observer
            // is non-null in the harness and null on device, so anything an
            // implementation *does* - drawing, scheduling, writing to a store -
            // happens on one side of the comparison the determinism story
            // rests on and not the other. The two would each be perfectly
            // reproducible and quietly different, and the difference would
            // read as an IL2CPP divergence rather than as diagnostic code.
            //
            // Worldgen alone is too thin to prove that: it takes two sites and
            // touches nothing but a grid. This drives the demographic systems
            // through births, deaths, courtship and conception, so the draws
            // are keyed on durable ids and their results feed back into the
            // state being compared.
            //
            // The digest is a stand-in for the canonical world hash #13 owns.
            // Once that exists, this assertion should become "the same hash"
            // and stop enumerating fields by hand.
            // Large enough to deal children: a band of twelve leaves every
            // couple outside the window TryChildAges needs, so BandChildAge
            // and BandChildSex are never drawn at all.
            const int Size = 45;
            const long Years = 40L;

            var detector = new DrawCollisionDetector();
            var watched = Run(detector, Size, Years);
            var unwatched = Run(null, Size, Years);

            Assert.Multiple(() =>
            {
                Assert.That(watched, Is.EqualTo(unwatched));
                Assert.That(watched, Does.Contain("|"), "the run produced no people to compare");

                // The detector is attached anyway, so its answer is free; an
                // observer whose findings nobody reads is just overhead.
                Assert.That(detector.Collisions, Is.Empty);
                Assert.That(detector.Draws, Is.GreaterThan(1000), "this run barely drew anything");
            });
        }

        // Sorted by durable id and serialised in a fixed field order, the way
        // section 5 asks a canonical hash to be built.
        private static string Run(IRandomDrawObserver? observer, int size, long years)
        {
            var world = new DemographicWorld(
                DemographicSettings.Default, WorldSeed, new TerrainGrid(16, 16, TerrainKind.Plains), observer);

            if (observer is Meddler meddler)
            {
                meddler.World = world;
            }

            var band = world.NewBand();
            var generated = world.Generator.Generate(size, new WorldPosition(4, 4));

            foreach (var member in generated.Members)
            {
                band.AddMember(member);
            }

            // Matchmaking runs off a booked CourtshipDue and Track is what
            // books one; DemographicWorld tracks the other systems but not
            // this, so without it the run never reaches RandomSite.MarriageRoll.
            world.Matchmaking.Track(band);

            world.AdvanceYears(years);

            var lines = new List<string>();
            var records = world.People.RecordSpan();

            for (var i = 0; i < records.Length; i++)
            {
                var record = records[i];

                if (record.Id.IsNone)
                {
                    continue;
                }

                lines.Add(
                    record.Id + "/" + record.Sex + "/" + record.AgeStage + "/" + record.BornTick
                    + "/" + record.Health + "/" + record.Position + "/" + record.Household
                    + "/" + record.BirthCulture + "/" + record.Assimilation + "/" + record.Job
                    + "/" + record.PregnancyDue + "/" + record.PendingMortalityCheck);
            }

            lines.Sort(StringComparer.Ordinal);
            return string.Join("|", lines);
        }

        [Test]
        public void A_real_run_draws_no_two_values_from_different_sites()
        {
            // The backstop actually running against real systems rather than
            // a contrived key: worldgen takes two sites over thousands of
            // draws, which is where a collision would show if one were
            // reachable.
            var detector = new DrawCollisionDetector();

            PlaceholderMap.Generate(60, 40, new DeterministicRng(WorldSeed, detector));

            Assert.Multiple(() =>
            {
                Assert.That(detector.Collisions, Is.Empty);
                Assert.That(detector.Draws, Is.GreaterThan(2000), "This run barely drew anything.");
            });
        }

        [Test]
        public void A_world_with_no_observer_draws_the_same_as_one_watched()
        {
            // The null observer is the production path, so it is worth saying
            // outright that passing one explicitly is the same as passing none.
            var implicitly_unwatched = new DeterministicRng(WorldSeed);
            var explicitly_unwatched = new DeterministicRng(WorldSeed, null);

            Assert.That(
                explicitly_unwatched.Key(RandomDomain.Mortality, RandomSite.LifeTableRoll).Mix(Household).NextUInt64(),
                Is.EqualTo(implicitly_unwatched.Key(RandomDomain.Mortality, RandomSite.LifeTableRoll).Mix(Household).NextUInt64()));
        }

        [Test]
        public void Two_domains_sharing_a_site_still_count_as_different_call_sites()
        {
            // The pair is what identifies a call site, not either half. A
            // detector comparing only the site would call this agreement
            // legitimate and stay quiet about a real collision.
            var detector = new DrawCollisionDetector();

            detector.Drew(RandomDomain.WorldGen, RandomSite.Terrain, 11UL);
            detector.Drew(RandomDomain.Mortality, RandomSite.Terrain, 11UL);

            Assert.That(detector.Collisions, Has.Count.EqualTo(1));
        }

        [Test]
        public void A_wandering_run_draws_no_two_values_from_different_sites()
        {
            // RandomSite.CampChoice is drawn from NomadicBands and nowhere
            // else, so without a run of its own it is the one production site
            // no collision check ever sees.
            var detector = new DrawCollisionDetector();
            var world = new WorkWorld(WorldSeed, WorkWorld.DefaultMap(), DemographicSettings.Default, detector);

            world.NewWanderingBand(WorkWorld.Camp, WorkWorld.PlentifulFood(BandMembers));
            world.Advance(NomadicBands.CampDays * SimulationTime.TicksPerDay * 4L);

            Assert.Multiple(() =>
            {
                Assert.That(detector.Collisions, Is.Empty);
                Assert.That(detector.Draws, Is.GreaterThan(0), "the band never chose a camp");
            });
        }

        [Test]
        public void Every_site_a_production_call_site_uses_is_covered_by_a_collision_check()
        {
            // The guard on this file rather than on the code. Copilot found
            // one run whose detector was discarded; the wider problem was that
            // only two of eleven sites were covered by any collision check at
            // all, and nothing would have said so when the twelfth arrived.
            //
            // So the sites the runs above actually exercise are collected here
            // and checked against the enum. A new RandomSite fails this until
            // someone either drives it under a detector or states here why it
            // cannot be.
            var seen = new SiteLog();

            PlaceholderMap.Generate(60, 40, new DeterministicRng(WorldSeed, seen));
            Run(seen, 45, 40L);

            var wandering = new WorkWorld(WorldSeed, WorkWorld.DefaultMap(), DemographicSettings.Default, seen);
            wandering.NewWanderingBand(WorkWorld.Camp, WorkWorld.PlentifulFood(BandMembers));
            wandering.Advance(NomadicBands.CampDays * SimulationTime.TicksPerDay * 4L);

            // Declared ahead of the system that will draw it, as
            // RandomDomain.Combat is. Nothing in Core keys a draw on it yet,
            // so there is no run that could reach it - remove it from here the
            // moment combat exists.
            var notYetDrawnInProduction = new[] { RandomSite.BattleOutcome };

            var uncovered = Enum.GetValues(typeof(RandomSite))
                .Cast<RandomSite>()
                .Where(site => !seen.Seen.Contains(site) && !notYetDrawnInProduction.Contains(site))
                .ToArray();

            Assert.That(
                uncovered,
                Is.Empty,
                "No collision-checked run draws from these sites, so a collision involving one "
                + "would go unnoticed. Drive them under a DrawCollisionDetector, or say here why not.");
        }

        private sealed class SiteLog : IRandomDrawObserver
        {
            internal HashSet<RandomSite> Seen { get; } = new HashSet<RandomSite>();

            public void Drew(RandomDomain domain, RandomSite site, ulong value) => Seen.Add(site);
        }

        [Test]
        public void The_detector_reports_two_sites_that_share_a_value()
        {
            // Proving the backstop can fail. Fed directly, because contriving
            // a real 2^-64 collision is the thing that cannot be done.
            var detector = new DrawCollisionDetector();

            detector.Drew(RandomDomain.WorldGen, RandomSite.Terrain, 5UL);
            detector.Drew(RandomDomain.WorldGen, RandomSite.Terrain, 5UL);
            detector.Drew(RandomDomain.WorldGen, RandomSite.RiverDrift, 5UL);

            Assert.Multiple(() =>
            {
                Assert.That(detector.Collisions, Has.Count.EqualTo(1), "One site drawing twice is not a collision.");
                Assert.That(detector.Collisions[0], Does.Contain("Terrain").And.Contain("RiverDrift"));
                Assert.That(detector.Draws, Is.EqualTo(3));
            });
        }

        [Test]
        public void An_observer_that_writes_to_the_world_is_caught_by_that_comparison()
        {
            // The rule with teeth. Without this, the test above has never been
            // seen to fail and could be passing because both sides are equally
            // broken.
            //
            // Note what does NOT diverge: an observer that merely *draws* is a
            // recursion hazard but not a determinism one, because keyed draws
            // have no stream position for an extra draw to advance. Writing to
            // the world is the failure that matters, so that is what is
            // mutated here.
            var meddler = new Meddler();
            var meddled = Run(meddler, 12, 40L);
            var clean = Run(null, 12, 40L);

            Assert.Multiple(() =>
            {
                Assert.That(meddler.Struck, Is.True, "the meddler never got a draw to act on");
                Assert.That(meddled, Is.Not.EqualTo(clean));
            });
        }

        // An observer that does the one thing the contract forbids, so the
        // equivalence test above can be seen to fail.
        private sealed class Meddler : IRandomDrawObserver
        {
            private int _draws;

            internal DemographicWorld? World { get; set; }

            internal bool Struck { get; private set; }

            public void Drew(RandomDomain domain, RandomSite site, ulong value)
            {
                // Once only, and to a field nothing reads back, so what the
                // comparison catches is the write itself rather than a
                // cascade the write set off. A field the simulation acts on
                // would also work, but only when its owner happens to outlive
                // the run - and a victim who dies either way takes the
                // evidence with them.
                if (Struck || World is null || ++_draws < 20)
                {
                    return;
                }

                var records = World.People.RecordSpan();

                // Every living person, not one of them: a single victim who
                // dies before the run ends takes the evidence with them, and
                // over forty years most of them do.
                for (var i = 0; i < records.Length; i++)
                {
                    if (records[i].Id.IsNone)
                    {
                        continue;
                    }

                    records[i].Assimilation = (byte)(records[i].Assimilation + 1);
                    Struck = true;
                }
            }
        }

        private sealed class Recorder : IRandomDrawObserver
        {
            private readonly List<string> _seen;

            internal Recorder(List<string> seen)
            {
                _seen = seen;
            }

            public void Drew(RandomDomain domain, RandomSite site, ulong value) =>
                _seen.Add(domain + "/" + site + "/" + value);
        }
    }
}
