using System;
using System.Collections.Generic;
using System.Linq;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Needs;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Data
{
    /// <summary>
    /// Tier zero must be reachable from nothing: every recipe here runs on an
    /// empty ledger. The quantities are placeholders and are not asserted.
    /// </summary>
    [TestFixture]
    public sealed class PrimitiveTierTests
    {
        [Test]
        public void Every_recipe_is_gathering_and_runs_on_an_empty_ledger()
        {
            Assert.Multiple(() =>
            {
                foreach (var recipe in PrimitiveTier.Recipes)
                {
                    var ledger = new ResourceLedger();

                    Assert.That(recipe.IsGathering, Is.True, recipe.Name);
                    Assert.That(() => ledger.BeginRecipe(recipe), Throws.Nothing, recipe.Name);
                    Assert.That(() => ledger.CompleteRecipe(recipe), Throws.Nothing, recipe.Name);
                    Assert.That(ledger.AuditBalances(), Is.True, recipe.Name);
                }
            });
        }

        [Test]
        public void Each_recipe_gathers_exactly_its_resource()
        {
            Assert.Multiple(() =>
            {
                Assert.That(PrimitiveTier.Forage.Outputs.Select(o => o.Kind), Is.EqualTo(new[] { ResourceKind.Food }));
                Assert.That(PrimitiveTier.GatherWood.Outputs.Select(o => o.Kind), Is.EqualTo(new[] { ResourceKind.Wood }));
                Assert.That(PrimitiveTier.GatherStone.Outputs.Select(o => o.Kind), Is.EqualTo(new[] { ResourceKind.Stone }));
            });
        }

        [Test]
        public void The_list_holds_every_recipe_once_in_a_fixed_order()
        {
            // Job assignment iterates this list, so its order is part of the
            // determinism contract and it must not be mutable.
            Assert.Multiple(() =>
            {
                Assert.That(
                    PrimitiveTier.Recipes,
                    Is.EqualTo(new[] { PrimitiveTier.Forage, PrimitiveTier.GatherWood, PrimitiveTier.GatherStone }));
                Assert.That(PrimitiveTier.Recipes.Select(r => r.Name), Is.Unique);
                Assert.That(PrimitiveTier.Recipes, Is.Not.InstanceOf<Recipe[]>());
                Assert.That(
                    () => ((IList<Recipe>)PrimitiveTier.Recipes).Clear(),
                    Throws.TypeOf<NotSupportedException>());
            });
        }

        [Test]
        public void Foraging_follows_the_seasons_and_differs_only_in_what_it_yields()
        {
            // The numbers are placeholders; the shape is not. Spring is the
            // base, and a season's recipe changes the output alone, so a task's
            // length never depends on when it starts.
            Assert.Multiple(() =>
            {
                Assert.That(PrimitiveTier.ForageIn(Season.Spring), Is.SameAs(PrimitiveTier.Forage));
                Assert.That(PrimitiveTier.ForageIn(Season.Summer), Is.SameAs(PrimitiveTier.ForageSummer));
                Assert.That(PrimitiveTier.ForageIn(Season.Autumn), Is.SameAs(PrimitiveTier.ForageAutumn));
                Assert.That(PrimitiveTier.ForageIn(Season.Winter), Is.SameAs(PrimitiveTier.ForageWinter));

                foreach (Season season in Enum.GetValues(typeof(Season)))
                {
                    var recipe = PrimitiveTier.ForageIn(season);
                    Assert.That(recipe.IsGathering, Is.True, recipe.Name);
                    Assert.That(recipe.Duration, Is.EqualTo(PrimitiveTier.Forage.Duration), recipe.Name);
                    Assert.That(recipe.Outputs.Select(o => o.Kind), Is.EqualTo(new[] { ResourceKind.Food }), recipe.Name);
                }

                Assert.That(() => PrimitiveTier.ForageIn((Season)4), Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void Winter_foraging_does_not_feed_the_forager()
        {
            // What makes stores matter at all (#53): a task's winter yield is
            // under one ration, so however many trips fit in a day, winter is
            // lived on what autumn put by. Summer and autumn are the plenty.
            Assert.Multiple(() =>
            {
                Assert.That(PrimitiveTier.ForageWinter.Outputs[0].Quantity, Is.LessThan(Hunger.DailyRation));
                Assert.That(PrimitiveTier.ForageSummer.Outputs[0].Quantity, Is.GreaterThan(PrimitiveTier.Forage.Outputs[0].Quantity));
                Assert.That(PrimitiveTier.ForageAutumn.Outputs[0].Quantity, Is.GreaterThan(PrimitiveTier.Forage.Outputs[0].Quantity));
            });
        }
    }
}
