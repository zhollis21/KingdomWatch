namespace KingdomWatch.Core.Rng
{
    /// <summary>
    /// The SplitMix64 mixing function. Turns a key into a well-distributed
    /// 64-bit value.
    /// </summary>
    /// <remarks>
    /// Shifts, xors and multiplies only. Deliberately no rotate: rotation would
    /// want System.Numerics.BitOperations, which does not exist on
    /// netstandard2.1, and hand-rolling one would be a worse version of an
    /// algorithm that does not need it.
    ///
    /// Every operation here is integer, so the result is bit-identical on the
    /// desktop harness and on Android under IL2CPP. That equivalence is the
    /// whole point - see docs/design/kingdom-watch-plan-v7.1.md section 5.
    ///
    /// Changing this function changes every roll in every world, past and
    /// future. The golden-vector tests exist to make that impossible to do by
    /// accident.
    /// </remarks>
    internal static class SplitMix64
    {
        internal static ulong Mix(ulong value)
        {
            unchecked
            {
                var z = value + 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
    }
}
