using System;
using System.Globalization;

namespace KingdomWatch.Core.Data
{
    /// <summary>
    /// A positive amount of one resource. The unit a <see cref="Recipe"/> is
    /// written in.
    /// </summary>
    /// <remarks>
    /// Always positive: a recipe line of zero is a line that should not exist,
    /// and a negative one is an input pretending to be an output. Rejecting
    /// both here keeps the ledger's operations from re-checking every line of
    /// every recipe they run. <c>default</c> is therefore not a valid
    /// quantity - its kind is <see cref="ResourceKind.None"/> - and anything
    /// that accepts one from outside has to check for it.
    /// </remarks>
    public readonly struct ResourceQuantity : IEquatable<ResourceQuantity>
    {
        private static readonly bool[] DefinedKinds = EnumGuard.BuildMask(typeof(ResourceKind));

        public ResourceQuantity(ResourceKind kind, int quantity)
        {
            ThrowIfNotAResource(kind, nameof(kind));

            if (quantity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(quantity), quantity, "A resource quantity must be positive.");
            }

            Kind = kind;
            Quantity = quantity;
        }

        public ResourceKind Kind { get; }

        public int Quantity { get; }

        /// <summary>
        /// Rejects <see cref="ResourceKind.None"/> and any integer cast to the
        /// enum that names no member. Shared by every public entry point that
        /// takes a kind, so the check is worded once.
        /// </summary>
        internal static void ThrowIfNotAResource(ResourceKind kind, string paramName)
        {
            if (!EnumGuard.IsDefined(DefinedKinds, (int)kind))
            {
                throw new ArgumentOutOfRangeException(
                    paramName, kind, "Not a defined ResourceKind.");
            }

            if (kind == ResourceKind.None)
            {
                throw new ArgumentOutOfRangeException(
                    paramName, kind, "ResourceKind.None is not a resource.");
            }
        }

        public bool Equals(ResourceQuantity other) =>
            Kind == other.Kind && Quantity == other.Quantity;

        public override bool Equals(object? obj) =>
            obj is ResourceQuantity other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Kind, Quantity);

        public override string ToString() =>
            Kind.ToString() + " x" + Quantity.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(ResourceQuantity left, ResourceQuantity right) =>
            left.Equals(right);

        public static bool operator !=(ResourceQuantity left, ResourceQuantity right) =>
            !left.Equals(right);
    }
}
