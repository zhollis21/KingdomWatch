using System;
using System.Collections.Generic;
using System.Globalization;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Rng;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Rng
{
    /// <summary>
    /// The keyed RNG. The property worth most here is the one in
    /// <see cref="Zooming_in_does_not_change_what_happens_elsewhere"/> - it is
    /// the reason keyed draws exist instead of per-subsystem streams.
    /// </summary>
    [TestFixture]
    public sealed class DeterministicRngTests
    {
        private const ulong WorldSeed = 38471928UL;

        private static readonly EntityId Battle = new EntityId(EntityKind.MobileGroup, 7UL);
        private static readonly EntityId Attacker = new EntityId(EntityKind.Person, 3UL);
        private static readonly EntityId Household = new EntityId(EntityKind.Household, 12UL);

        private static DeterministicRng NewRng() => new DeterministicRng(WorldSeed);

        [Test]
        public void The_same_key_always_gives_the_same_value()
        {
            var first = NewRng().Key(RandomDomain.Combat).Mix(Battle).Mix(Attacker).Mix(2).NextUInt64();
            var second = NewRng().Key(RandomDomain.Combat).Mix(Battle).Mix(Attacker).Mix(2).NextUInt64();

            Assert.That(first, Is.EqualTo(second));
        }

        [Test]
        public void A_different_world_seed_gives_a_different_value()
        {
            var here = new DeterministicRng(WorldSeed).Key(RandomDomain.Combat).Mix(Battle).NextUInt64();
            var elsewhere = new DeterministicRng(WorldSeed + 1UL).Key(RandomDomain.Combat).Mix(Battle).NextUInt64();

            Assert.That(here, Is.Not.EqualTo(elsewhere));
        }

        [Test]
        public void Domains_do_not_collide_on_the_same_key()
        {
            var rng = NewRng();

            var combat = rng.Key(RandomDomain.Combat).Mix(Household).NextUInt64();
            var conception = rng.Key(RandomDomain.Conception).Mix(Household).NextUInt64();
            var social = rng.Key(RandomDomain.Social).Mix(Household).NextUInt64();

            Assert.Multiple(() =>
            {
                Assert.That(combat, Is.Not.EqualTo(conception));
                Assert.That(combat, Is.Not.EqualTo(social));
                Assert.That(conception, Is.Not.EqualTo(social));
            });
        }

        [Test]
        public void Key_order_matters()
        {
            var rng = NewRng();

            var forwards = rng.Key(RandomDomain.Combat).Mix(Battle).Mix(Attacker).NextUInt64();
            var backwards = rng.Key(RandomDomain.Combat).Mix(Attacker).Mix(Battle).NextUInt64();

            Assert.That(forwards, Is.Not.EqualTo(backwards));
        }

        [Test]
        public void Entity_kind_is_part_of_the_key()
        {
            // Person 1 and Settlement 1 must key different draws, or the kind
            // field on EntityId would be decorative.
            var rng = NewRng();

            var person = rng.Key(RandomDomain.Social).Mix(new EntityId(EntityKind.Person, 1UL)).NextUInt64();
            var settlement = rng.Key(RandomDomain.Social).Mix(new EntityId(EntityKind.Settlement, 1UL)).NextUInt64();

            Assert.That(person, Is.Not.EqualTo(settlement));
        }

        [Test]
        public void Every_sequence_number_gives_its_own_draw()
        {
            var rng = NewRng();
            var seen = new HashSet<ulong>();

            for (var sequence = 0; sequence < 1000; sequence++)
            {
                var value = rng.Key(RandomDomain.Combat).Mix(Battle).Mix(sequence).NextUInt64();
                Assert.That(seen.Add(value), Is.True, "Collision at sequence " + sequence + ".");
            }
        }

        [Test]
        public void Zooming_in_does_not_change_what_happens_elsewhere()
        {
            // The point of the whole design. Detailed combat draws hit, damage,
            // dodge, hit, damage; compressed combat draws once. With a stream,
            // the second battle would land at a different stream position
            // purely because the player happened to zoom in on the first. Keyed
            // draws have no position to disturb, so both paths agree.
            var rng = NewRng();

            ulong SecondBattleOutcome() =>
                rng.Key(RandomDomain.Combat)
                   .Mix(new EntityId(EntityKind.MobileGroup, 99UL))
                   .Mix(0)
                   .NextUInt64();

            ResolveCompressed(rng);
            var afterCompressedFirstBattle = SecondBattleOutcome();

            ResolveDetailed(rng, 250);
            var afterDetailedFirstBattle = SecondBattleOutcome();

            Assert.That(afterDetailedFirstBattle, Is.EqualTo(afterCompressedFirstBattle));
        }

        [Test]
        public void Event_ids_key_draws()
        {
            var rng = NewRng();

            var first = rng.Key(RandomDomain.Social).Mix(new EventId(1UL)).NextUInt64();
            var second = rng.Key(RandomDomain.Social).Mix(new EventId(2UL)).NextUInt64();

            Assert.That(first, Is.Not.EqualTo(second));
        }

        [Test]
        public void An_event_id_does_not_key_the_same_draw_as_its_bare_value()
        {
            // Event#7 must not collide with the plain number 7, or a call site
            // keyed on an event id would share a draw with one keyed on a
            // counter. A collision like that is invisible - it does not crash,
            // fail a test, or disturb the cross-platform hash - and surfaces
            // only as two independent things moving in lockstep.
            var rng = NewRng();

            var viaEvent = rng.Key(RandomDomain.Social).Mix(new EventId(7UL)).NextUInt64();
            var viaValue = rng.Key(RandomDomain.Social).Mix(7UL).NextUInt64();

            Assert.That(viaEvent, Is.Not.EqualTo(viaValue));
        }

        [Test]
        public void An_entity_id_does_not_key_the_same_draw_as_its_parts()
        {
            // EntityKind.Person is 1, so without a type tag Person#42 would key
            // exactly what the pair (1, 42) keys - and small integers are what
            // counters and indices look like, so a call site could reach that
            // collision without trying.
            var rng = NewRng();

            var viaEntity = rng.Key(RandomDomain.Social)
                .Mix(new EntityId(EntityKind.Person, 42UL)).Mix(3).NextUInt64();
            var viaParts = rng.Key(RandomDomain.Social)
                .Mix(1UL).Mix(42UL).Mix(3).NextUInt64();

            Assert.That(viaEntity, Is.Not.EqualTo(viaParts));
        }

        [Test]
        public void An_event_id_does_not_key_the_same_draw_as_an_entity_id()
        {
            var rng = NewRng();

            var viaEvent = rng.Key(RandomDomain.Social).Mix(new EventId(7UL)).NextUInt64();
            var viaEntity = rng.Key(RandomDomain.Social)
                .Mix(new EntityId(EntityKind.Person, 7UL)).NextUInt64();

            Assert.That(viaEvent, Is.Not.EqualTo(viaEntity));
        }

        [Test]
        public void Below_rejects_the_short_final_block_rather_than_biasing()
        {
            // The unbiasedness guarantee, exercised rather than assumed. With
            // an upper bound of 2^63 + 1 the last usable block ends at
            // 2^63 - 1, so roughly half of all first draws fall in the rejected
            // region and must be redrawn. Without the rejection this would be a
            // plain modulo and small results would be about twice as likely as
            // large ones.
            const ulong exclusiveMax = (1UL << 63) + 1UL;
            const ulong threshold = (1UL << 63) - 1UL;

            var rng = NewRng();
            var sawARejection = false;

            for (var i = 0; i < 200; i++)
            {
                var key = rng.Key(RandomDomain.Social).Mix(i);
                var firstDraw = key.NextUInt64();
                var result = key.Below(exclusiveMax);

                Assert.That(result, Is.LessThan(exclusiveMax));

                if (firstDraw < threshold)
                {
                    // Rejected, so the answer came from a later attempt.
                    sawARejection = true;
                    Assert.That(result, Is.Not.EqualTo(firstDraw % exclusiveMax));
                }
                else
                {
                    Assert.That(result, Is.EqualTo(firstDraw % exclusiveMax));
                }
            }

            Assert.That(sawARejection, Is.True, "This test never exercised the rejection path.");
        }

        [Test]
        public void Below_handles_the_largest_possible_bound()
        {
            var rng = NewRng();

            for (var i = 0; i < 100; i++)
            {
                Assert.That(
                    rng.Key(RandomDomain.Social).Mix(i).Below(ulong.MaxValue),
                    Is.LessThan(ulong.MaxValue));
            }
        }

        [Test]
        public void Range_spans_the_whole_int_range_without_overflowing()
        {
            // Range does its arithmetic in long and casts back to int. The
            // widest possible span is the case where that could go wrong.
            var rng = NewRng();

            for (var i = 0; i < 500; i++)
            {
                var value = rng.Key(RandomDomain.Social).Mix(i).Range(int.MinValue, int.MaxValue);
                Assert.That(value, Is.InRange(int.MinValue, int.MaxValue - 1));
            }
        }

        [Test]
        public void Below_stays_in_range()
        {
            var rng = NewRng();

            for (var i = 0; i < 2000; i++)
            {
                var value = rng.Key(RandomDomain.Social).Mix(i).Below(7UL);
                Assert.That(value, Is.LessThan(7UL));
            }
        }

        [Test]
        public void Below_one_is_always_zero()
        {
            var rng = NewRng();

            for (var i = 0; i < 100; i++)
            {
                Assert.That(rng.Key(RandomDomain.Social).Mix(i).Below(1UL), Is.EqualTo(0UL));
            }
        }

        [Test]
        public void Below_zero_is_refused()
        {
            Assert.That(
                () => NewRng().Key(RandomDomain.Social).Mix(1).Below(0UL),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Below_covers_its_whole_range_roughly_evenly()
        {
            // Not a statistical proof, just a guard against a mixer that has
            // gone obviously lopsided. Six buckets, 60,000 draws, so each
            // bucket expects 10,000.
            var rng = NewRng();
            var buckets = new int[6];

            for (var i = 0; i < 60000; i++)
            {
                buckets[rng.Key(RandomDomain.Social).Mix(i).Below(6UL)]++;
            }

            foreach (var count in buckets)
            {
                Assert.That(count, Is.InRange(9400, 10600));
            }
        }

        [Test]
        public void Range_stays_within_its_bounds()
        {
            var rng = NewRng();

            for (var i = 0; i < 2000; i++)
            {
                var value = rng.Key(RandomDomain.Social).Mix(i).Range(-5, 5);
                Assert.That(value, Is.InRange(-5, 4));
            }
        }

        [Test]
        public void Range_refuses_an_empty_or_inverted_span()
        {
            var key = NewRng().Key(RandomDomain.Social).Mix(1);

            Assert.Multiple(() =>
            {
                Assert.That(() => key.Range(5, 5), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => key.Range(5, 4), Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void Certain_chances_resolve_without_a_draw()
        {
            var key = NewRng().Key(RandomDomain.Conception).Mix(Household);

            Assert.Multiple(() =>
            {
                Assert.That(key.Chance(0, 10), Is.False);
                Assert.That(key.Chance(10, 10), Is.True);
            });
        }

        [Test]
        public void Chance_lands_near_its_stated_odds()
        {
            var rng = NewRng();
            var hits = 0;

            for (var i = 0; i < 60000; i++)
            {
                if (rng.Key(RandomDomain.Conception).Mix(Household).Mix(i).Chance(1, 4))
                {
                    hits++;
                }
            }

            Assert.That(hits, Is.InRange(14400, 15600));
        }

        [Test]
        public void Chance_refuses_nonsense_odds()
        {
            var key = NewRng().Key(RandomDomain.Conception).Mix(Household);

            Assert.Multiple(() =>
            {
                Assert.That(() => key.Chance(1, 0), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => key.Chance(1, -1), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => key.Chance(-1, 10), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => key.Chance(11, 10), Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void A_draw_must_belong_to_a_real_domain()
        {
            Assert.That(
                () => NewRng().Key(RandomDomain.None),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Golden_vectors_pin_the_algorithm()
        {
            // Changing the mixer changes every roll in every world, past and
            // future. These values exist so that can never happen quietly - if
            // this test fails and the change was deliberate, every saved world
            // is invalidated and that decision belongs to a person.
            var rng = NewRng();

            var lines = new List<string>
            {
                Line("combat", rng.Key(RandomDomain.Combat).Mix(Battle).Mix(Attacker).Mix(2).NextUInt64()),
                Line("conception", rng.Key(RandomDomain.Conception).Mix(Household).Mix(1).NextUInt64()),
                Line("social", rng.Key(RandomDomain.Social).Mix(Attacker).Mix(4).Mix(9).NextUInt64()),
                Line("event", rng.Key(RandomDomain.Social).Mix(new EventId(7UL)).NextUInt64()),
                Line("below100", rng.Key(RandomDomain.Social).Mix(Attacker).Below(100UL)),
                Line("range", (ulong)(long)rng.Key(RandomDomain.Social).Mix(Attacker).Range(0, 1000)),
            };

            Assert.That(
                string.Join("|", lines),
                Is.EqualTo(
                    "combat=13514175954488880116"
                    + "|conception=12047574463750113385"
                    + "|social=16099087809957385506"
                    + "|event=5573920518276458559"
                    + "|below100=67"
                    + "|range=467"));
        }

        private static string Line(string name, ulong value) =>
            name + "=" + value.ToString(CultureInfo.InvariantCulture);

        /// <summary>Compressed combat: one draw resolves the whole battle.</summary>
        private static ulong ResolveCompressed(DeterministicRng rng) =>
            rng.Key(RandomDomain.Combat)
               .Mix(new EntityId(EntityKind.MobileGroup, 1UL))
               .Mix(0)
               .NextUInt64();

        /// <summary>Detailed combat: a draw per blow, many of them.</summary>
        private static ulong ResolveDetailed(DeterministicRng rng, int blows)
        {
            var last = 0UL;

            for (var blow = 0; blow < blows; blow++)
            {
                last = rng.Key(RandomDomain.Combat)
                          .Mix(new EntityId(EntityKind.MobileGroup, 1UL))
                          .Mix(blow)
                          .NextUInt64();
            }

            return last;
        }
    }
}
