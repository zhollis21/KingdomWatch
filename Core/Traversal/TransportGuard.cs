using System;

namespace KingdomWatch.Core.Traversal
{
    /// <summary>
    /// The flags counterpart of <see cref="EnumGuard"/>: a combination is
    /// defined when every set bit is a declared <see cref="Transport"/> member.
    /// A member mask cannot answer that, because Foot | Boat is a valid
    /// argument that is not itself a declared value.
    /// </summary>
    internal static class TransportGuard
    {
        private static readonly Transport AllBits = CollectBits();

        internal static bool IsDefined(Transport value) => (value & ~AllBits) == 0;

        /// <summary>
        /// Rejects a mover that could not move: no transport at all, or a bit
        /// that names none. Every public entry point taking a mover goes
        /// through here so they all refuse the same things.
        /// </summary>
        internal static void RequireMover(Transport mover)
        {
            if (mover == Transport.None || !IsDefined(mover))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(mover), mover, "A mover needs at least one defined Transport.");
            }
        }

        private static Transport CollectBits()
        {
            var all = Transport.None;

            foreach (Transport member in Enum.GetValues(typeof(Transport)))
            {
                all |= member;
            }

            return all;
        }
    }
}
