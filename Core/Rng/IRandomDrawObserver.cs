namespace KingdomWatch.Core.Rng
{
    /// <summary>
    /// Watches every value a <see cref="DeterministicRng"/> hands out, so a
    /// test or the harness can check that two different call sites never
    /// produce the same draw (#57).
    /// </summary>
    /// <remarks>
    /// The backstop, not the main protection. Declaring a
    /// <see cref="RandomSite"/> is what makes a collision a 2^-64 accident
    /// rather than something a bare counter can reach; this catches the
    /// residue, and catches it only along paths that actually execute.
    ///
    /// **Why an observer rather than a build flag.** Core is deliberately
    /// single-targeted so that the harness and the IL2CPP build reach
    /// bit-identical state from one seed - see
    /// <c>Core/KingdomWatch.Core.csproj</c>. Compiling the recorder in or out
    /// would mean the thing being fuzzed is not the thing being shipped,
    /// which is the same hazard wearing a different hat. A null reference
    /// costs one predictable branch per draw and leaves one code path in one
    /// assembly.
    ///
    /// Implementations must not draw, schedule, or touch the world. This runs
    /// inside the draw itself, and anything it did would happen in the harness
    /// and not on device.
    /// </remarks>
    public interface IRandomDrawObserver
    {
        /// <summary>
        /// Called with the finished value of one draw and the call site that
        /// asked for it. <paramref name="value"/> is the raw 64-bit draw,
        /// before any bounding into a range - two sites that agree here agree
        /// on everything downstream.
        /// </summary>
        void Drew(RandomDomain domain, RandomSite site, ulong value);
    }
}
