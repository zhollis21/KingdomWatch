using System;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Relationships;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Relationships
{
    [TestFixture]
    public sealed class SocialTiesTests
    {
        private static readonly SocialTieSettings Settings = new SocialTieSettings(3, SimulationTime.TicksPerDay);

        [Test]
        public void Adjusting_creates_a_tie_then_moves_it()
        {
            var ids = new IdAllocator();
            var ties = new SocialTies(Settings);
            var aldric = ids.Next(EntityKind.Person);
            var mira = ids.Next(EntityKind.Person);

            ties.Adjust(aldric, mira, 10, 0, 5, SimulationTime.FromDays(1));
            ties.Adjust(aldric, mira, -3, 4, 1, SimulationTime.FromDays(2));

            Assert.Multiple(() =>
            {
                Assert.That(ties.TryGet(aldric, mira, out var tie), Is.True);
                Assert.That(tie.Liking, Is.EqualTo(7));
                Assert.That(tie.Resentment, Is.EqualTo(4));
                Assert.That(tie.Familiarity, Is.EqualTo(6));
                Assert.That(tie.LastTouched, Is.EqualTo(SimulationTime.FromDays(2)));
                Assert.That(ties.Ties(aldric).Length, Is.EqualTo(1), "moved, not duplicated");
            });
        }

        [Test]
        public void Ties_are_directed()
        {
            var ids = new IdAllocator();
            var ties = new SocialTies(Settings);
            var aldric = ids.Next(EntityKind.Person);
            var mira = ids.Next(EntityKind.Person);

            ties.Adjust(aldric, mira, 20, 0, 0, SimulationTime.Zero);

            Assert.Multiple(() =>
            {
                Assert.That(ties.TryGet(aldric, mira, out _), Is.True);
                Assert.That(ties.TryGet(mira, aldric, out _), Is.False);
                Assert.That(ties.Ties(mira).Length, Is.Zero);
            });
        }

        [Test]
        public void Values_are_clamped_to_their_ranges()
        {
            var ids = new IdAllocator();
            var ties = new SocialTies(Settings);
            var aldric = ids.Next(EntityKind.Person);
            var mira = ids.Next(EntityKind.Person);

            ties.Adjust(aldric, mira, 1000, -1000, 1000, SimulationTime.Zero);
            ties.TryGet(aldric, mira, out var high);
            ties.Adjust(aldric, mira, -1000, 1000, -1000, SimulationTime.Zero);
            ties.TryGet(aldric, mira, out var low);

            Assert.Multiple(() =>
            {
                Assert.That(high.Liking, Is.EqualTo(SocialTie.MaxMagnitude));
                Assert.That(high.Resentment, Is.Zero, "resentment has no negative");
                Assert.That(high.Familiarity, Is.EqualTo(SocialTie.MaxMagnitude));
                Assert.That(low.Liking, Is.EqualTo(-SocialTie.MaxMagnitude));
                Assert.That(low.Resentment, Is.EqualTo(SocialTie.MaxMagnitude));
                Assert.That(low.Familiarity, Is.Zero);
            });
        }

        [Test]
        public void Adjusting_by_the_extremes_of_int_clamps_rather_than_wrapping()
        {
            var ids = new IdAllocator();
            var ties = new SocialTies(Settings);
            var aldric = ids.Next(EntityKind.Person);
            var mira = ids.Next(EntityKind.Person);

            ties.Adjust(aldric, mira, int.MaxValue, int.MaxValue, int.MaxValue, SimulationTime.Zero);
            ties.Adjust(aldric, mira, int.MaxValue, int.MaxValue, int.MaxValue, SimulationTime.Zero);
            ties.TryGet(aldric, mira, out var high);
            ties.Adjust(aldric, mira, int.MinValue, int.MinValue, int.MinValue, SimulationTime.Zero);
            ties.Adjust(aldric, mira, int.MinValue, int.MinValue, int.MinValue, SimulationTime.Zero);
            ties.TryGet(aldric, mira, out var low);

            Assert.Multiple(() =>
            {
                Assert.That(high.Liking, Is.EqualTo(SocialTie.MaxMagnitude));
                Assert.That(high.Resentment, Is.EqualTo(SocialTie.MaxMagnitude));
                Assert.That(high.Familiarity, Is.EqualTo(SocialTie.MaxMagnitude));
                Assert.That(low.Liking, Is.EqualTo(-SocialTie.MaxMagnitude));
                Assert.That(low.Resentment, Is.Zero);
                Assert.That(low.Familiarity, Is.Zero);
            });
        }

        [Test]
        public void Decay_that_fails_part_way_changes_nothing()
        {
            // Two ties touched at different times, then a decay at an instant
            // between them: the later one is a time-runs-backwards error, and
            // the earlier one must not have been decayed before it was found.
            var ids = new IdAllocator();
            var ties = new SocialTies(Settings);
            var aldric = ids.Next(EntityKind.Person);
            var early = ids.Next(EntityKind.Person);
            var late = ids.Next(EntityKind.Person);
            ties.Adjust(aldric, late, 10, 0, 0, SimulationTime.FromDays(5));
            ties.Adjust(aldric, early, 10, 0, 0, SimulationTime.FromDays(1));

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => ties.Decay(aldric, SimulationTime.FromDays(3)),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(ties.TryGet(aldric, early, out var tie) && tie.Liking == 10, Is.True);
            });
        }

        [Test]
        public void At_the_cap_the_weakest_tie_is_evicted_lowest_id_breaking_ties()
        {
            var ids = new IdAllocator();
            var ties = new SocialTies(Settings);
            var aldric = ids.Next(EntityKind.Person);
            var weakLow = ids.Next(EntityKind.Person);
            var strong = ids.Next(EntityKind.Person);
            var weakHigh = ids.Next(EntityKind.Person);
            var newcomer = ids.Next(EntityKind.Person);

            ties.Adjust(aldric, strong, 50, 0, 0, SimulationTime.Zero);
            ties.Adjust(aldric, weakHigh, 0, 2, 0, SimulationTime.Zero);
            ties.Adjust(aldric, weakLow, -2, 0, 0, SimulationTime.Zero);

            ties.Adjust(aldric, newcomer, 1, 0, 0, SimulationTime.Zero);

            Assert.Multiple(() =>
            {
                Assert.That(ties.Ties(aldric).Length, Is.EqualTo(Settings.MaxTiesPerPerson));
                Assert.That(ties.TryGet(aldric, weakLow, out _), Is.False, "equal weight, lower id goes");
                Assert.That(ties.TryGet(aldric, weakHigh, out _), Is.True);
                Assert.That(ties.TryGet(aldric, strong, out _), Is.True);
                Assert.That(ties.TryGet(aldric, newcomer, out _), Is.True);
            });
        }

        [Test]
        public void Adjusting_an_existing_tie_at_the_cap_evicts_nobody()
        {
            var ids = new IdAllocator();
            var ties = new SocialTies(Settings);
            var aldric = ids.Next(EntityKind.Person);
            var a = ids.Next(EntityKind.Person);
            var b = ids.Next(EntityKind.Person);
            var c = ids.Next(EntityKind.Person);
            ties.Adjust(aldric, a, 1, 0, 0, SimulationTime.Zero);
            ties.Adjust(aldric, b, 1, 0, 0, SimulationTime.Zero);
            ties.Adjust(aldric, c, 1, 0, 0, SimulationTime.Zero);

            ties.Adjust(aldric, a, 5, 0, 0, SimulationTime.Zero);

            Assert.That(ties.Ties(aldric).Length, Is.EqualTo(3));
        }

        [Test]
        public void Decay_moves_every_value_toward_zero_by_whole_steps_and_keeps_the_remainder()
        {
            var ids = new IdAllocator();
            var ties = new SocialTies(Settings);
            var aldric = ids.Next(EntityKind.Person);
            var mira = ids.Next(EntityKind.Person);
            ties.Adjust(aldric, mira, -10, 6, 2, SimulationTime.FromDays(1));

            // Two and a half days: two steps, half a step still owed.
            ties.Decay(aldric, SimulationTime.FromHours(24 + 60));
            ties.TryGet(aldric, mira, out var afterTwo);

            // Half a day later the owed half completes a third step.
            ties.Decay(aldric, SimulationTime.FromHours(24 + 72));
            ties.TryGet(aldric, mira, out var afterThree);

            Assert.Multiple(() =>
            {
                Assert.That(afterTwo.Liking, Is.EqualTo(-8));
                Assert.That(afterTwo.Resentment, Is.EqualTo(4));
                Assert.That(afterTwo.Familiarity, Is.Zero, "cannot pass zero");
                Assert.That(afterTwo.LastTouched, Is.EqualTo(SimulationTime.FromDays(3)), "stamp moves by whole steps");
                Assert.That(afterThree.Liking, Is.EqualTo(-7));
                Assert.That(afterThree.Resentment, Is.EqualTo(3));
                Assert.That(afterThree.LastTouched, Is.EqualTo(SimulationTime.FromDays(4)));
            });
        }

        [Test]
        public void Decaying_twice_at_the_same_instant_decays_once()
        {
            var ids = new IdAllocator();
            var ties = new SocialTies(Settings);
            var aldric = ids.Next(EntityKind.Person);
            var mira = ids.Next(EntityKind.Person);
            ties.Adjust(aldric, mira, 10, 0, 0, SimulationTime.Zero);

            ties.Decay(aldric, SimulationTime.FromDays(2));
            ties.Decay(aldric, SimulationTime.FromDays(2));
            ties.TryGet(aldric, mira, out var tie);

            Assert.That(tie.Liking, Is.EqualTo(8));
        }

        [Test]
        public void A_tie_that_decays_to_nothing_is_removed_and_the_rest_keep_their_order()
        {
            var ids = new IdAllocator();
            var ties = new SocialTies(Settings);
            var aldric = ids.Next(EntityKind.Person);
            var first = ids.Next(EntityKind.Person);
            var fading = ids.Next(EntityKind.Person);
            var last = ids.Next(EntityKind.Person);
            ties.Adjust(aldric, first, 30, 0, 0, SimulationTime.Zero);
            ties.Adjust(aldric, fading, 1, 1, 0, SimulationTime.Zero);
            ties.Adjust(aldric, last, 0, 0, 30, SimulationTime.Zero);

            ties.Decay(aldric, SimulationTime.FromDays(5));

            var remaining = ties.Ties(aldric).ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(remaining.Length, Is.EqualTo(2));
                Assert.That(remaining[0].Toward, Is.EqualTo(first));
                Assert.That(remaining[1].Toward, Is.EqualTo(last));
                Assert.That(remaining[0].Liking, Is.EqualTo(25));
            });
        }

        [Test]
        public void Time_does_not_run_backwards()
        {
            var ids = new IdAllocator();
            var ties = new SocialTies(Settings);
            var aldric = ids.Next(EntityKind.Person);
            var mira = ids.Next(EntityKind.Person);
            ties.Adjust(aldric, mira, 10, 0, 0, SimulationTime.FromDays(5));

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => ties.Decay(aldric, SimulationTime.FromDays(4)),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => ties.Adjust(aldric, mira, 1, 0, 0, SimulationTime.FromDays(4)),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(ties.TryGet(aldric, mira, out var tie) && tie.Liking == 10, Is.True, "unchanged");
            });
        }

        [Test]
        public void Decay_for_someone_with_no_ties_is_a_no_op()
        {
            var ids = new IdAllocator();
            var ties = new SocialTies(Settings);

            Assert.That(() => ties.Decay(ids.Next(EntityKind.Person), SimulationTime.FromDays(9)), Throws.Nothing);
        }

        [Test]
        public void Refused_inputs()
        {
            var ids = new IdAllocator();
            var ties = new SocialTies(Settings);
            var aldric = ids.Next(EntityKind.Person);
            var settlement = ids.Next(EntityKind.Settlement);

            Assert.Multiple(() =>
            {
                Assert.That(() => ties.Adjust(EntityId.None, aldric, 1, 0, 0, SimulationTime.Zero), Throws.ArgumentException);
                Assert.That(() => ties.Adjust(aldric, EntityId.None, 1, 0, 0, SimulationTime.Zero), Throws.ArgumentException);
                Assert.That(() => ties.Adjust(settlement, aldric, 1, 0, 0, SimulationTime.Zero), Throws.ArgumentException);
                Assert.That(() => ties.Adjust(aldric, settlement, 1, 0, 0, SimulationTime.Zero), Throws.ArgumentException);
                Assert.That(() => ties.Adjust(aldric, aldric, 1, 0, 0, SimulationTime.Zero), Throws.ArgumentException, "self");
                Assert.That(() => ties.Decay(settlement, SimulationTime.Zero), Throws.ArgumentException);
                Assert.That(() => ties.Ties(EntityId.None), Throws.ArgumentException);
                Assert.That(() => ties.TryGet(aldric, settlement, out _), Throws.ArgumentException);
                Assert.That(() => ties.TryGet(aldric, aldric, out _), Throws.ArgumentException, "nobody has cause to ask about themselves");
                Assert.That(ties.Ties(aldric).Length, Is.Zero, "nothing was recorded");
            });
        }

        [Test]
        public void Settings_are_validated()
        {
            Assert.Multiple(() =>
            {
                Assert.That(() => new SocialTieSettings(0, 1L), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => new SocialTieSettings(1, 0L), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => new SocialTieSettings(1, -1L), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(new SocialTies(Settings).Settings.MaxTiesPerPerson, Is.EqualTo(3));
            });
        }

        [Test]
        public void A_person_with_no_ties_has_no_entry()
        {
            // Entries are keyed by durable id and so would outlive the
            // person; a 200-year run must not keep one for everyone who ever
            // had a tie. Every way a person's last tie can go is covered.
            var ids = new IdAllocator();
            var ties = new SocialTies(Settings);
            var aldric = ids.Next(EntityKind.Person);
            var mira = ids.Next(EntityKind.Person);

            ties.Adjust(aldric, mira, 0, 0, 0, SimulationTime.Zero);
            Assert.That(ties.PersonCount, Is.Zero, "a nothing-tie toward a stranger");

            ties.Adjust(aldric, mira, 3, 0, 0, SimulationTime.Zero);
            Assert.That(ties.PersonCount, Is.EqualTo(1));
            ties.Adjust(aldric, mira, -3, 0, 0, SimulationTime.Zero);
            Assert.That(ties.PersonCount, Is.Zero, "adjusted to nothing");

            ties.Adjust(aldric, mira, 1, 0, 0, SimulationTime.Zero);
            ties.Decay(aldric, SimulationTime.FromDays(5));
            Assert.That(ties.PersonCount, Is.Zero, "decayed to nothing");

            ties.Adjust(aldric, mira, 1, 0, 0, SimulationTime.FromDays(5));
            Assert.That(ties.TryGet(aldric, mira, out _), Is.True, "and a tie can form again afterwards");
        }

        [Test]
        public void Default_settings_are_refused_by_the_store()
        {
            // The settings constructor validates every field, so default is
            // the one invalid instance that can exist - a zero cap and a
            // zero decay interval.
            Assert.That(() => new SocialTies(default), Throws.ArgumentException);
        }

        [Test]
        public void A_tie_adjusted_to_nothing_is_removed()
        {
            var ids = new IdAllocator();
            var ties = new SocialTies(Settings);
            var aldric = ids.Next(EntityKind.Person);
            var mira = ids.Next(EntityKind.Person);
            ties.Adjust(aldric, mira, 5, 2, 0, SimulationTime.Zero);

            ties.Adjust(aldric, mira, -5, -2, 0, SimulationTime.Zero);

            Assert.Multiple(() =>
            {
                Assert.That(ties.TryGet(aldric, mira, out _), Is.False);
                Assert.That(ties.Ties(aldric).Length, Is.Zero);
            });
        }

        [Test]
        public void A_new_tie_that_would_be_nothing_is_not_added_and_evicts_nobody()
        {
            var ids = new IdAllocator();
            var ties = new SocialTies(Settings);
            var aldric = ids.Next(EntityKind.Person);
            var a = ids.Next(EntityKind.Person);
            var b = ids.Next(EntityKind.Person);
            var c = ids.Next(EntityKind.Person);
            var nobody = ids.Next(EntityKind.Person);
            ties.Adjust(aldric, a, 1, 0, 0, SimulationTime.Zero);
            ties.Adjust(aldric, b, 1, 0, 0, SimulationTime.Zero);
            ties.Adjust(aldric, c, 1, 0, 0, SimulationTime.Zero);

            ties.Adjust(aldric, nobody, 0, 0, 0, SimulationTime.Zero);
            ties.Adjust(aldric, nobody, 0, -5, -5, SimulationTime.Zero);

            Assert.Multiple(() =>
            {
                Assert.That(ties.TryGet(aldric, nobody, out _), Is.False);
                Assert.That(ties.TryGet(aldric, a, out _), Is.True, "the weakest real tie was not evicted");
                Assert.That(ties.Ties(aldric).Length, Is.EqualTo(3));
            });
        }

        [Test]
        public void The_same_operations_produce_the_same_state()
        {
            // Eviction and removal order must be a function of the operations
            // alone. Two stores, one script, identical spans.
            var ids = new IdAllocator();
            var people = new EntityId[8];

            for (var i = 0; i < people.Length; i++)
            {
                people[i] = ids.Next(EntityKind.Person);
            }

            var left = Run();
            var right = Run();

            Assert.That(right.Ties(people[0]).ToArray(), Is.EqualTo(left.Ties(people[0]).ToArray()));

            SocialTies Run()
            {
                var ties = new SocialTies(Settings);

                for (var round = 0; round < 20; round++)
                {
                    for (var i = 1; i < people.Length; i++)
                    {
                        ties.Adjust(people[0], people[i], ((round * 7) + i) % 5 - 2, i % 3, round % 4, SimulationTime.FromDays(round));
                    }

                    ties.Decay(people[0], SimulationTime.FromDays(round).Plus(SimulationTime.TicksPerHour * 30));
                }

                return ties;
            }
        }
    }
}
