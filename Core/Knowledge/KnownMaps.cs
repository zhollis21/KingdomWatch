using System;
using System.Collections.Generic;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Traversal;

namespace KingdomWatch.Core.Knowledge
{
    /// <summary>
    /// What each community knows of the terrain. Section 12 <i>Bounded map
    /// knowledge</i>: the god sees the whole map and the people do not, so
    /// every decision that picks a place chooses among the cells its holder
    /// knows rather than the world.
    /// </summary>
    /// <remarks>
    /// **Keyed by holder id, not held by the community.** There is no polity or
    /// race entity in <c>Core</c> yet, so the holder is whichever entity owns
    /// the knowledge: a wandering band is its own holder, and at founding the
    /// settlement takes the map over the same way it takes over members and
    /// stock. #39 re-keys the holder to the polity, which is a change of key
    /// rather than of shape. A field on <see cref="ICommunity"/> would instead
    /// assert that every community owns its own map, which stops being true
    /// the moment one race has two settlements.
    ///
    /// **Terrain only, so nothing here goes stale.** Terrain is static except
    /// for bridges, so a seen cell stays known forever. What a polity knows
    /// about the <i>actors</i> on that land is a last-seen claim in the section
    /// 11 knowledge tiers (#41) - deliberately a second structure, so the two
    /// cannot drift into each other.
    ///
    /// **One write path.** A mover reveals into its holder's map as it goes.
    /// Nothing carries a map of its own, so there is never a merge to order
    /// and never a question of which write won.
    ///
    /// The maps live in a <see cref="Dictionary{TKey, TValue}"/>, which section
    /// 5 allows so long as nothing acts on its iteration order - nothing here
    /// does, and this class deliberately exposes no way to enumerate holders.
    /// Anything that later needs to walk every holder (a world-state hash, #13)
    /// must sort by key first.
    ///
    /// **Radius is Chebyshev, not Euclidean** - a square, matching the bounding
    /// box <see cref="Pathfinder.TryFindNearest"/> searches within and the
    /// <c>dx/dy</c> scan a band picks its next camp from. One shape for
    /// "within n of here" across the whole simulation is worth more than the
    /// slightly prettier circle.
    /// </remarks>
    public sealed class KnownMaps
    {
        private readonly TerrainGrid _grid;

        // Sized by holder rather than pre-allocated per entity: most entities
        // never hold a map, and a band that never existed should not cost a
        // grid's worth of bytes. One bool per cell rather than a packed bit
        // per cell - CellCount bytes is nothing at this scale, and a
        // ReadOnlySpan<bool> is the shape the pathfinder's masks already take,
        // so scoring can be handed one without unpacking anything.
        private readonly Dictionary<EntityId, bool[]> _maps = new Dictionary<EntityId, bool[]>();

        public KnownMaps(TerrainGrid grid)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        }

        /// <summary>
        /// Whether this holder has a map at all. Refuses
        /// <see cref="EntityId.None"/>, as every entry point here does.
        /// </summary>
        public bool IsTracked(EntityId holder)
        {
            RequireRealHolder(holder);
            return _maps.ContainsKey(holder);
        }

        /// <summary>
        /// Gives a holder an empty map: it knows nothing until something
        /// reveals for it. Throws if it already has one, because a second
        /// <c>Track</c> would silently discard everything the first learned.
        /// </summary>
        public void Track(EntityId holder)
        {
            RequireRealHolder(holder);

            if (_maps.ContainsKey(holder))
            {
                throw new InvalidOperationException(holder + " already has a known map; tracking twice would discard it.");
            }

            _maps.Add(holder, new bool[_grid.CellCount]);
        }

        /// <summary>
        /// Every holder with a map, sorted by durable id, replacing whatever
        /// the list held - for the world hash, which must not read the
        /// dictionary's own order. Allocates only if the list has to grow.
        /// </summary>
        public void CopyHoldersTo(List<EntityId> into)
        {
            if (into is null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Clear();

            foreach (var holder in _maps.Keys)
            {
                into.Add(holder);
            }

            into.Sort();
        }

        /// <summary>
        /// Forgets a holder's map entirely. For an entity that has ceased to
        /// exist - a band that founded a settlement has already handed its map
        /// over with <see cref="HandOver"/>.
        /// </summary>
        public void Untrack(EntityId holder)
        {
            RequireRealHolder(holder);

            if (!_maps.Remove(holder))
            {
                throw new InvalidOperationException(holder + " has no known map to forget.");
            }
        }

        /// <summary>
        /// The holder's map, indexed by grid cell index - the shape
        /// <see cref="Pathfinder.TryFindNearest"/> takes, so a caller fetches
        /// it once and hands it to every search in a scan rather than asking
        /// this class per cell.
        /// </summary>
        public ReadOnlySpan<bool> For(EntityId holder) => RequireMap(holder);

        /// <summary>Whether the holder knows this cell. Off-map is never known.</summary>
        public bool Knows(EntityId holder, WorldPosition at)
        {
            var map = RequireMap(holder);
            return _grid.Contains(at) && map[_grid.IndexOf(at)];
        }

        /// <summary>
        /// Reveals every cell within <paramref name="radius"/> of a point, and
        /// answers how many were new. Cells off the map are skipped rather
        /// than refused, so a mover near an edge reveals what there is.
        /// </summary>
        public int Reveal(EntityId holder, WorldPosition centre, int radius)
        {
            var map = RequireMap(holder);

            if (radius < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(radius), radius, "A radius is not negative.");
            }

            var revealed = 0;

            // Clipped to the grid before looping, not inside it. Iterating
            // -radius..radius would scan the square the caller asked for rather
            // than the part of it that exists: a radius of a million costs four
            // trillion steps on a map of a few hundred cells, and at
            // int.MaxValue the increment wraps past the bound and the loop
            // never ends at all. The arithmetic is done in long because
            // centre +/- radius is exactly what overflows an int.
            var minX = (int)Math.Max(0L, (long)centre.X - radius);
            var maxX = (int)Math.Min(_grid.Width - 1L, (long)centre.X + radius);
            var minY = (int)Math.Max(0L, (long)centre.Y - radius);
            var maxY = (int)Math.Min(_grid.Height - 1L, (long)centre.Y + radius);

            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    // Every cell in range is on the map, so there is nothing
                    // left to reject: a centre far enough off it leaves the
                    // bounds crossed and the loops never run.
                    var index = _grid.IndexOf(new WorldPosition(x, y));

                    if (!map[index])
                    {
                        map[index] = true;
                        revealed++;
                    }
                }
            }

            return revealed;
        }

        /// <summary>
        /// Reveals around every cell of a route. What a mover learns by
        /// walking it: the whole path at once, which is the same set of cells
        /// a stepped agent would reveal one at a time, as section 4's LOD
        /// equivalence requires.
        /// </summary>
        public int RevealAlong(EntityId holder, IReadOnlyList<WorldPosition> route, int radius)
        {
            if (route is null)
            {
                throw new ArgumentNullException(nameof(route));
            }

            // Both checked here rather than left to the loop: an empty route
            // would otherwise report a cheerful zero for a holder with no map,
            // or for a radius Reveal rejects outright - and "it depends how
            // many cells you passed" is the wrong answer to whether an
            // argument is valid.
            RequireMap(holder);

            if (radius < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(radius), radius, "A radius is not negative.");
            }

            var revealed = 0;

            for (var i = 0; i < route.Count; i++)
            {
                revealed += Reveal(holder, route[i], radius);
            }

            return revealed;
        }

        /// <summary>
        /// Moves a map from one holder to another, as founding moves members
        /// and stock. The old holder keeps nothing: it is becoming the new one,
        /// not splitting from it, so a copy would leave two maps that drift.
        /// Secession and joining - which do copy and union - are #39.
        /// </summary>
        public void HandOver(EntityId from, EntityId to)
        {
            RequireRealHolder(to);
            var map = RequireMap(from);

            if (_maps.ContainsKey(to))
            {
                throw new InvalidOperationException(
                    to + " already has a known map; founding hands one over to a new holder, it does not merge.");
            }

            _maps.Add(to, map);
            _maps.Remove(from);
        }

        private bool[] RequireMap(EntityId holder)
        {
            // Before the lookup, so a defaulted id is named as one rather than
            // reported as a holder nobody tracked. The two failures send a
            // reader to opposite places: one to the caller's uninitialised
            // field, the other to a missing Track.
            RequireRealHolder(holder);

            if (!_maps.TryGetValue(holder, out var map))
            {
                throw new InvalidOperationException(holder + " has no known map; it is not tracked by KnownMaps.");
            }

            return map;
        }

        // Every public entry point runs this, directly or through RequireMap:
        // None can never be tracked, so answering "not tracked" for it would
        // conflate an invalid id with a real holder that simply has no map.
        private static void RequireRealHolder(EntityId holder)
        {
            if (holder.IsNone)
            {
                throw new ArgumentException("EntityId.None is not a holder; a map belongs to something.", nameof(holder));
            }
        }
    }
}
