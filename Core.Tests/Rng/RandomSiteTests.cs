using System;
using System.Collections.Generic;
using System.Linq;
using KingdomWatch.Core.Data;
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
