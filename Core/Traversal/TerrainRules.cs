using System;

namespace KingdomWatch.Core.Traversal
{
    /// <summary>
    /// The traversal cost table: one <see cref="TerrainRule"/> per
    /// <see cref="TerrainKind"/>. Every movement in the game resolves against
    /// this rather than against code that knows what forests or rivers are.
    /// </summary>
    /// <remarks>
    /// Data, not code, for the same reason recipes are (section 9): a bridge
    /// (#35) is a cell rewritten from <see cref="TerrainKind.SmallRiver"/> to
    /// a passable kind, and boats (#45) are a mover that carries
    /// <see cref="Transport.Boat"/>. Neither touches the pathfinder. A table
    /// with a hole in it is refused at construction, because the hole would
    /// otherwise surface as an exception in the middle of a route query.
    ///
    /// <see cref="Default"/> holds placeholder numbers in the
    /// <see cref="Data.PrimitiveTier"/> sense: plausible enough to route
    /// around a forest, and nothing to read a balance decision into. Tuning
    /// is a data change.
    /// </remarks>
    public sealed class TerrainRules
    {
        private static readonly bool[] DefinedKinds = EnumGuard.BuildMask(typeof(TerrainKind));

        /// <summary>
        /// Placeholder costs: plains are the baseline, forest doubles it, hills
        /// triple it; deep water is as easy as plains for anything that floats;
        /// a small river admits nobody until bridged.
        /// </summary>
        public static readonly TerrainRules Default = new TerrainRules(
            (TerrainKind.Plains, new TerrainRule(10, Transport.Foot)),
            (TerrainKind.Forest, new TerrainRule(20, Transport.Foot)),
            (TerrainKind.Hills, new TerrainRule(30, Transport.Foot)),
            (TerrainKind.SmallRiver, TerrainRule.Impassable),
            (TerrainKind.DeepWater, new TerrainRule(10, Transport.Boat)));

        private readonly TerrainRule[] _rules;

        /// <summary>
        /// Builds a table from one rule per kind. Every kind other than
        /// <see cref="TerrainKind.None"/> must appear exactly once.
        /// </summary>
        public TerrainRules(params (TerrainKind Kind, TerrainRule Rule)[] rules)
        {
            if (rules is null)
            {
                throw new ArgumentNullException(nameof(rules));
            }

            _rules = new TerrainRule[DefinedKinds.Length];
            var seen = new bool[DefinedKinds.Length];
            var cheapest = int.MaxValue;

            foreach (var (kind, rule) in rules)
            {
                if (!EnumGuard.IsDefined(DefinedKinds, (int)kind) || kind == TerrainKind.None)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(rules), kind, "Not a defined TerrainKind.");
                }

                if (seen[(int)kind])
                {
                    throw new ArgumentException(
                        "TerrainKind " + kind + " has two rules.", nameof(rules));
                }

                seen[(int)kind] = true;
                _rules[(int)kind] = rule;

                if (!rule.IsImpassable && rule.Cost < cheapest)
                {
                    cheapest = rule.Cost;
                }
            }

            for (var kind = 1; kind < DefinedKinds.Length; kind++)
            {
                if (DefinedKinds[kind] && !seen[kind])
                {
                    throw new ArgumentException(
                        "No rule for TerrainKind " + (TerrainKind)kind + ".", nameof(rules));
                }
            }

            if (cheapest == int.MaxValue)
            {
                throw new ArgumentException(
                    "Every kind is impassable; nothing could ever move.", nameof(rules));
            }

            CheapestCost = cheapest;
        }

        /// <summary>
        /// The lowest cost of any passable kind, to any transport. The
        /// pathfinder's heuristic scales distance by it, which keeps the
        /// estimate at or below the real cost for every mover.
        /// </summary>
        public int CheapestCost { get; }

        /// <summary>The rule for a kind. Throws for <see cref="TerrainKind.None"/> or an undefined value.</summary>
        public TerrainRule this[TerrainKind kind]
        {
            get
            {
                if (!EnumGuard.IsDefined(DefinedKinds, (int)kind) || kind == TerrainKind.None)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(kind), kind, "Not a defined TerrainKind.");
                }

                return _rules[(int)kind];
            }
        }

        /// <summary>Whether a mover with these transports may enter a cell of this kind.</summary>
        public bool IsPassable(TerrainKind kind, Transport mover) => this[kind].Admits(mover);
    }
}
