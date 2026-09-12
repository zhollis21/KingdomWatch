using KingdomWatch.Core.Data;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Performance
{
    /// <summary>
    /// The ledger sits under every eat, gather and craft the tick loop runs,
    /// so it gets the same zero-allocation guard as the scheduler (#59).
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    public sealed class ResourceLedgerAllocationTests
    {
        private static readonly Recipe Cook = new Recipe(
            "Cook",
            new[] { new ResourceQuantity(ResourceKind.Stone, 1), new ResourceQuantity(ResourceKind.Wood, 1) },
            new[] { new ResourceQuantity(ResourceKind.Food, 2) },
            1L);

        [Test]
        public void Every_operation_at_steady_state_allocates_nothing()
        {
            var ledger = new ResourceLedger();
            var other = new ResourceLedger();

            // First pass JITs every path. Measure the second.
            Workload(ledger, other);

            var allocated = Allocations.Measure(() => Workload(ledger, other));

            Assert.That(allocated, Is.Zero, "bytes allocated on the test thread across the workload");
        }

        private static void Workload(ResourceLedger ledger, ResourceLedger other)
        {
            for (var i = 0; i < 1000; i++)
            {
                ledger.Gather(ResourceKind.Stone, 2);
                ledger.Gather(ResourceKind.Wood, 2);
                ledger.BeginRecipe(PrimitiveTier.Forage);
                ledger.CompleteRecipe(PrimitiveTier.Forage);
                ledger.BeginRecipe(Cook);
                ledger.CompleteRecipe(Cook);
                ledger.BeginRecipe(Cook);
                ledger.CancelRecipe(Cook);
                ledger.Reserve(ResourceKind.Food, 1);
                ledger.Release(ResourceKind.Food, 1);
                ledger.PickUp(ResourceKind.Food, 1);
                ledger.SetDown(ResourceKind.Food, 1);
                ledger.Consume(ResourceKind.Food, 2);
                ledger.Destroy(ResourceKind.Food, 1);
                ledger.Embody(ResourceKind.Wood, 1);
                ledger.TransferTo(other, ResourceKind.Food, 1);
                other.TransferTo(ledger, ResourceKind.Food, 1);
                ledger.Consume(ResourceKind.Food, ledger.Available(ResourceKind.Food));
                ledger.Consume(ResourceKind.Stone, ledger.Available(ResourceKind.Stone));
                _ = ledger.Stock(ResourceKind.Wood);
                _ = ledger.Flows(ResourceKind.Wood);
                _ = ledger.AuditBalances();
            }
        }
    }
}
