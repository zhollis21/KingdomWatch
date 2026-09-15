using System;
using System.Globalization;

namespace KingdomWatch.Core.Traversal
{
    /// <summary>
    /// What one <see cref="TerrainKind"/> costs to enter and what it takes to
    /// be allowed in. One row of <see cref="TerrainRules"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="Cost"/> is an integer in abstract traversal units, section 5:
    /// routes are compared and summed in branching code, so they cannot be
    /// floating point. Converting units into travel time is the mover's
    /// business - a band and a cart cover the same cost at different speeds.
    ///
    /// A rule is either passable to at least one <see cref="Transport"/> with a
    /// positive cost, or it is <see cref="Impassable"/>. There is no third
    /// state, so a cell that admits nobody cannot carry a cost that some code
    /// path might read as if it did.
    ///
    /// Costs are capped at <see cref="MaxCost"/> so that a route total fits in
    /// an int without checking every addition: at 14 per diagonal step, a
    /// route would have to enter over 150,000 cells at the cap to overflow,
    /// which is longer than any map this game will draw has cells across it.
    /// </remarks>
    public readonly struct TerrainRule : IEquatable<TerrainRule>
    {
        /// <summary>Nobody may enter. Cost is zero because it is never paid.</summary>
        public static readonly TerrainRule Impassable = default;

        /// <summary>The highest cost a passable kind may carry. Plains are 10.</summary>
        public const int MaxCost = 1000;

        public TerrainRule(int cost, Transport allowed)
        {
            if (cost <= 0 || cost > MaxCost)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cost), cost, "A passable rule needs a cost from 1 to " + MaxCost + "; use TerrainRule.Impassable for none.");
            }

            if (allowed == Transport.None)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(allowed), allowed, "A passable rule must admit some transport; use TerrainRule.Impassable for none.");
            }

            if (!TransportGuard.IsDefined(allowed))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(allowed), allowed, "Not a combination of defined Transport flags.");
            }

            Cost = cost;
            Allowed = allowed;
        }

        /// <summary>Traversal units to enter a cell of this kind. Zero only when impassable.</summary>
        public int Cost { get; }

        /// <summary>Which transports may enter. <see cref="Transport.None"/> means nobody.</summary>
        public Transport Allowed { get; }

        public bool IsImpassable => Allowed == Transport.None;

        /// <summary>Whether a mover with these transports may enter.</summary>
        public bool Admits(Transport mover) => (Allowed & mover) != 0;

        public bool Equals(TerrainRule other) => Cost == other.Cost && Allowed == other.Allowed;

        public override bool Equals(object? obj) => obj is TerrainRule other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Cost, (int)Allowed);

        public override string ToString() =>
            IsImpassable
                ? "impassable"
                : Cost.ToString(CultureInfo.InvariantCulture) + " by " + Allowed;

        public static bool operator ==(TerrainRule left, TerrainRule right) => left.Equals(right);

        public static bool operator !=(TerrainRule left, TerrainRule right) => !left.Equals(right);
    }
}
