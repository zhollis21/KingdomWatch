using System;
using KingdomWatch.Core.Traversal;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Traversal
{
    [TestFixture]
    public sealed class TerrainRulesTests
    {
        private static readonly TerrainKind[] Kinds =
        {
            TerrainKind.Plains,
            TerrainKind.Forest,
            TerrainKind.Hills,
            TerrainKind.SmallRiver,
            TerrainKind.DeepWater,
        };

        [Test]
        public void Default_has_a_rule_for_every_defined_kind()
        {
            // The table is what makes "never hardcode land-only" hold: a kind
            // without a row would be a kind the pathfinder cannot answer for.
            foreach (TerrainKind kind in Enum.GetValues(typeof(TerrainKind)))
            {
                if (kind == TerrainKind.None)
                {
                    continue;
                }

                Assert.That(() => TerrainRules.Default[kind], Throws.Nothing, kind.ToString());
            }
        }

        [Test]
        public void Default_expresses_section_12s_water_rules()
        {
            Assert.Multiple(() =>
            {
                Assert.That(TerrainRules.Default.IsPassable(TerrainKind.SmallRiver, Transport.Foot), Is.False);
                Assert.That(TerrainRules.Default.IsPassable(TerrainKind.SmallRiver, Transport.Boat), Is.False);
                Assert.That(TerrainRules.Default.IsPassable(TerrainKind.DeepWater, Transport.Foot), Is.False);
                Assert.That(TerrainRules.Default.IsPassable(TerrainKind.DeepWater, Transport.Boat), Is.True);
                Assert.That(TerrainRules.Default.IsPassable(TerrainKind.Plains, Transport.Foot), Is.True);
                Assert.That(TerrainRules.Default.IsPassable(TerrainKind.Plains, Transport.Boat), Is.False);
            });
        }

        [Test]
        public void A_mover_with_several_transports_passes_where_any_is_admitted()
        {
            var amphibious = Transport.Foot | Transport.Boat;

            Assert.Multiple(() =>
            {
                Assert.That(TerrainRules.Default.IsPassable(TerrainKind.Plains, amphibious), Is.True);
                Assert.That(TerrainRules.Default.IsPassable(TerrainKind.DeepWater, amphibious), Is.True);
                Assert.That(TerrainRules.Default.IsPassable(TerrainKind.SmallRiver, amphibious), Is.False);
            });
        }

        [Test]
        public void Cheapest_cost_is_the_lowest_passable_cost()
        {
            var rules = new TerrainRules(
                (TerrainKind.Plains, new TerrainRule(7, Transport.Foot)),
                (TerrainKind.Forest, new TerrainRule(20, Transport.Foot)),
                (TerrainKind.Hills, new TerrainRule(30, Transport.Foot)),
                (TerrainKind.SmallRiver, TerrainRule.Impassable),
                (TerrainKind.DeepWater, new TerrainRule(3, Transport.Boat)));

            // Across all transports, not just Foot: the heuristic must stay
            // admissible for a boat too.
            Assert.That(rules.CheapestCost, Is.EqualTo(3));
        }

        [Test]
        public void Indexer_rejects_none_and_undefined_kinds()
        {
            Assert.Multiple(() =>
            {
                Assert.That(() => TerrainRules.Default[TerrainKind.None], Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => TerrainRules.Default[(TerrainKind)255], Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => TerrainRules.Default[(TerrainKind)6], Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => TerrainRules.Default.IsPassable(TerrainKind.None, Transport.Foot), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => TerrainRules.Default.IsPassable((TerrainKind)255, Transport.Foot), Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void A_table_missing_a_kind_is_refused()
        {
            Assert.That(
                () => new TerrainRules(
                    (TerrainKind.Plains, new TerrainRule(10, Transport.Foot)),
                    (TerrainKind.Forest, new TerrainRule(20, Transport.Foot)),
                    (TerrainKind.Hills, new TerrainRule(30, Transport.Foot)),
                    (TerrainKind.SmallRiver, TerrainRule.Impassable)),
                Throws.ArgumentException.With.Message.Contains("DeepWater"));
        }

        [Test]
        public void A_table_with_a_kind_twice_is_refused()
        {
            Assert.That(
                () => new TerrainRules(
                    (TerrainKind.Plains, new TerrainRule(10, Transport.Foot)),
                    (TerrainKind.Plains, new TerrainRule(11, Transport.Foot)),
                    (TerrainKind.Forest, new TerrainRule(20, Transport.Foot)),
                    (TerrainKind.Hills, new TerrainRule(30, Transport.Foot)),
                    (TerrainKind.SmallRiver, TerrainRule.Impassable),
                    (TerrainKind.DeepWater, new TerrainRule(10, Transport.Boat))),
                Throws.ArgumentException.With.Message.Contains("two rules"));
        }

        [Test]
        public void A_table_with_an_undefined_kind_is_refused()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    () => new TerrainRules(((TerrainKind)255, new TerrainRule(10, Transport.Foot))),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => new TerrainRules((TerrainKind.None, new TerrainRule(10, Transport.Foot))),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void A_table_where_nothing_is_passable_is_refused()
        {
            var rows = new (TerrainKind, TerrainRule)[Kinds.Length];

            for (var i = 0; i < Kinds.Length; i++)
            {
                rows[i] = (Kinds[i], TerrainRule.Impassable);
            }

            Assert.That(() => new TerrainRules(rows), Throws.ArgumentException);
        }

        [Test]
        public void A_null_table_is_refused()
        {
            Assert.That(() => new TerrainRules(null!), Throws.ArgumentNullException);
        }

        [Test]
        public void A_rule_needs_a_positive_cost_and_a_defined_transport()
        {
            Assert.Multiple(() =>
            {
                Assert.That(() => new TerrainRule(0, Transport.Foot), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => new TerrainRule(-1, Transport.Foot), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => new TerrainRule(TerrainRule.MaxCost, Transport.Foot), Throws.Nothing);
                Assert.That(() => new TerrainRule(TerrainRule.MaxCost + 1, Transport.Foot), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => new TerrainRule(int.MaxValue, Transport.Foot), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => new TerrainRule(int.MinValue, Transport.Foot), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => new TerrainRule(10, Transport.None), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => new TerrainRule(10, (Transport)4), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => new TerrainRule(10, Transport.Foot | (Transport)8), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => new TerrainRule(10, Transport.Foot | Transport.Boat), Throws.Nothing);
            });
        }

        [Test]
        public void Admits_and_is_passable_refuse_a_mover_with_no_defined_transport()
        {
            // A bare mask test would answer None with "not admitted" and an
            // undefined bit with whatever the defined bits say; both are
            // caller bugs, and every public entry taking a mover refuses them.
            var rule = new TerrainRule(10, Transport.Foot);

            Assert.Multiple(() =>
            {
                Assert.That(() => rule.Admits(Transport.None), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => rule.Admits((Transport)4), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => rule.Admits(Transport.Foot | (Transport)4), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => rule.Admits((Transport)(-1)), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => TerrainRule.Impassable.Admits(Transport.None), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => TerrainRules.Default.IsPassable(TerrainKind.Plains, Transport.None), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => TerrainRules.Default.IsPassable(TerrainKind.Plains, Transport.Foot | (Transport)4), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => TerrainRules.Default.IsPassable(TerrainKind.Plains, (Transport)(-1)), Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void Impassable_is_the_default_struct_and_admits_nobody()
        {
            Assert.Multiple(() =>
            {
                Assert.That(TerrainRule.Impassable, Is.EqualTo(default(TerrainRule)));
                Assert.That(TerrainRule.Impassable.IsImpassable, Is.True);
                Assert.That(TerrainRule.Impassable.Cost, Is.Zero);
                Assert.That(TerrainRule.Impassable.Admits(Transport.Foot | Transport.Boat), Is.False);
                Assert.That(new TerrainRule(10, Transport.Foot).IsImpassable, Is.False);
            });
        }

        [Test]
        public void Rules_compare_by_value()
        {
            var a = new TerrainRule(10, Transport.Foot);
            var b = new TerrainRule(10, Transport.Foot);
            var c = new TerrainRule(10, Transport.Boat);

            Assert.Multiple(() =>
            {
                Assert.That(a, Is.EqualTo(b));
                Assert.That(a == b, Is.True);
                Assert.That(a != c, Is.True);
                Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
                Assert.That(a.Equals((object)b), Is.True);
                Assert.That(a.Equals("not a rule"), Is.False);
                Assert.That(a.ToString(), Is.EqualTo("10 by Foot"));
                Assert.That(TerrainRule.Impassable.ToString(), Is.EqualTo("impassable"));
            });
        }
    }
}
