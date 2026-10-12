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

        /// <summary>Stone a community holds with no storage built.</summary>
        public const int BaseStone = 100;

        /// <summary>Food each finished Granary adds.</summary>
        public const int GranaryFood = 500;

        /// <summary>Grain each finished Granary adds.</summary>
        public const int GranaryGrain = 100;

        /// <summary>Wood each finished Woodshed adds.</summary>
        public const int WoodshedWood = 250;

        /// <summary>
        /// Whether a store counts as full for the demand for more storage:
        /// within a tenth of its capacity, so a night's eating or a day's
        /// fuel before the dawn it is read at does not hide it.
        /// </summary>
        public static bool IsFull(long available, long capacity) => available * 10L >= capacity * 9L;

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
        public static long WoodInShed(long wood, int order)
        {
            if (wood < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(wood), wood, "Wood cannot be negative.");
            }

            if (order < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(order), order, "The first Woodshed is 0.");
            }

            var before = BaseWood + (long)order * WoodshedWood;
            return wood <= before ? 0L : Math.Min(wood - before, WoodshedWood);
        }

        /// <summary>
        /// Loses whatever of a kind is available past <paramref name="capacity"/>,
        /// as destroyed, so the ledger still balances.
        /// </summary>
        public static void Spill(ResourceLedger stores, ResourceKind kind, long capacity)
        {
            if (stores is null)
            {
                throw new ArgumentNullException(nameof(stores));
            }

            var over = stores.Available(kind) - capacity;

            if (over > 0L)
            {
                stores.Destroy(kind, (int)over);
            }
        }
    }
}
