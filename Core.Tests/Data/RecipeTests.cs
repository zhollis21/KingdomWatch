using System;
using System.Collections.Generic;
using KingdomWatch.Core.Data;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Data
{
    [TestFixture]
    public sealed class RecipeTests
    {
        private static readonly ResourceQuantity[] NoInputs = Array.Empty<ResourceQuantity>();

        private static ResourceQuantity Food(int quantity) => new ResourceQuantity(ResourceKind.Food, quantity);

        private static ResourceQuantity Wood(int quantity) => new ResourceQuantity(ResourceKind.Wood, quantity);

        private static ResourceQuantity Stone(int quantity) => new ResourceQuantity(ResourceKind.Stone, quantity);

        [Test]
        public void A_crafting_recipe_keeps_its_lines_in_order()
        {
            var recipe = new Recipe("Test", new[] { Stone(2), Wood(1) }, new[] { Food(5) }, 60L);

            Assert.Multiple(() =>
            {
                Assert.That(recipe.Name, Is.EqualTo("Test"));
                Assert.That(recipe.ToString(), Is.EqualTo("Test"));
                Assert.That(recipe.Inputs, Is.EqualTo(new[] { Stone(2), Wood(1) }));
                Assert.That(recipe.Outputs, Is.EqualTo(new[] { Food(5) }));
                Assert.That(recipe.Duration, Is.EqualTo(60L));
                Assert.That(recipe.IsGathering, Is.False);
            });
        }

        [Test]
        public void No_inputs_means_gathering()
        {
            var recipe = new Recipe("Forage", NoInputs, new[] { Food(1) }, 1L);

            Assert.Multiple(() =>
            {
                Assert.That(recipe.IsGathering, Is.True);
                Assert.That(recipe.Inputs, Is.Empty);
            });
        }

        [Test]
        public void Lines_are_copied_so_the_caller_cannot_change_them_later()
        {
            var inputs = new List<ResourceQuantity> { Stone(1) };
            var outputs = new List<ResourceQuantity> { Food(1) };
            var recipe = new Recipe("Test", inputs, outputs, 1L);

            inputs.Add(Wood(1));
            outputs.Clear();

            Assert.Multiple(() =>
            {
                Assert.That(recipe.Inputs, Has.Count.EqualTo(1));
                Assert.That(recipe.Outputs, Has.Count.EqualTo(1));
            });
        }

        [Test]
        public void Lines_cannot_be_mutated_through_a_downcast()
        {
            var recipe = new Recipe("Test", new[] { Stone(1) }, new[] { Food(1) }, 1L);

            Assert.Multiple(() =>
            {
                Assert.That(recipe.Inputs, Is.Not.InstanceOf<List<ResourceQuantity>>());
                Assert.That(recipe.Inputs, Is.Not.InstanceOf<ResourceQuantity[]>());
                Assert.That(
                    () => ((IList<ResourceQuantity>)recipe.Inputs).Add(Wood(1)),
                    Throws.TypeOf<NotSupportedException>());
                Assert.That(
                    () => ((IList<ResourceQuantity>)recipe.Outputs)[0] = Wood(1),
                    Throws.TypeOf<NotSupportedException>());
            });
        }

        [Test]
        public void A_name_is_required()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    () => new Recipe(null!, NoInputs, new[] { Food(1) }, 1L),
                    Throws.ArgumentException);
                Assert.That(
                    () => new Recipe("", NoInputs, new[] { Food(1) }, 1L),
                    Throws.ArgumentException);
                Assert.That(
                    () => new Recipe("   ", NoInputs, new[] { Food(1) }, 1L),
                    Throws.ArgumentException);
            });
        }

        [Test]
        public void Null_lists_are_rejected()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    () => new Recipe("Test", null!, new[] { Food(1) }, 1L),
                    Throws.ArgumentNullException);
                Assert.That(
                    () => new Recipe("Test", NoInputs, null!, 1L),
                    Throws.ArgumentNullException);
            });
        }

        [Test]
        public void A_recipe_must_make_something()
        {
            Assert.That(
                () => new Recipe("Nothing", new[] { Stone(1) }, NoInputs, 1L),
                Throws.ArgumentException);
        }

        [Test]
        public void Duration_must_be_positive()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    () => new Recipe("Test", NoInputs, new[] { Food(1) }, 0L),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => new Recipe("Test", NoInputs, new[] { Food(1) }, -1L),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => new Recipe("Test", NoInputs, new[] { Food(1) }, long.MinValue),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void The_longest_duration_the_type_holds_is_valid()
        {
            var recipe = new Recipe("Slow", NoInputs, new[] { Food(1) }, long.MaxValue);

            Assert.That(recipe.Duration, Is.EqualTo(long.MaxValue));
        }

        [Test]
        public void A_kind_may_appear_once_per_side()
        {
            // Two lines for the same kind is either a typo or a merged line in
            // disguise. Refusing beats guessing, and it lets the ledger treat
            // each line independently.
            Assert.Multiple(() =>
            {
                Assert.That(
                    () => new Recipe("Test", new[] { Stone(1), Stone(2) }, new[] { Food(1) }, 1L),
                    Throws.ArgumentException.With.Message.Contains("Stone"));
                Assert.That(
                    () => new Recipe("Test", NoInputs, new[] { Food(1), Food(1) }, 1L),
                    Throws.ArgumentException.With.Message.Contains("Food"));
                // The same kind on both sides is fine: a recipe that refines
                // food into more food is legitimate.
                Assert.That(
                    () => new Recipe("Test", new[] { Food(1) }, new[] { Food(2) }, 1L),
                    Throws.Nothing);
            });
        }

        [Test]
        public void A_defaulted_line_is_rejected()
        {
            // default(ResourceQuantity) never ran the constructor, so it has
            // kind None and quantity zero, and it would otherwise index the
            // ledger's unused slot.
            Assert.Multiple(() =>
            {
                Assert.That(
                    () => new Recipe("Test", new[] { default(ResourceQuantity) }, new[] { Food(1) }, 1L),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => new Recipe("Test", NoInputs, new[] { default(ResourceQuantity) }, 1L),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }
    }
}
