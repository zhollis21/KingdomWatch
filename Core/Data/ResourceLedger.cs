using System;
using System.Collections.Generic;
using System.Globalization;

namespace KingdomWatch.Core.Data
{
    /// <summary>
    /// The one authoritative account of what a holder owns. Every quality
    /// level of the simulation manipulates this and nothing else: nearby,
    /// "Bob picks up 4 logs" is a move between two of its buckets; offscreen,
    /// "lumber +40" is a gather. The visible pile of logs is a rendering of
    /// this, never the other way round.
    /// </summary>
    /// <remarks>
    /// If near and far LOD kept resources in different state, changing LOD
    /// would duplicate or lose them. So there is one ledger type, each holder
    /// (a <see cref="MobileGroup"/> now, a settlement later) owns one, and
    /// every mutation is a named operation here. See
    /// docs/design/kingdom-watch-plan-v7.1.md section 12.
    ///
    /// Two invariants, both from section 12:
    ///
    /// <list type="bullet">
    /// <item><c>available + reserved + carried + inProcess == stock</c>.
    /// Stock is derived rather than stored, so this holds by construction;
    /// what the operations enforce is that no bucket ever goes negative.</item>
    /// <item><c>opening + produced + gathered + imported == stock + consumed +
    /// exported + destroyed + embodied</c> - the conservation audit, checked
    /// by <see cref="AuditBalances()"/>. The flow counters are maintained
    /// here, because there is nowhere else they could be counted; evaluating
    /// the equation is the WorldValidator's job (#13), not something done per
    /// mutation.</item>
    /// </list>
    ///
    /// Both are structural while this class is the only write path. They
    /// become real checks the moment a second path exists - save/load, a bulk
    /// span, a migration - which is the same reasoning as the PersonStore
    /// invariants recorded on #13.
    ///
    /// Reserved and carried are defined here so that the reservation system
    /// (#24) and hauling (M3) extend this class rather than growing a second
    /// representation. Moving straight from reserved to carried is #24's to
    /// define. Quantities are integers because the sim branches on them.
    ///
    /// Flat arrays indexed by <see cref="ResourceKind"/>, nothing allocated
    /// after construction, and integer arithmetic that throws on overflow
    /// rather than wrapping silently.
    /// </remarks>
    public sealed class ResourceLedger
    {
        private static readonly bool[] DefinedKinds = EnumGuard.BuildMask(typeof(ResourceKind));

        // One account per declared kind, including a never-used slot for None
        // so the enum value is the index. Four bytes wasted per bucket beats an
        // off-by-one on every access.
        private readonly Account[] _accounts = new Account[DefinedKinds.Length];

        /// <summary>Stock on hand and free to be used, reserved, or carried.</summary>
        public int Available(ResourceKind kind) => Ref(kind).Available;

        /// <summary>Stock promised to a task. Not free.</summary>
        public int Reserved(ResourceKind kind) => Ref(kind).Reserved;

        /// <summary>Stock in someone's hands between places.</summary>
        public int Carried(ResourceKind kind) => Ref(kind).Carried;

        /// <summary>Recipe inputs committed to a run that has not finished.</summary>
        public int InProcess(ResourceKind kind) => Ref(kind).InProcess;

        /// <summary>Everything that exists: the sum of the four buckets.</summary>
        public int Stock(ResourceKind kind) => StockOf(ref Ref(kind));

        /// <summary>The cumulative in and out flows for the conservation audit.</summary>
        public ResourceFlows Flows(ResourceKind kind)
        {
            ref var account = ref Ref(kind);
            return new ResourceFlows(
                account.Opening,
                account.Produced,
                account.Gathered,
                account.Imported,
                account.Consumed,
                account.Exported,
                account.Destroyed,
                account.Embodied);
        }

        /// <summary>
        /// The conservation audit for one resource: everything that ever
        /// entered equals what is here plus everything that ever left.
        /// </summary>
        public bool AuditBalances(ResourceKind kind)
        {
            var flows = Flows(kind);
            return flows.TotalIn == Stock(kind) + flows.TotalOut;
        }

        /// <summary>The conservation audit across every resource.</summary>
        public bool AuditBalances()
        {
            // From 1: index 0 is None, which is never a resource. The mask
            // check is for a gap in the enum, should one ever be reserved.
            for (var kind = 1; kind < DefinedKinds.Length; kind++)
            {
                if (DefinedKinds[kind] && !AuditBalances((ResourceKind)kind))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Stock the holder starts with. World generation and band creation
        /// use this; it is the only way stock appears from nowhere without
        /// being gathered.
        /// </summary>
        public void Open(ResourceKind kind, int quantity)
        {
            ref var account = ref Ref(kind, quantity);
            AddAvailable(ref account, kind, quantity);
            account.Opening = checked(account.Opening + quantity);
        }

        /// <summary>Stock taken from the world: foraged, felled, quarried.</summary>
        public void Gather(ResourceKind kind, int quantity)
        {
            ref var account = ref Ref(kind, quantity);
            AddAvailable(ref account, kind, quantity);
            account.Gathered = checked(account.Gathered + quantity);
        }

        /// <summary>Stock received from another ledger. See <see cref="TransferTo"/>.</summary>
        public void Import(ResourceKind kind, int quantity)
        {
            ref var account = ref Ref(kind, quantity);
            AddAvailable(ref account, kind, quantity);
            account.Imported = checked(account.Imported + quantity);
        }

        /// <summary>Stock used up: eaten, burnt as fuel.</summary>
        public void Consume(ResourceKind kind, int quantity)
        {
            ref var account = ref Ref(kind, quantity);
            TakeAvailable(ref account, kind, quantity);
            account.Consumed = checked(account.Consumed + quantity);
        }

        /// <summary>Stock handed to another ledger. See <see cref="TransferTo"/>.</summary>
        public void Export(ResourceKind kind, int quantity)
        {
            ref var account = ref Ref(kind, quantity);
            TakeAvailable(ref account, kind, quantity);
            account.Exported = checked(account.Exported + quantity);
        }

        /// <summary>Stock lost: spoiled, raided, burned down.</summary>
        public void Destroy(ResourceKind kind, int quantity)
        {
            ref var account = ref Ref(kind, quantity);
            TakeAvailable(ref account, kind, quantity);
            account.Destroyed = checked(account.Destroyed + quantity);
        }

        /// <summary>Stock built into a camp or building. No longer stock.</summary>
        public void Embody(ResourceKind kind, int quantity)
        {
            ref var account = ref Ref(kind, quantity);
            TakeAvailable(ref account, kind, quantity);
            account.Embodied = checked(account.Embodied + quantity);
        }

        /// <summary>Promises available stock to a task.</summary>
        public void Reserve(ResourceKind kind, int quantity)
        {
            ref var account = ref Ref(kind, quantity);
            TakeAvailable(ref account, kind, quantity);
            account.Reserved += quantity;
        }

        /// <summary>Returns a reservation to the available pool.</summary>
        public void Release(ResourceKind kind, int quantity)
        {
            ref var account = ref Ref(kind, quantity);
            Take(ref account.Reserved, kind, quantity, "reserved");
            account.Available += quantity;
        }

        /// <summary>Someone picks available stock up.</summary>
        public void PickUp(ResourceKind kind, int quantity)
        {
            ref var account = ref Ref(kind, quantity);
            TakeAvailable(ref account, kind, quantity);
            account.Carried += quantity;
        }

        /// <summary>Someone sets carried stock down.</summary>
        public void SetDown(ResourceKind kind, int quantity)
        {
            ref var account = ref Ref(kind, quantity);
            Take(ref account.Carried, kind, quantity, "carried");
            account.Available += quantity;
        }

        /// <summary>
        /// Moves stock to another ledger: an export here and an import there,
        /// so both audits still balance. How a band's supplies become a new
        /// settlement's stores.
        /// </summary>
        public void TransferTo(ResourceLedger other, ResourceKind kind, int quantity)
        {
            if (other is null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            if (ReferenceEquals(other, this))
            {
                throw new ArgumentException(
                    "A ledger cannot transfer to itself.", nameof(other));
            }

            // Export runs first, so the receiver is checked for room before
            // anything leaves - otherwise a full receiver would lose the stock
            // in transit with both audits still balancing.
            ThrowIfNoRoom(ref other.Ref(kind, quantity), kind, quantity);
            Export(kind, quantity);
            other.Import(kind, quantity);
        }

        /// <summary>
        /// Commits a recipe's inputs to a run: they leave the available pool
        /// and sit in process until <see cref="CompleteRecipe"/> or
        /// <see cref="CancelRecipe"/>. All or nothing - if any input is short,
        /// nothing moves.
        /// </summary>
        /// <remarks>
        /// Checking every input before moving any is what makes this safe to
        /// call speculatively: a task can attempt a recipe and, on failure,
        /// know the ledger is exactly as it was.
        /// </remarks>
        public void BeginRecipe(Recipe recipe)
        {
            if (recipe is null)
            {
                throw new ArgumentNullException(nameof(recipe));
            }

            var inputs = recipe.Inputs;

            for (var i = 0; i < inputs.Count; i++)
            {
                var line = inputs[i];
                ref var account = ref Ref(line.Kind);

                if (account.Available < line.Quantity)
                {
                    throw new InvalidOperationException(
                        "Cannot begin " + recipe.Name + ": needs " + line
                        + " but only " + Describe(account.Available) + " available.");
                }
            }

            for (var i = 0; i < inputs.Count; i++)
            {
                var line = inputs[i];
                ref var account = ref Ref(line.Kind);
                account.Available -= line.Quantity;
                account.InProcess += line.Quantity;
            }
        }

        /// <summary>
        /// Abandons a run: the inputs return to the available pool. Throws if
        /// the run was never begun, because that would conjure stock.
        /// </summary>
        public void CancelRecipe(Recipe recipe)
        {
            if (recipe is null)
            {
                throw new ArgumentNullException(nameof(recipe));
            }

            var inputs = recipe.Inputs;
            ThrowUnlessInProcess(recipe, inputs);

            for (var i = 0; i < inputs.Count; i++)
            {
                var line = inputs[i];
                ref var account = ref Ref(line.Kind);
                account.InProcess -= line.Quantity;
                account.Available += line.Quantity;
            }
        }

        /// <summary>
        /// Finishes a run: the inputs are consumed and the outputs become
        /// available. Outputs of a gathering recipe are tallied as gathered
        /// rather than produced, since they came from the world, not from
        /// stock. Throws if the run was never begun.
        /// </summary>
        public void CompleteRecipe(Recipe recipe)
        {
            if (recipe is null)
            {
                throw new ArgumentNullException(nameof(recipe));
            }

            var inputs = recipe.Inputs;
            var outputs = recipe.Outputs;
            ThrowUnlessInProcess(recipe, inputs);

            // All or nothing, as with BeginRecipe: an output that would not fit
            // must not cost the inputs. Checked against stock before the inputs
            // come out, which is conservative when a kind is on both sides -
            // that only matters within a few units of int.MaxValue.
            for (var i = 0; i < outputs.Count; i++)
            {
                var line = outputs[i];
                ThrowIfNoRoom(ref Ref(line.Kind), line.Kind, line.Quantity);
            }

            for (var i = 0; i < inputs.Count; i++)
            {
                var line = inputs[i];
                ref var account = ref Ref(line.Kind);
                account.InProcess -= line.Quantity;
                account.Consumed = checked(account.Consumed + line.Quantity);
            }

            for (var i = 0; i < outputs.Count; i++)
            {
                var line = outputs[i];

                if (recipe.IsGathering)
                {
                    Gather(line.Kind, line.Quantity);
                }
                else
                {
                    ref var account = ref Ref(line.Kind);
                    AddAvailable(ref account, line.Kind, line.Quantity);
                    account.Produced = checked(account.Produced + line.Quantity);
                }
            }
        }

        // The ledger cannot tell which run a completion belongs to - it holds
        // quantities, not runs - so the check is that at least one run's worth
        // of every input is in process. Completing a run that was never begun
        // would otherwise send in-process negative and conjure the outputs.
        private void ThrowUnlessInProcess(
            Recipe recipe, IReadOnlyList<ResourceQuantity> inputs)
        {
            for (var i = 0; i < inputs.Count; i++)
            {
                var line = inputs[i];
                ref var account = ref Ref(line.Kind);

                if (account.InProcess < line.Quantity)
                {
                    throw new InvalidOperationException(
                        recipe.Name + " was not begun: needs " + line
                        + " in process but only " + Describe(account.InProcess) + " is.");
                }
            }
        }

        private ref Account Ref(ResourceKind kind)
        {
            ResourceQuantity.ThrowIfNotAResource(kind, nameof(kind));
            return ref _accounts[(int)kind];
        }

        private ref Account Ref(ResourceKind kind, int quantity)
        {
            if (quantity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(quantity), quantity, "Ledger operations move a positive quantity.");
            }

            return ref Ref(kind);
        }

        // Stock can grow only here, and the check is on the whole stock rather
        // than on the one bucket, so the sum the buckets make never exceeds
        // int.MaxValue. That is what lets the moves between buckets use plain
        // arithmetic: a bucket is never larger than the stock it is part of.
        private static void AddAvailable(ref Account account, ResourceKind kind, int quantity)
        {
            ThrowIfNoRoom(ref account, kind, quantity);
            account.Available += quantity;
        }

        private static void ThrowIfNoRoom(ref Account account, ResourceKind kind, int quantity)
        {
            if (StockOf(ref account) > int.MaxValue - quantity)
            {
                throw new OverflowException(
                    "Adding " + Describe(quantity) + " " + kind + " would overflow the ledger.");
            }
        }

        private static int StockOf(ref Account account) =>
            account.Available + account.Reserved + account.Carried + account.InProcess;

        private static void TakeAvailable(ref Account account, ResourceKind kind, int quantity) =>
            Take(ref account.Available, kind, quantity, "available");

        private static void Take(ref int bucket, ResourceKind kind, int quantity, string bucketName)
        {
            if (bucket < quantity)
            {
                throw new InvalidOperationException(
                    "Cannot take " + Describe(quantity) + " " + kind + ": only "
                    + Describe(bucket) + " " + bucketName + ".");
            }

            bucket -= quantity;
        }

        private static string Describe(int quantity) =>
            quantity.ToString(CultureInfo.InvariantCulture);

        // Plain fields in a plain struct. The class mutates them in place
        // through a ref, and nothing outside sees the layout.
        private struct Account
        {
            public int Available;
            public int Reserved;
            public int Carried;
            public int InProcess;

            public long Opening;
            public long Produced;
            public long Gathered;
            public long Imported;
            public long Consumed;
            public long Exported;
            public long Destroyed;
            public long Embodied;
        }
    }
}
