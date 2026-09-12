using System;
using System.Collections.Generic;
using System.Linq;
using KingdomWatch.Core.Data;
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
    }
}
