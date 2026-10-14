using System;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Needs;
using KingdomWatch.Core.Traversal;
using KingdomWatch.Core.Work;

namespace KingdomWatch.Core.Construction
{
    /// <summary>
    /// Foresters (#155): when a settlement plants trees, and where.
    /// </summary>
    /// <remarks>
    /// The forest in reach caps how many hearths a village keeps
    /// (<see cref="CanFuelAnotherHearth"/>), and building on forest clears
    /// it. A Forester plants a tree on a cell of plains
    /// (<see cref="Land.LandCover.Plant"/>), which is wood in reach once it
    /// has grown.
    ///
    /// **When.** A settlement wants Foresters while the forest in reach,
    /// counting the trees it planted that are still growing, gives less
    /// than twice what a new house waits on: the hearths it has and one more
    /// burning through a winter, with <see cref="PlantHeadroomNumerator"/>/<see cref="PlantHeadroomDenominator"/>
    /// headroom where a house has <see cref="WoodHeadroomNumerator"/>/<see cref="WoodHeadroomDenominator"/>.
    /// Nothing grows back on its own, and a tree takes five years to count
    /// toward a house, so planting runs ahead of a growing village rather
    /// than behind it.
    ///
    /// **Where.** The nearest plains cell a Forester can reach within
    /// <see cref="WoodReach"/>: one beside forest first, so a wood grows out
    /// from its edge, and any other only when none is. Never on a road or a
    /// pending lane, under a building or its roof, or in any settlement's
    /// town: the square <see cref="StreetReach"/> out from its centre each
    /// way, to the ends of its planned spokes, so saplings never crowd its
    /// streets and houses. That is past <see cref="WoodFullReach"/>, so a
    /// planted tree mostly counts half (<see cref="WoodPerPlantingAt"/>), as
    /// any tree that far out does, and a village plants about twice as many.
    ///
    /// The numbers are placeholders.
    /// </remarks>
    public sealed partial class Buildings
    {
        /// <summary>Worker-ticks to plant one tree: a Forester's work at the site.</summary>
        public const long PlantTicks = Hour;

        /// <summary>
        /// Over <see cref="PlantHeadroomDenominator"/>: the margin Foresters
        /// plant to over what a settlement's hearths and one more burn -
        /// twice the house limit's.
        /// </summary>
        public const long PlantHeadroomNumerator = 2L * WoodHeadroomNumerator;

        /// <summary>See <see cref="PlantHeadroomNumerator"/>.</summary>
        public const long PlantHeadroomDenominator = WoodHeadroomDenominator;

        /// <summary>
        /// Wood a year one planted tree gives once grown, counted whole: what
        /// a Forester out planting is expected to add.
        /// </summary>
        public long WoodPerPlanting => WoodFromTrees(TreeWeight.Whole) / TreeWeight.Whole;

        /// <summary>
        /// <see cref="WoodPerPlanting"/> for a tree this far out, in route
        /// cost: whole out to <see cref="WoodFullReach"/>, half out to
        /// <see cref="WoodReach"/>, as the forest in reach counts it, and
        /// nothing past it.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The cost is negative.</exception>
        public long WoodPerPlantingAt(long cost) =>
            cost >= 0L
                ? WoodFromTrees(Trees.WeightOf(cost)) / TreeWeight.Whole
                : throw new ArgumentOutOfRangeException(nameof(cost), cost, "A walk costs no less than nothing.");

        /// <summary>
        /// Cells a Forester may plant: open plains under nothing, off every
        /// road, lane, yard and planned street. Use <see cref="BesideForest"/>
        /// for the cells of these that touch forest.
        /// </summary>
        public ISiteFilter Plantable => _plantable;

        /// <summary><see cref="Plantable"/> cells with forest in one of their eight neighbours.</summary>
        public ISiteFilter BesideForest => _besideForest;

        /// <summary>
        /// Wood a year the forest in a settlement's reach will give once
        /// every tree it planted has grown: <see cref="WoodInReach(ICommunity)"/>
        /// with the saplings counted. What Foresters plant toward.
        /// </summary>
        public long WoodPlantedInReach(ICommunity settlement)
        {
            if (settlement is null)
            {
                throw new ArgumentNullException(nameof(settlement));
            }

            return WoodFromTrees(Weighed(_plantedWoodCounts, settlement.Position, settlement.Id, _forest, WoodReach, Trees, null)) / TreeWeight.Whole;
        }

        /// <summary>
        /// How short of Foresters a settlement is, in wood a year, against a
        /// target of twice what its hearths and one more burn in a winter
        /// with the building headroom (#155): what <see cref="Jobs"/> ranks a Forester
        /// by. <paramref name="hearths"/> is the dawn count. A Forester's tree
        /// goes in the ground as the trip sets out, so those already out are
        /// counted in the forest rather than on their way.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The hearths are negative.</exception>
        public bool TryPlantWork(ICommunity settlement, int hearths, out long shortBy, out long target)
        {
            if (settlement is null)
            {
                throw new ArgumentNullException(nameof(settlement));
            }

            if (hearths < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(hearths), hearths, "No fewer hearths than none.");
            }

            var burn = (hearths + 1L) * Warmth.FuelPerFire * SimulationTime.DaysPerSeason;
            target = burn * PlantHeadroomNumerator;

            // The grown forest is the house limit's count, already swept, and
            // never more than the planted: when it is enough, so is the other.
            shortBy = target - (WoodInReach(settlement) * PlantHeadroomDenominator);

            if (shortBy > 0L)
            {
                shortBy = target - (WoodPlantedInReach(settlement) * PlantHeadroomDenominator);
            }

            if (shortBy <= 0L)
            {
                shortBy = 0L;
                return false;
            }

            return true;
        }

        // Plains a tree may go on: under nothing, off the network and every
        // pending lane, and outside every town - the square out to the ends
        // of its spokes (StreetReach), yard and planned streets inside it -
        // so a wood never grows up between the houses.
        private bool IsPlantable(int cell)
        {
            if (_grid.KindAt(cell) != TerrainKind.Plains
                || _grid.RoadAt(cell) != RoadGrade.None
                || _onCell.ContainsKey(cell)
                || _underRoof.Contains(cell)
                || _laneOf.ContainsKey(cell))
            {
                return false;
            }

            var at = _grid.PositionAt(cell);

            for (var i = 0; i < _camps.Count; i++)
            {
                var camp = _camps[i].Position;

                if (Math.Abs(at.X - camp.X) <= StreetReach && Math.Abs(at.Y - camp.Y) <= StreetReach)
                {
                    return false;
                }
            }

            return true;
        }

        private bool TouchesForest(int cell)
        {
            var at = _grid.PositionAt(cell);

            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    var beside = new WorldPosition(at.X + dx, at.Y + dy);

                    if ((dx != 0 || dy != 0) && _grid.Contains(beside) && _grid[beside] == TerrainKind.Forest)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private sealed class PlantableFilter : ISiteFilter
        {
            private readonly Buildings _owner;
            private readonly bool _besideForest;

            public PlantableFilter(Buildings owner, bool besideForest)
            {
                _owner = owner;
                _besideForest = besideForest;
            }

            public bool Accepts(int cell) => _owner.IsPlantable(cell) && (!_besideForest || _owner.TouchesForest(cell));
        }
    }
}
