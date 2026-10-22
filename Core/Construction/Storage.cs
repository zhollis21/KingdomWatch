using System;
using KingdomWatch.Core.Data;

namespace KingdomWatch.Core.Construction
{
    /// <summary>
    /// How much of each kind a community can hold (#98, economy ladder
    /// section 5): a base every band and settlement has for free, and what
    /// each finished storage building adds for its bundle.
    /// </summary>
    /// <remarks>
    /// Workers stop early rather than fill past it: <see cref="Work.Jobs"/>
    /// aims each store at the lesser of what it wants and its capacity, and
    /// Farmers stop when the Grain is full. What still arrives past it - a
    /// harvest day, the timber and rock of a clearing, the last load of a
    /// trip - is lost (<see cref="Spill"/>). The numbers are placeholders,
    /// set by the harness.
    /// </remarks>
    public static class Storage
    {
        /// <summary>Food a community holds with no storage built.</summary>
        public const int BaseFood = 500;

        /// <summary>Grain a community holds with no storage built.</summary>
        public const int BaseGrain = 100;

        /// <summary>Wood a community holds with no storage built.</summary>
        public const int BaseWood = 250;

        /// <summary>
        /// Stone a community holds with no storage built: the dearest
        /// building's stone and no more (#178), so a growing village wants a
        /// Stoneyard for the reserve it keeps (<see cref="Work.Jobs.StoneWant"/>).
        /// </summary>
        public const int BaseStone = 50;

        /// <summary>Food each finished Granary adds.</summary>
        public const int GranaryFood = 500;

        /// <summary>Grain each finished Granary adds.</summary>
        public const int GranaryGrain = 100;

        /// <summary>Wood each finished Woodshed adds.</summary>
        public const int WoodshedWood = 250;

        /// <summary>Stone each finished Stoneyard adds (#178).</summary>
        public const int StoneyardStone = 50;

        /// <summary>
        /// Whether a store counts as full for the demand for more storage:
        /// within a tenth of its capacity, so a night's eating or a day's
        /// fuel before the dawn it is read at does not hide it. Throws for a
        /// negative amount or capacity.
        /// </summary>
        public static bool IsFull(long available, long capacity)
        {
            if (available < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(available), available, "A store cannot hold less than nothing.");
            }

            RequireCapacity(capacity);

            // Nine tenths, rounded up, without a product that overflows near
            // long.MaxValue (#98 review).
            return available >= capacity - (capacity / 10L);
        }

        /// <summary>The capacity of a kind with no storage built. Throws for <see cref="ResourceKind.None"/> or an undefined kind.</summary>
        public static int BaseOf(ResourceKind kind)
        {
            switch (kind)
            {
                case ResourceKind.Food:
                    return BaseFood;
                case ResourceKind.Grain:
                    return BaseGrain;
                case ResourceKind.Wood:
                    return BaseWood;
                case ResourceKind.Stone:
                    return BaseStone;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a defined ResourceKind, or None.");
            }
        }

        /// <summary>
        /// What one finished building of a kind adds to the capacity of a
        /// resource: nothing, unless it is storage for it. Throws for
        /// <see cref="BuildingKind.None"/>, <see cref="ResourceKind.None"/>
        /// or an undefined value of either.
        /// </summary>
        public static int AddedBy(BuildingKind building, ResourceKind kind)
        {
            BuildingTable.Of(building);
            BaseOf(kind);

            switch (building)
            {
                case BuildingKind.Granary:
                    return kind == ResourceKind.Food ? GranaryFood : kind == ResourceKind.Grain ? GranaryGrain : 0;
                case BuildingKind.Woodshed:
                    return kind == ResourceKind.Wood ? WoodshedWood : 0;
                case BuildingKind.Stoneyard:
                    return kind == ResourceKind.Stone ? StoneyardStone : 0;
                default:
                    return 0;
            }
        }

        /// <summary>
        /// The share of a settlement's <paramref name="wood"/> its Woodshed
        /// <paramref name="order"/> holds - 0 for the first finished, 1 for
        /// the next - when the base holds the first <see cref="BaseWood"/>
        /// and each Woodshed in turn the next <see cref="WoodshedWood"/>. The
        /// ledger keeps one total, not one per building; this is how the view
        /// draws each yard's logs. Throws for a negative amount or order.
        /// </summary>
        public static long WoodInShed(long wood, int order) =>
            HeldBy(wood, order, BaseWood, WoodshedWood, nameof(wood));

        /// <summary>
        /// The share of a settlement's <paramref name="stone"/> its Stoneyard
        /// <paramref name="order"/> holds, as <see cref="WoodInShed"/> shares
        /// out wood (#178): the base holds the first <see cref="BaseStone"/>,
        /// and each Stoneyard in turn the next <see cref="StoneyardStone"/>.
        /// Throws for a negative amount or order.
        /// </summary>
        public static long StoneInYard(long stone, int order) =>
            HeldBy(stone, order, BaseStone, StoneyardStone, nameof(stone));

        private static long HeldBy(long amount, int order, int baseHolds, int eachHolds, string name)
        {
            if (amount < 0L)
            {
                throw new ArgumentOutOfRangeException(name, amount, "A store cannot hold less than nothing.");
            }

            if (order < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(order), order, "The first is 0.");
            }

            var before = baseHolds + (long)order * eachHolds;
            return amount <= before ? 0L : Math.Min(amount - before, eachHolds);
        }

        /// <summary>
        /// Loses whatever of a kind is available past <paramref name="capacity"/>,
        /// as destroyed, so the ledger still balances. Throws for a negative
        /// capacity.
        /// </summary>
        public static void Spill(ResourceLedger stores, ResourceKind kind, long capacity)
        {
            if (stores is null)
            {
                throw new ArgumentNullException(nameof(stores));
            }

            RequireCapacity(capacity);

            var over = stores.Available(kind) - capacity;

            if (over > 0L)
            {
                stores.Destroy(kind, (int)over);
            }
        }

        private static void RequireCapacity(long capacity)
        {
            if (capacity < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "A capacity cannot be negative.");
            }
        }
    }
}
