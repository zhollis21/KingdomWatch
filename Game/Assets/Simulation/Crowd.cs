using System;
using System.Collections.Generic;
using UnityEngine;
using EntityId = KingdomWatch.Core.Data.EntityId;

namespace KingdomWatch.Game
{
    // Soft separation between people as drawn (#25, section 12): nobody
    // blocks anybody, but two figures that would overlap lean apart, and
    // walkers keep to the right of the way they are going, so a band on one
    // route reads as people rather than one doll, and two streams on a road
    // pass each other. Presentation only: it reads where Core puts people and
    // never writes back, so it cannot change what happens, only how it looks.
    //
    // Each figure slot has a base point (where the simulation has them, in
    // cells, rows counting south) and an offset this adds. The offsets are
    // worked out SolvesPerSecond times a real second, section 4's local
    // avoidance cadence, and the drawn offset eases toward them every frame.
    internal sealed class Crowd
    {
        // Two figures closer than this, in cells, lean apart: about a doll's
        // width, so people may brush shoulders but not stand in each other.
        private const float Radius = 0.45f;

        // How far a figure may be leant off its base, in cells: under half
        // a cell, so nobody is drawn somewhere the simulation does not have
        // them.
        private const float MaxLean = 0.4f;

        // How much of an overlap one solve undoes, and how much of the last
        // lean is kept when nothing pushes: a crowd spreads over a few
        // solves, and drifts back once it has thinned.
        private const float Push = 0.5f;
        private const float Keep = 0.85f;

        // How far to the right of their way a walker keeps, in cells.
        private const float Lane = 0.15f;

        // The local avoidance cadence (section 4: 10-30 real Hz), and how
        // fast, in cells a real second, a drawn offset may follow a new one.
        private const float SolvesPerSecond = 20f;
        private const float Follow = 1.5f;

        // Someone at work gives way this much against someone passing:
        // they are swinging at something, and leaning them off it would
        // swing at air.
        private const float AtWork = 0.3f;

        // Setting to work or off from it, someone's base point can jump: a
        // field hand works a spot of their own, a woodcutter stands back
        // from the tree, and the walk starts from the cell itself. A jump up
        // to this many cells (a builder at the far side of a site, a step
        // back) is carried into the drawn offset and eased out, so they step
        // across rather than vanish and reappear; a longer one, and what a
        // fast clock piles up past that, is drawn as it is.
        private const float MaxCarry = 5f;

        private readonly int width, height;
        private readonly Func<int, int, bool> standable;

        // Per slot: who is in it, where the simulation has them, which way
        // they walk (zero standing), how readily they give way, the lean the
        // last solve settled on, and the offset drawn.
        private EntityId[] owner = new EntityId[0];
        private Vector2[] at = new Vector2[0], heading = new Vector2[0], lean = new Vector2[0], drawn = new Vector2[0];
        private float[] give = new float[0];

        // The cells' first slot and each slot's next in its cell, for one
        // solve; and the cells written, to clear after.
        private readonly int[] first;
        private int[] next = new int[0];
        private readonly List<int> filled = new List<int>();
        private float sinceSolve = float.MaxValue;

        // standable(x, y): whether a figure may be drawn leant onto that
        // cell. Asked only of cells on the map.
        public Crowd(int width, int height, Func<int, int, bool> standable)
        {
            this.width = width;
            this.height = height;
            this.standable = standable;
            first = new int[width * height];
            for (var i = 0; i < first.Length; i++) first[i] = -1;
        }

        // Where the simulation has someone this frame, by figure slot.
        // `heading` is the step they are taking, zero standing still;
        // `working`, whether they are at work. A slot that has passed to
        // someone else starts with no lean of the last owner's.
        public void Place(int slot, EntityId who, Vector2 cell, Vector2 heading, bool working)
        {
            if (slot >= owner.Length) Grow(slot + 1);
            if (owner[slot] != who)
            {
                owner[slot] = who;
                lean[slot] = Vector2.zero;
                drawn[slot] = Vector2.zero;
            }
            else if ((give[slot] == AtWork) != working)
            {
                var jump = cell - at[slot];
                if (jump.magnitude <= MaxCarry) drawn[slot] = Vector2.ClampMagnitude(drawn[slot] - jump, MaxCarry);
            }
            at[slot] = cell;
            this.heading[slot] = heading.sqrMagnitude > 0f ? heading.normalized : Vector2.zero;
            give[slot] = working ? AtWork : 1f;
        }

        // Where a slot is drawn, after Settle.
        public Vector2 Drawn(int slot) => at[slot] + drawn[slot];

        // Works out the leans for the first `count` slots when a solve is
        // due, then eases every drawn offset toward its lean and lane by
        // `seconds` of real time. Zero seconds (paused) moves nobody.
        public void Settle(int count, float seconds)
        {
            if (seconds <= 0f) return;
            sinceSolve += seconds;
            if (sinceSolve >= 1f / SolvesPerSecond)
            {
                sinceSolve = 0f;
                Solve(count);
            }
            var step = Follow * seconds;
            for (var i = 0; i < count; i++) drawn[i] = Vector2.MoveTowards(drawn[i], Target(i), step);
        }

        // Puts everyone straight onto their lean, as after a jump: used when
        // people come into view, so they do not drift apart in front of you.
        public void Snap(int count)
        {
            Solve(count);
            for (var i = 0; i < count; i++) drawn[i] = Target(i);
            sinceSolve = 0f;
        }

        // The lane: to the walker's right. Rows count south, so turning a
        // heading a quarter clockwise on screen is (x, y) -> (-y, x).
        private Vector2 Target(int i) => lean[i] + new Vector2(-heading[i].y, heading[i].x) * Lane;

        private void Solve(int count)
        {
            for (var i = 0; i < count; i++)
            {
                var p = at[i] + drawn[i];
                var x = Mathf.FloorToInt(p.x);
                var y = Mathf.FloorToInt(p.y);
                if (x < 0 || y < 0 || x >= width || y >= height)
                {
                    next[i] = -2;
                    continue;
                }
                var cell = y * width + x;
                if (first[cell] < 0) filled.Add(cell);
                next[i] = first[cell];
                first[cell] = i;
            }

            for (var i = 0; i < count; i++)
            {
                var p = at[i] + drawn[i];
                var cx = Mathf.FloorToInt(p.x);
                var cy = Mathf.FloorToInt(p.y);
                var push = Vector2.zero;
                for (var y = cy - 1; y <= cy + 1; y++)
                {
                    if (y < 0 || y >= height) continue;
                    for (var x = cx - 1; x <= cx + 1; x++)
                    {
                        if (x < 0 || x >= width) continue;
                        for (var j = first[y * width + x]; j >= 0; j = next[j])
                        {
                            if (j == i) continue;
                            var apart = p - (at[j] + drawn[j]);
                            var distance = apart.magnitude;
                            if (distance >= Radius) continue;
                            // Two on the very same spot part along a way
                            // drawn from both their ids, opposite for each.
                            var away = distance > 1e-4f ? apart / distance : Apart(owner[i], owner[j]);
                            // Of the overlap, the share this one takes: all
                            // of it against someone at work, a little if
                            // they are at work themselves.
                            var share = give[i] / (give[i] + give[j]);
                            push += away * ((Radius - distance) * share);
                        }
                    }
                }

                var leant = Vector2.ClampMagnitude(lean[i] * Keep + push * Push, MaxLean);
                lean[i] = OnStandable(at[i], leant);
            }

            foreach (var cell in filled) first[cell] = -1;
            filled.Clear();
        }

        // A lean that would put someone onto water or into a building from
        // ground they could stand on is halved until it does not; standing
        // on such a cell already (a builder on a site), any lean will do.
        private Vector2 OnStandable(Vector2 cell, Vector2 leant)
        {
            if (!Standable(cell)) return leant;
            for (var tries = 0; tries < 3; tries++)
            {
                if (Standable(cell + leant)) return leant;
                leant *= 0.5f;
            }
            return Vector2.zero;
        }

        private bool Standable(Vector2 p)
        {
            var x = Mathf.FloorToInt(p.x);
            var y = Mathf.FloorToInt(p.y);
            return x >= 0 && y >= 0 && x < width && y < height && standable(x, y);
        }

        // A unit vector from the pair's ids, pointing from b to a: swapping
        // them turns it round, so the two part opposite ways. Not
        // GetHashCode: HashCode.Combine is seeded per process.
        private static Vector2 Apart(EntityId a, EntityId b)
        {
            var low = Math.Min(a.Value, b.Value);
            var high = Math.Max(a.Value, b.Value);
            var angle = (((low * 0x9E3779B97F4A7C15UL) ^ high) * 0xD6E8FEB86659FD93UL >> 40) / (float)(1UL << 24) * 2f * Mathf.PI;
            var way = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            return a.Value < b.Value ? way : -way;
        }

        private void Grow(int needed)
        {
            var size = Math.Max(needed, owner.Length * 2);
            Array.Resize(ref owner, size);
            Array.Resize(ref at, size);
            Array.Resize(ref heading, size);
            Array.Resize(ref lean, size);
            Array.Resize(ref drawn, size);
            Array.Resize(ref give, size);
            Array.Resize(ref next, size);
        }
    }
}
