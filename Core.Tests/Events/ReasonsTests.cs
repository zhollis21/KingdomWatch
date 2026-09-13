using System;
using KingdomWatch.Core.Events;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Events
{
    [TestFixture]
    public sealed class ReasonsTests
    {
        [Test]
        public void None_is_empty_and_equal_to_default()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Reasons.None.Count, Is.Zero);
                Assert.That(Reasons.None, Is.EqualTo(default(Reasons)));
                Assert.That(Reasons.None.Contains(ReasonCode.FoodShortage), Is.False);
                Assert.That(Reasons.None.ToString(), Is.EqualTo("[]"));
            });
        }

        [Test]
        public void One_to_four_reasons_round_trip_in_the_order_given()
        {
            var one = new Reasons(ReasonCode.FoodShortage);
            var two = new Reasons(ReasonCode.FoodShortage, ReasonCode.SpouseDied);
            var three = new Reasons(ReasonCode.FoodShortage, ReasonCode.SpouseDied, ReasonCode.KinLiveThere);
            var four = new Reasons(
                ReasonCode.FoodShortage, ReasonCode.SpouseDied, ReasonCode.KinLiveThere, ReasonCode.AcceptsMigrants);

            Assert.Multiple(() =>
            {
                Assert.That(one.Count, Is.EqualTo(1));
                Assert.That(two.Count, Is.EqualTo(2));
                Assert.That(three.Count, Is.EqualTo(3));
                Assert.That(four.Count, Is.EqualTo(4));

                Assert.That(four[0], Is.EqualTo(ReasonCode.FoodShortage));
                Assert.That(four[1], Is.EqualTo(ReasonCode.SpouseDied));
                Assert.That(four[2], Is.EqualTo(ReasonCode.KinLiveThere));
                Assert.That(four[3], Is.EqualTo(ReasonCode.AcceptsMigrants));
            });
        }

        [Test]
        public void The_cap_is_four()
        {
            Assert.That(Reasons.MaxCount, Is.EqualTo(4));
        }

        [Test]
        public void The_indexer_stops_at_the_count_not_at_the_capacity()
        {
            var two = new Reasons(ReasonCode.FoodShortage, ReasonCode.SpouseDied);

            Assert.Multiple(() =>
            {
                Assert.That(() => two[2], Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => two[-1], Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => Reasons.None[0], Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void None_is_not_a_reason_in_any_position()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    () => new Reasons(ReasonCode.None),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => new Reasons(ReasonCode.FoodShortage, ReasonCode.None),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => new Reasons(ReasonCode.FoodShortage, ReasonCode.SpouseDied, ReasonCode.None),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => new Reasons(
                        ReasonCode.FoodShortage, ReasonCode.SpouseDied, ReasonCode.KinLiveThere, ReasonCode.None),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void An_undefined_code_is_rejected_in_any_position()
        {
            var bogus = (ReasonCode)999;
            var negative = (ReasonCode)(-1);

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => new Reasons(bogus),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => new Reasons(negative),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => new Reasons(ReasonCode.FoodShortage, bogus),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => new Reasons(ReasonCode.FoodShortage, ReasonCode.SpouseDied, bogus),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => new Reasons(
                        ReasonCode.FoodShortage, ReasonCode.SpouseDied, ReasonCode.KinLiveThere, bogus),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void A_reason_may_be_listed_once()
        {
            // Listing a reason twice is a bug at the decision site, not a
            // stronger reason. Every pair of positions is checked, since the
            // duplicate check is written out by hand per position.
            Assert.Multiple(() =>
            {
                Assert.That(
                    () => new Reasons(ReasonCode.FoodShortage, ReasonCode.FoodShortage),
                    Throws.TypeOf<ArgumentException>());
                Assert.That(
                    () => new Reasons(ReasonCode.FoodShortage, ReasonCode.SpouseDied, ReasonCode.FoodShortage),
                    Throws.TypeOf<ArgumentException>());
                Assert.That(
                    () => new Reasons(ReasonCode.FoodShortage, ReasonCode.SpouseDied, ReasonCode.SpouseDied),
                    Throws.TypeOf<ArgumentException>());
                Assert.That(
                    () => new Reasons(
                        ReasonCode.FoodShortage, ReasonCode.SpouseDied, ReasonCode.KinLiveThere, ReasonCode.FoodShortage),
                    Throws.TypeOf<ArgumentException>());
                Assert.That(
                    () => new Reasons(
                        ReasonCode.FoodShortage, ReasonCode.SpouseDied, ReasonCode.KinLiveThere, ReasonCode.SpouseDied),
                    Throws.TypeOf<ArgumentException>());
                Assert.That(
                    () => new Reasons(
                        ReasonCode.FoodShortage, ReasonCode.SpouseDied, ReasonCode.KinLiveThere, ReasonCode.KinLiveThere),
                    Throws.TypeOf<ArgumentException>());
            });
        }

        [Test]
        public void Contains_sees_every_recorded_position_and_nothing_else()
        {
            var four = new Reasons(
                ReasonCode.FoodShortage, ReasonCode.SpouseDied, ReasonCode.KinLiveThere, ReasonCode.AcceptsMigrants);
            var one = new Reasons(ReasonCode.TradersAttacked);

            Assert.Multiple(() =>
            {
                Assert.That(four.Contains(ReasonCode.FoodShortage), Is.True);
                Assert.That(four.Contains(ReasonCode.SpouseDied), Is.True);
                Assert.That(four.Contains(ReasonCode.KinLiveThere), Is.True);
                Assert.That(four.Contains(ReasonCode.AcceptsMigrants), Is.True);
                Assert.That(four.Contains(ReasonCode.TradersAttacked), Is.False);
                Assert.That(four.Contains(ReasonCode.None), Is.False);

                // The unused slots hold None; asking for None must not find them.
                Assert.That(one.Contains(ReasonCode.None), Is.False);

                // None is a defined member and the honest answer is "no"; an
                // undefined value is a caller bug and is refused.
                Assert.That(() => four.Contains((ReasonCode)999), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => four.Contains((ReasonCode)(-1)), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => Reasons.None.Contains((ReasonCode)999), Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void Equality_is_by_count_and_by_order()
        {
            var a = new Reasons(ReasonCode.FoodShortage, ReasonCode.SpouseDied);
            var same = new Reasons(ReasonCode.FoodShortage, ReasonCode.SpouseDied);
            var reversed = new Reasons(ReasonCode.SpouseDied, ReasonCode.FoodShortage);
            var shorter = new Reasons(ReasonCode.FoodShortage);

            Assert.Multiple(() =>
            {
                Assert.That(a, Is.EqualTo(same));
                Assert.That(a == same, Is.True);
                Assert.That(a.GetHashCode(), Is.EqualTo(same.GetHashCode()));
                Assert.That(a, Is.Not.EqualTo(reversed));
                Assert.That(a != reversed, Is.True);
                Assert.That(a, Is.Not.EqualTo(shorter));
                Assert.That(a.Equals((object)same), Is.True);
                Assert.That(a.Equals("not reasons"), Is.False);
            });
        }

        [Test]
        public void ToString_lists_the_reasons_in_rank_order()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    new Reasons(ReasonCode.FoodShortage).ToString(),
                    Is.EqualTo("[FoodShortage]"));
                Assert.That(
                    new Reasons(ReasonCode.FoodShortage, ReasonCode.SpouseDied).ToString(),
                    Is.EqualTo("[FoodShortage, SpouseDied]"));
                Assert.That(
                    new Reasons(ReasonCode.FoodShortage, ReasonCode.SpouseDied, ReasonCode.KinLiveThere).ToString(),
                    Is.EqualTo("[FoodShortage, SpouseDied, KinLiveThere]"));
                Assert.That(
                    new Reasons(
                        ReasonCode.FoodShortage,
                        ReasonCode.SpouseDied,
                        ReasonCode.KinLiveThere,
                        ReasonCode.AcceptsMigrants).ToString(),
                    Is.EqualTo("[FoodShortage, SpouseDied, KinLiveThere, AcceptsMigrants]"));
            });
        }
    }
}
