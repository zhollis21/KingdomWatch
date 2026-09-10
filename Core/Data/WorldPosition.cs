using System;
using System.Globalization;

namespace KingdomWatch.Core.Data
{
    /// <summary>
    /// An integer position in the world.
    /// </summary>
    /// <remarks>
    /// Integer rather than floating point because the simulation branches on
    /// position, and section 5 requires integer or fixed-point wherever the sim
    /// branches - floating point is not guaranteed bit-identical between the
    /// desktop harness and Android under IL2CPP.
    ///
    /// Routes, distances and traversal are the traversal abstraction's problem
    /// (issue #16), not this type's.
    /// </remarks>
    public readonly struct WorldPosition : IEquatable<WorldPosition>
    {
        public WorldPosition(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }

        public int Y { get; }

        public bool Equals(WorldPosition other) => X == other.X && Y == other.Y;

        public override bool Equals(object? obj) => obj is WorldPosition other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Y);

        public override string ToString() =>
            "(" + X.ToString(CultureInfo.InvariantCulture)
            + ", " + Y.ToString(CultureInfo.InvariantCulture) + ")";

        public static bool operator ==(WorldPosition left, WorldPosition right) => left.Equals(right);

        public static bool operator !=(WorldPosition left, WorldPosition right) => !left.Equals(right);
    }
}
