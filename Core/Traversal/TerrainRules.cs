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
    ///
    /// **Roads (#23).** Each <see cref="RoadGrade"/> has a rule of its own,
    /// and a cell with a road on it costs whichever of its ground's rule and
    /// its road's rule is cheaper among those that admit the mover. So a
    /// road is never slower than the ground it is on, and a grade that
    /// admits a transport its ground does not - a bridge over a river - opens
    /// the cell to it. A grade with no rule given is
    /// <see cref="TerrainRule.Impassable"/>: it adds nothing to its ground.
    /// </remarks>
    public sealed class TerrainRules
    {
        private static readonly bool[] DefinedKinds = EnumGuard.BuildMask(typeof(TerrainKind));

        // Before Default, which reads it: static fields initialise in order.
        private static readonly bool[] DefinedGrades = EnumGuard.BuildMask(typeof(RoadGrade));

        /// <summary>
        /// Placeholder costs: plains are the baseline, scrub is half as slow
        /// again, forest doubles it, rocks triple it; deep water is as easy as
        /// plains for anything that floats; a small river admits nobody until
        /// bridged. A track is a little over half again as quick as plains
        /// on foot.
        /// </summary>
        public static readonly TerrainRules Default = new TerrainRules(
            new[]
            {
                (TerrainKind.Plains, new TerrainRule(10, Transport.Foot)),
                (TerrainKind.Forest, new TerrainRule(20, Transport.Foot)),
                (TerrainKind.Rocks, new TerrainRule(30, Transport.Foot)),
                (TerrainKind.SmallRiver, TerrainRule.Impassable),
                (TerrainKind.DeepWater, new TerrainRule(10, Transport.Boat)),
                (TerrainKind.Scrub, new TerrainRule(15, Transport.Foot)),
            },
            new[]
            {
                (RoadGrade.Track, new TerrainRule(6, Transport.Foot)),
            });

        private readonly TerrainRule[] _rules;
        private readonly TerrainRule[] _roads;

        /// <summary>
        /// Builds a table from one rule per kind. Every kind other than
        /// <see cref="TerrainKind.None"/> must appear exactly once.
        /// </summary>
        public TerrainRules(params (TerrainKind Kind, TerrainRule Rule)[] rules)
            : this(rules, Array.Empty<(RoadGrade, TerrainRule)>())
        {
        }

        /// <summary>
        /// Builds a table from one rule per kind and a rule for any road
        /// grades that should count. Every kind other than
        /// <see cref="TerrainKind.None"/> must appear exactly once; a grade
        /// at most once, and never <see cref="RoadGrade.None"/>.
        /// </summary>
        public TerrainRules((TerrainKind Kind, TerrainRule Rule)[] rules, (RoadGrade Grade, TerrainRule Rule)[] roads)
        {
            if (rules is null)
            {
                throw new ArgumentNullException(nameof(rules));
            }

            if (roads is null)
            {
                throw new ArgumentNullException(nameof(roads));
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

            _roads = new TerrainRule[DefinedGrades.Length];
            var given = new bool[DefinedGrades.Length];

            foreach (var (grade, rule) in roads)
            {
                if (!EnumGuard.IsDefined(DefinedGrades, (int)grade) || grade == RoadGrade.None)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(roads), grade, "Not a defined RoadGrade.");
                }

                if (given[(int)grade])
                {
                    throw new ArgumentException(
                        "RoadGrade " + grade + " has two rules.", nameof(roads));
                }

                given[(int)grade] = true;
                _roads[(int)grade] = rule;

                if (!rule.IsImpassable && rule.Cost < cheapest)
                {
                    cheapest = rule.Cost;
                }
            }

            CheapestCost = cheapest;
        }

        /// <summary>
        /// The lowest cost of any passable kind or road, to any transport. The
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

        /// <summary>
        /// The rule for a road grade: <see cref="TerrainRule.Impassable"/>
        /// for <see cref="RoadGrade.None"/> and for a grade given no rule.
        /// Throws for an undefined value.
        /// </summary>
        public TerrainRule this[RoadGrade grade]
        {
            get
            {
                if (!EnumGuard.IsDefined(DefinedGrades, (int)grade))
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(grade), grade, "Not a defined RoadGrade.");
                }

                return _roads[(int)grade];
            }
        }

        /// <summary>Whether a mover with these transports may enter a cell of this kind with no road on it.</summary>
        public bool IsPassable(TerrainKind kind, Transport mover) => this[kind].Admits(mover);

        /// <summary>
        /// What a mover pays to enter a cell of this kind with this road on
        /// it: the cheaper of the two rules that admit it, or zero when
        /// neither does. The pathfinder's inner loop, so no guards; the
        /// mover is checked once per query.
        /// </summary>
        internal int EntryCost(TerrainKind kind, RoadGrade road, Transport mover)
        {
            var ground = _rules[(int)kind];
            var cost = (ground.Allowed & mover) != 0 ? ground.Cost : 0;

            if (road != RoadGrade.None)
            {
                var way = _roads[(int)road];

                if ((way.Allowed & mover) != 0 && (cost == 0 || way.Cost < cost))
                {
                    cost = way.Cost;
                }
            }

            return cost;
        }
    }
}
