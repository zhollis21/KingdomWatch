using System;
using System.Collections.Generic;
using KingdomWatch.Core.Data;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Data
{
    /// <summary>
    /// The one authoritative account. The properties that matter: every
    /// operation moves quantity between named buckets or across the ledger's
    /// boundary and nowhere else, nothing goes negative, and the conservation
    /// audit balances after any sequence of operations.
    /// </summary>
    [TestFixture]
    public sealed class ResourceLedgerTests
    {
        private static readonly ResourceQuantity[] NoInputs = Array.Empty<ResourceQuantity>();

        // Stone and wood into food. Not a real recipe - M1 ships gathering only
        // - but it is the shape that exercises the in-process bucket and the
        // produced-versus-gathered split, which is what needs proving here.
        private static readonly Recipe Cook = new Recipe(
            "Cook",
            new[] { new ResourceQuantity(ResourceKind.Stone, 2), new ResourceQuantity(ResourceKind.Wood, 1) },
            new[] { new ResourceQuantity(ResourceKind.Food, 5) },
            10L);

        private static readonly Recipe Forage = new Recipe(
            "Forage",
            NoInputs,
            new[] { new ResourceQuantity(ResourceKind.Food, 3) },
            10L);

        // Every (kind, quantity) operation, named, so the guard tests can run
        // over all of them rather than trusting each one was remembered.
        private static IEnumerable<TestCaseData> Operations()
        {
            yield return Case("Open", (l, k, q) => l.Open(k, q));
            yield return Case("Gather", (l, k, q) => l.Gather(k, q));
            yield return Case("Import", (l, k, q) => l.Import(k, q));
            yield return Case("Consume", (l, k, q) => l.Consume(k, q));
            yield return Case("Export", (l, k, q) => l.Export(k, q));
            yield return Case("Destroy", (l, k, q) => l.Destroy(k, q));
            yield return Case("Embody", (l, k, q) => l.Embody(k, q));
            yield return Case("Reserve", (l, k, q) => l.Reserve(k, q));
            yield return Case("Release", (l, k, q) => l.Release(k, q));
            yield return Case("PickUp", (l, k, q) => l.PickUp(k, q));
            yield return Case("SetDown", (l, k, q) => l.SetDown(k, q));
            yield return Case("TransferTo", (l, k, q) => l.TransferTo(new ResourceLedger(), k, q));
        }

        // The operations that remove from the available pool and record a flow
        // for it, paired with the flow they record.
        private static IEnumerable<TestCaseData> Sinks()
        {
            yield return Case("Consume", (l, k, q) => l.Consume(k, q), (Func<ResourceFlows, long>)(f => f.Consumed));
            yield return Case("Export", (l, k, q) => l.Export(k, q), (Func<ResourceFlows, long>)(f => f.Exported));
            yield return Case("Destroy", (l, k, q) => l.Destroy(k, q), (Func<ResourceFlows, long>)(f => f.Destroyed));
            yield return Case("Embody", (l, k, q) => l.Embody(k, q), (Func<ResourceFlows, long>)(f => f.Embodied));
        }

        private static IEnumerable<TestCaseData> Sources()
        {
            yield return Case("Open", (l, k, q) => l.Open(k, q), (Func<ResourceFlows, long>)(f => f.Opening));
            yield return Case("Gather", (l, k, q) => l.Gather(k, q), (Func<ResourceFlows, long>)(f => f.Gathered));
            yield return Case("Import", (l, k, q) => l.Import(k, q), (Func<ResourceFlows, long>)(f => f.Imported));
        }

        private static TestCaseData Case(string name, Action<ResourceLedger, ResourceKind, int> op, params object[] extra)
        {
            var args = new List<object> { op };
            args.AddRange(extra);
            return new TestCaseData(args.ToArray()).SetName("{m}(" + name + ")");
        }

        private static ResourceLedger WithFood(int available)
        {
            var ledger = new ResourceLedger();
            ledger.Open(ResourceKind.Food, available);
            return ledger;
        }

        [Test]
        public void A_new_ledger_holds_nothing_and_balances()
        {
            var ledger = new ResourceLedger();

            Assert.Multiple(() =>
            {
                foreach (var kind in new[] { ResourceKind.Food, ResourceKind.Wood, ResourceKind.Stone })
                {
                    Assert.That(ledger.Stock(kind), Is.Zero, kind.ToString());
                    Assert.That(ledger.Flows(kind).TotalIn, Is.Zero, kind.ToString());
                    Assert.That(ledger.Flows(kind).TotalOut, Is.Zero, kind.ToString());
                }

                Assert.That(ledger.AuditBalances(), Is.True);
            });
        }

        [TestCaseSource(nameof(Operations))]
        public void None_and_undefined_kinds_are_rejected(Action<ResourceLedger, ResourceKind, int> op)
        {
            var ledger = WithFood(10);

            Assert.Multiple(() =>
            {
                Assert.That(() => op(ledger, ResourceKind.None, 1), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => op(ledger, (ResourceKind)999, 1), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => op(ledger, (ResourceKind)(-1), 1), Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [TestCaseSource(nameof(Operations))]
        public void Zero_and_negative_quantities_are_rejected(Action<ResourceLedger, ResourceKind, int> op)
        {
            var ledger = WithFood(10);

            Assert.Multiple(() =>
            {
                Assert.That(() => op(ledger, ResourceKind.Food, 0), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => op(ledger, ResourceKind.Food, -1), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => op(ledger, ResourceKind.Food, int.MinValue), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(ledger.Available(ResourceKind.Food), Is.EqualTo(10), "nothing moved");
            });
        }

        [Test]
        public void Accessors_reject_none_and_undefined_kinds()
        {
            var ledger = new ResourceLedger();

            Assert.Multiple(() =>
            {
                Assert.That(() => ledger.Available(ResourceKind.None), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => ledger.Reserved((ResourceKind)999), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => ledger.Carried((ResourceKind)(-1)), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => ledger.InProcess(ResourceKind.None), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => ledger.Stock(ResourceKind.None), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => ledger.Flows(ResourceKind.None), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => ledger.AuditBalances(ResourceKind.None), Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [TestCaseSource(nameof(Sources))]
        public void A_source_adds_to_available_and_records_its_flow(
            Action<ResourceLedger, ResourceKind, int> op, Func<ResourceFlows, long> flow)
        {
            var ledger = new ResourceLedger();

            op(ledger, ResourceKind.Wood, 4);
            op(ledger, ResourceKind.Wood, 3);

            Assert.Multiple(() =>
            {
                Assert.That(ledger.Available(ResourceKind.Wood), Is.EqualTo(7));
                Assert.That(ledger.Stock(ResourceKind.Wood), Is.EqualTo(7));
                Assert.That(flow(ledger.Flows(ResourceKind.Wood)), Is.EqualTo(7L));
                Assert.That(ledger.Flows(ResourceKind.Wood).TotalIn, Is.EqualTo(7L));
                Assert.That(ledger.Flows(ResourceKind.Wood).TotalOut, Is.Zero);
                Assert.That(ledger.Available(ResourceKind.Food), Is.Zero, "other kinds untouched");
                Assert.That(ledger.AuditBalances(), Is.True);
            });
        }

        [TestCaseSource(nameof(Sinks))]
        public void A_sink_takes_from_available_and_records_its_flow(
            Action<ResourceLedger, ResourceKind, int> op, Func<ResourceFlows, long> flow)
        {
            var ledger = WithFood(10);

            op(ledger, ResourceKind.Food, 4);

            Assert.Multiple(() =>
            {
                Assert.That(ledger.Available(ResourceKind.Food), Is.EqualTo(6));
                Assert.That(ledger.Stock(ResourceKind.Food), Is.EqualTo(6));
                Assert.That(flow(ledger.Flows(ResourceKind.Food)), Is.EqualTo(4L));
                Assert.That(ledger.Flows(ResourceKind.Food).TotalOut, Is.EqualTo(4L));
                Assert.That(ledger.AuditBalances(), Is.True);
            });
        }

        [TestCaseSource(nameof(Sinks))]
        public void A_sink_short_of_stock_throws_and_moves_nothing(
            Action<ResourceLedger, ResourceKind, int> op, Func<ResourceFlows, long> flow)
        {
            // Reserved stock is not available. Ten in total, six reserved,
            // four free: taking five must fail even though ten exist.
            var ledger = WithFood(10);
            ledger.Reserve(ResourceKind.Food, 6);

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => op(ledger, ResourceKind.Food, 5),
                    Throws.InvalidOperationException.With.Message.Contains("only 4 available"));
                Assert.That(ledger.Available(ResourceKind.Food), Is.EqualTo(4));
                Assert.That(ledger.Reserved(ResourceKind.Food), Is.EqualTo(6));
                Assert.That(flow(ledger.Flows(ResourceKind.Food)), Is.Zero);
                Assert.That(ledger.AuditBalances(), Is.True);
            });
        }

        [Test]
        public void Reserving_and_releasing_moves_between_buckets_without_a_flow()
        {
            var ledger = WithFood(10);

            ledger.Reserve(ResourceKind.Food, 7);

            Assert.Multiple(() =>
            {
                Assert.That(ledger.Available(ResourceKind.Food), Is.EqualTo(3));
                Assert.That(ledger.Reserved(ResourceKind.Food), Is.EqualTo(7));
                Assert.That(ledger.Stock(ResourceKind.Food), Is.EqualTo(10));
            });

            ledger.Release(ResourceKind.Food, 2);

            Assert.Multiple(() =>
            {
                Assert.That(ledger.Available(ResourceKind.Food), Is.EqualTo(5));
                Assert.That(ledger.Reserved(ResourceKind.Food), Is.EqualTo(5));
                Assert.That(ledger.Stock(ResourceKind.Food), Is.EqualTo(10));
                Assert.That(ledger.Flows(ResourceKind.Food).TotalOut, Is.Zero);
                Assert.That(ledger.AuditBalances(), Is.True);
            });
        }

        [Test]
        public void Picking_up_and_setting_down_moves_between_buckets_without_a_flow()
        {
            var ledger = WithFood(10);

            ledger.PickUp(ResourceKind.Food, 4);

            Assert.Multiple(() =>
            {
                Assert.That(ledger.Available(ResourceKind.Food), Is.EqualTo(6));
                Assert.That(ledger.Carried(ResourceKind.Food), Is.EqualTo(4));
                Assert.That(ledger.Stock(ResourceKind.Food), Is.EqualTo(10));
            });

            ledger.SetDown(ResourceKind.Food, 4);

            Assert.Multiple(() =>
            {
                Assert.That(ledger.Available(ResourceKind.Food), Is.EqualTo(10));
                Assert.That(ledger.Carried(ResourceKind.Food), Is.Zero);
                Assert.That(ledger.Flows(ResourceKind.Food).TotalOut, Is.Zero);
                Assert.That(ledger.AuditBalances(), Is.True);
            });
        }

        [Test]
        public void A_bucket_cannot_be_drawn_below_zero()
        {
            // Release and SetDown draw on their own buckets, not on available:
            // releasing more than was reserved would mint stock.
            var ledger = WithFood(10);
            ledger.Reserve(ResourceKind.Food, 2);
            ledger.PickUp(ResourceKind.Food, 3);

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => ledger.Release(ResourceKind.Food, 3),
                    Throws.InvalidOperationException.With.Message.Contains("only 2 reserved"));
                Assert.That(
                    () => ledger.SetDown(ResourceKind.Food, 4),
                    Throws.InvalidOperationException.With.Message.Contains("only 3 carried"));
                Assert.That(
                    () => ledger.Reserve(ResourceKind.Food, 6),
                    Throws.InvalidOperationException.With.Message.Contains("only 5 available"));
                Assert.That(
                    () => ledger.PickUp(ResourceKind.Food, 6),
                    Throws.InvalidOperationException.With.Message.Contains("only 5 available"));
                Assert.That(ledger.Stock(ResourceKind.Food), Is.EqualTo(10));
                Assert.That(ledger.AuditBalances(), Is.True);
            });
        }

        [Test]
        public void Stock_is_the_sum_of_the_four_buckets()
        {
            var ledger = new ResourceLedger();
            ledger.Open(ResourceKind.Stone, 10);
            ledger.Open(ResourceKind.Wood, 1);
            ledger.Reserve(ResourceKind.Stone, 1);
            ledger.PickUp(ResourceKind.Stone, 2);
            ledger.BeginRecipe(Cook); // 2 stone in process

            Assert.Multiple(() =>
            {
                Assert.That(ledger.Available(ResourceKind.Stone), Is.EqualTo(5));
                Assert.That(ledger.Reserved(ResourceKind.Stone), Is.EqualTo(1));
                Assert.That(ledger.Carried(ResourceKind.Stone), Is.EqualTo(2));
                Assert.That(ledger.InProcess(ResourceKind.Stone), Is.EqualTo(2));
                Assert.That(ledger.Stock(ResourceKind.Stone), Is.EqualTo(10));
            });
        }

        [Test]
        public void Transfer_is_an_export_here_and_an_import_there()
        {
            var band = WithFood(10);
            var settlement = new ResourceLedger();

            band.TransferTo(settlement, ResourceKind.Food, 8);

            Assert.Multiple(() =>
            {
                Assert.That(band.Available(ResourceKind.Food), Is.EqualTo(2));
                Assert.That(band.Flows(ResourceKind.Food).Exported, Is.EqualTo(8L));
                Assert.That(settlement.Available(ResourceKind.Food), Is.EqualTo(8));
                Assert.That(settlement.Flows(ResourceKind.Food).Imported, Is.EqualTo(8L));
                Assert.That(band.AuditBalances(), Is.True);
                Assert.That(settlement.AuditBalances(), Is.True);
            });
        }

        [Test]
        public void Transfer_short_of_stock_touches_neither_ledger()
        {
            var band = WithFood(3);
            var settlement = new ResourceLedger();

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => band.TransferTo(settlement, ResourceKind.Food, 4),
                    Throws.InvalidOperationException);
                Assert.That(band.Available(ResourceKind.Food), Is.EqualTo(3));
                Assert.That(band.Flows(ResourceKind.Food).Exported, Is.Zero);
                Assert.That(settlement.Stock(ResourceKind.Food), Is.Zero);
                Assert.That(settlement.Flows(ResourceKind.Food).Imported, Is.Zero);
            });
        }

        [Test]
        public void Transfer_into_a_full_ledger_touches_neither_ledger()
        {
            // Export runs before import, so without a room check on the
            // receiver the sender would lose the stock and the receiver never
            // get it - both audits would still balance, which is exactly why
            // this needs its own test.
            var band = WithFood(10);
            var settlement = WithFood(int.MaxValue);

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => band.TransferTo(settlement, ResourceKind.Food, 1),
                    Throws.TypeOf<OverflowException>());
                Assert.That(band.Available(ResourceKind.Food), Is.EqualTo(10));
                Assert.That(band.Flows(ResourceKind.Food).Exported, Is.Zero);
                Assert.That(settlement.Stock(ResourceKind.Food), Is.EqualTo(int.MaxValue));
                Assert.That(settlement.Flows(ResourceKind.Food).Imported, Is.Zero);
            });
        }

        [Test]
        public void Transfer_to_self_or_nothing_is_refused()
        {
            var ledger = WithFood(10);

            Assert.Multiple(() =>
            {
                Assert.That(() => ledger.TransferTo(ledger, ResourceKind.Food, 1), Throws.ArgumentException);
                Assert.That(() => ledger.TransferTo(null!, ResourceKind.Food, 1), Throws.ArgumentNullException);
                Assert.That(ledger.Available(ResourceKind.Food), Is.EqualTo(10));
            });
        }

        [Test]
        public void Beginning_a_recipe_moves_its_inputs_into_process()
        {
            var ledger = new ResourceLedger();
            ledger.Open(ResourceKind.Stone, 5);
            ledger.Open(ResourceKind.Wood, 5);

            ledger.BeginRecipe(Cook);

            Assert.Multiple(() =>
            {
                Assert.That(ledger.Available(ResourceKind.Stone), Is.EqualTo(3));
                Assert.That(ledger.InProcess(ResourceKind.Stone), Is.EqualTo(2));
                Assert.That(ledger.Available(ResourceKind.Wood), Is.EqualTo(4));
                Assert.That(ledger.InProcess(ResourceKind.Wood), Is.EqualTo(1));
                Assert.That(ledger.Available(ResourceKind.Food), Is.Zero, "nothing made yet");
                Assert.That(ledger.Flows(ResourceKind.Stone).Consumed, Is.Zero, "nothing consumed yet");
                Assert.That(ledger.AuditBalances(), Is.True);
            });
        }

        [Test]
        public void Beginning_a_recipe_is_all_or_nothing()
        {
            // Enough stone, not enough wood. Stone is checked first and would
            // pass; it must not have moved when wood fails.
            var ledger = new ResourceLedger();
            ledger.Open(ResourceKind.Stone, 5);

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => ledger.BeginRecipe(Cook),
                    Throws.InvalidOperationException.With.Message.Contains("Wood x1"));
                Assert.That(ledger.Available(ResourceKind.Stone), Is.EqualTo(5));
                Assert.That(ledger.InProcess(ResourceKind.Stone), Is.Zero);
                Assert.That(ledger.InProcess(ResourceKind.Wood), Is.Zero);
            });
        }

        [Test]
        public void Completing_a_recipe_consumes_its_inputs_and_produces_its_outputs()
        {
            var ledger = new ResourceLedger();
            ledger.Open(ResourceKind.Stone, 5);
            ledger.Open(ResourceKind.Wood, 5);
            ledger.BeginRecipe(Cook);

            ledger.CompleteRecipe(Cook);

            Assert.Multiple(() =>
            {
                Assert.That(ledger.InProcess(ResourceKind.Stone), Is.Zero);
                Assert.That(ledger.InProcess(ResourceKind.Wood), Is.Zero);
                Assert.That(ledger.Stock(ResourceKind.Stone), Is.EqualTo(3));
                Assert.That(ledger.Stock(ResourceKind.Wood), Is.EqualTo(4));
                Assert.That(ledger.Flows(ResourceKind.Stone).Consumed, Is.EqualTo(2L));
                Assert.That(ledger.Flows(ResourceKind.Wood).Consumed, Is.EqualTo(1L));
                Assert.That(ledger.Available(ResourceKind.Food), Is.EqualTo(5));
                Assert.That(ledger.Flows(ResourceKind.Food).Produced, Is.EqualTo(5L));
                Assert.That(ledger.Flows(ResourceKind.Food).Gathered, Is.Zero, "produced, not gathered");
                Assert.That(ledger.AuditBalances(), Is.True);
            });
        }

        [Test]
        public void Completing_a_gathering_recipe_tallies_its_outputs_as_gathered()
        {
            var ledger = new ResourceLedger();

            ledger.BeginRecipe(Forage);
            ledger.CompleteRecipe(Forage);

            Assert.Multiple(() =>
            {
                Assert.That(ledger.Available(ResourceKind.Food), Is.EqualTo(3));
                Assert.That(ledger.Flows(ResourceKind.Food).Gathered, Is.EqualTo(3L));
                Assert.That(ledger.Flows(ResourceKind.Food).Produced, Is.Zero, "gathered, not produced");
                Assert.That(ledger.AuditBalances(), Is.True);
            });
        }

        [Test]
        public void Cancelling_a_recipe_returns_its_inputs()
        {
            var ledger = new ResourceLedger();
            ledger.Open(ResourceKind.Stone, 5);
            ledger.Open(ResourceKind.Wood, 5);
            ledger.BeginRecipe(Cook);

            ledger.CancelRecipe(Cook);

            Assert.Multiple(() =>
            {
                Assert.That(ledger.Available(ResourceKind.Stone), Is.EqualTo(5));
                Assert.That(ledger.Available(ResourceKind.Wood), Is.EqualTo(5));
                Assert.That(ledger.InProcess(ResourceKind.Stone), Is.Zero);
                Assert.That(ledger.InProcess(ResourceKind.Wood), Is.Zero);
                Assert.That(ledger.Flows(ResourceKind.Stone).Consumed, Is.Zero);
                Assert.That(ledger.Available(ResourceKind.Food), Is.Zero);
                Assert.That(ledger.AuditBalances(), Is.True);
            });
        }

        [Test]
        public void Beginning_or_cancelling_a_gathering_recipe_moves_nothing()
        {
            // No inputs, so nothing to commit and nothing to return. Neither
            // is an error: a forager who gives up simply has no food.
            var ledger = new ResourceLedger();

            Assert.Multiple(() =>
            {
                Assert.That(() => ledger.BeginRecipe(Forage), Throws.Nothing);
                Assert.That(() => ledger.CancelRecipe(Forage), Throws.Nothing);
                Assert.That(ledger.Stock(ResourceKind.Food), Is.Zero);
                Assert.That(ledger.Flows(ResourceKind.Food).TotalIn, Is.Zero);
            });
        }

        [Test]
        public void Completing_or_cancelling_a_run_that_was_never_begun_is_refused()
        {
            // The ledger holds quantities, not runs, so this is the strongest
            // check it can make - and without it, completing an un-begun run
            // would send in-process negative and conjure the outputs.
            var ledger = new ResourceLedger();
            ledger.Open(ResourceKind.Stone, 5);
            ledger.Open(ResourceKind.Wood, 5);

            Assert.Multiple(() =>
            {
                Assert.That(() => ledger.CompleteRecipe(Cook), Throws.InvalidOperationException);
                Assert.That(() => ledger.CancelRecipe(Cook), Throws.InvalidOperationException);
                Assert.That(ledger.Available(ResourceKind.Food), Is.Zero);
                Assert.That(ledger.Available(ResourceKind.Stone), Is.EqualTo(5));
                Assert.That(ledger.AuditBalances(), Is.True);
            });
        }

        [Test]
        public void A_second_run_short_of_one_input_leaves_the_first_run_in_process()
        {
            // Two runs begun, one input's worth short for a third. The
            // partially-satisfied check must not disturb the two in process.
            var ledger = new ResourceLedger();
            ledger.Open(ResourceKind.Stone, 5);
            ledger.Open(ResourceKind.Wood, 2);
            ledger.BeginRecipe(Cook);
            ledger.BeginRecipe(Cook);

            Assert.Multiple(() =>
            {
                Assert.That(() => ledger.BeginRecipe(Cook), Throws.InvalidOperationException);
                Assert.That(ledger.InProcess(ResourceKind.Stone), Is.EqualTo(4));
                Assert.That(ledger.InProcess(ResourceKind.Wood), Is.EqualTo(2));
            });

            ledger.CompleteRecipe(Cook);

            Assert.Multiple(() =>
            {
                Assert.That(ledger.InProcess(ResourceKind.Stone), Is.EqualTo(2), "one run still in process");
                Assert.That(ledger.Available(ResourceKind.Food), Is.EqualTo(5));
                Assert.That(ledger.AuditBalances(), Is.True);
            });
        }

        [Test]
        public void Recipe_operations_reject_null()
        {
            var ledger = new ResourceLedger();

            Assert.Multiple(() =>
            {
                Assert.That(() => ledger.BeginRecipe(null!), Throws.ArgumentNullException);
                Assert.That(() => ledger.CompleteRecipe(null!), Throws.ArgumentNullException);
                Assert.That(() => ledger.CancelRecipe(null!), Throws.ArgumentNullException);
            });
        }

        [Test]
        public void Growing_stock_past_the_type_throws_rather_than_wrapping()
        {
            // The check is on the whole stock, not the one bucket: with the
            // maximum reserved and nothing available, adding one more would
            // leave every bucket in range and the sum out of it.
            var ledger = new ResourceLedger();
            ledger.Gather(ResourceKind.Food, int.MaxValue);
            ledger.Reserve(ResourceKind.Food, int.MaxValue);

            Assert.Multiple(() =>
            {
                Assert.That(() => ledger.Gather(ResourceKind.Food, 1), Throws.TypeOf<OverflowException>());
                Assert.That(() => ledger.Open(ResourceKind.Food, 1), Throws.TypeOf<OverflowException>());
                Assert.That(() => ledger.Import(ResourceKind.Food, 1), Throws.TypeOf<OverflowException>());
                Assert.That(ledger.Available(ResourceKind.Food), Is.Zero);
                Assert.That(ledger.Stock(ResourceKind.Food), Is.EqualTo(int.MaxValue));
                Assert.That(ledger.AuditBalances(), Is.True);
            });
        }

        [Test]
        public void A_recipe_whose_output_would_overflow_is_refused_on_completion()
        {
            var ledger = new ResourceLedger();
            ledger.Open(ResourceKind.Stone, 2);
            ledger.Open(ResourceKind.Wood, 1);
            ledger.Gather(ResourceKind.Food, int.MaxValue);
            ledger.BeginRecipe(Cook);

            Assert.Multiple(() =>
            {
                Assert.That(() => ledger.CompleteRecipe(Cook), Throws.TypeOf<OverflowException>());
                Assert.That(ledger.Stock(ResourceKind.Food), Is.EqualTo(int.MaxValue));
                // All or nothing, as with beginning: the inputs are still in
                // process, not consumed for an output that never appeared.
                Assert.That(ledger.InProcess(ResourceKind.Stone), Is.EqualTo(2));
                Assert.That(ledger.InProcess(ResourceKind.Wood), Is.EqualTo(1));
                Assert.That(ledger.Flows(ResourceKind.Stone).Consumed, Is.Zero);
            });
        }

        [Test]
        public void A_recipe_with_a_kind_on_both_sides_completes_at_full_stock()
        {
            // Food x1 -> Food x1 leaves stock unchanged, so it must complete
            // even at the maximum. A check against stock before the input
            // comes out would refuse it.
            var refine = new Recipe(
                "Refine",
                new[] { new ResourceQuantity(ResourceKind.Food, 1) },
                new[] { new ResourceQuantity(ResourceKind.Food, 1) },
                1L);
            var ledger = WithFood(int.MaxValue);
            ledger.BeginRecipe(refine);

            Assert.Multiple(() =>
            {
                Assert.That(() => ledger.CompleteRecipe(refine), Throws.Nothing);
                Assert.That(ledger.Available(ResourceKind.Food), Is.EqualTo(int.MaxValue));
                Assert.That(ledger.Flows(ResourceKind.Food).Consumed, Is.EqualTo(1L));
                Assert.That(ledger.Flows(ResourceKind.Food).Produced, Is.EqualTo(1L));
                Assert.That(ledger.AuditBalances(), Is.True);
            });
        }

        [Test]
        public void A_recipe_that_nets_more_than_the_room_left_is_still_refused()
        {
            // Food x1 -> Food x2 nets one more. With one unit of room it
            // completes; with none it must not, and the input stays in
            // process.
            var grow = new Recipe(
                "Grow",
                new[] { new ResourceQuantity(ResourceKind.Food, 1) },
                new[] { new ResourceQuantity(ResourceKind.Food, 2) },
                1L);
            var ledger = WithFood(int.MaxValue);
            ledger.BeginRecipe(grow);

            Assert.Multiple(() =>
            {
                Assert.That(() => ledger.CompleteRecipe(grow), Throws.TypeOf<OverflowException>());
                Assert.That(ledger.InProcess(ResourceKind.Food), Is.EqualTo(1));
                Assert.That(ledger.Stock(ResourceKind.Food), Is.EqualTo(int.MaxValue));
            });
        }

        [Test]
        public void Completing_a_gathering_recipe_never_begun_is_a_gather()
        {
            // A gathering recipe has no inputs, so there is nothing for the
            // un-begun check to look at and Complete is exactly Gather. That
            // is not a hole: Gather is a public operation, and the run itself
            // is #52's task state, not the ledger's. Pinned so the remarks
            // cannot drift from the behaviour.
            var ledger = new ResourceLedger();

            Assert.Multiple(() =>
            {
                Assert.That(() => ledger.CompleteRecipe(Forage), Throws.Nothing);
                Assert.That(ledger.Available(ResourceKind.Food), Is.EqualTo(3));
                Assert.That(ledger.Flows(ResourceKind.Food).Gathered, Is.EqualTo(3L));
                Assert.That(ledger.AuditBalances(), Is.True);
            });
        }

        [Test]
        public void The_audit_balances_after_every_kind_of_operation()
        {
            var ledger = new ResourceLedger();
            var other = new ResourceLedger();

            ledger.Open(ResourceKind.Food, 20);
            ledger.Open(ResourceKind.Wood, 10);
            ledger.Open(ResourceKind.Stone, 10);
            ledger.Gather(ResourceKind.Food, 5);
            other.Open(ResourceKind.Wood, 3);
            other.TransferTo(ledger, ResourceKind.Wood, 3);
            ledger.Consume(ResourceKind.Food, 6);
            ledger.Destroy(ResourceKind.Food, 1);
            ledger.Embody(ResourceKind.Wood, 4);
            ledger.TransferTo(other, ResourceKind.Stone, 2);
            ledger.Reserve(ResourceKind.Wood, 2);
            ledger.Release(ResourceKind.Wood, 1);
            ledger.PickUp(ResourceKind.Food, 3);
            ledger.SetDown(ResourceKind.Food, 1);
            ledger.BeginRecipe(Cook);
            ledger.BeginRecipe(Cook);
            ledger.CancelRecipe(Cook);
            ledger.CompleteRecipe(Cook);
            ledger.BeginRecipe(Forage);
            ledger.CompleteRecipe(Forage);

            var food = ledger.Flows(ResourceKind.Food);

            Assert.Multiple(() =>
            {
                Assert.That(ledger.AuditBalances(), Is.True);
                Assert.That(other.AuditBalances(), Is.True);

                // Spot-check the food figures by hand: in 20 + 5 + 5 + 3, out
                // 6 + 1, so 26 exist, of which 2 are carried.
                Assert.That(food.Opening, Is.EqualTo(20L));
                Assert.That(food.Gathered, Is.EqualTo(8L));
                Assert.That(food.Produced, Is.EqualTo(5L));
                Assert.That(food.Consumed, Is.EqualTo(6L));
                Assert.That(food.Destroyed, Is.EqualTo(1L));
                Assert.That(food.TotalIn, Is.EqualTo(33L));
                Assert.That(food.TotalOut, Is.EqualTo(7L));
                Assert.That(ledger.Stock(ResourceKind.Food), Is.EqualTo(26));
                Assert.That(ledger.Carried(ResourceKind.Food), Is.EqualTo(2));
            });
        }
    }
}
