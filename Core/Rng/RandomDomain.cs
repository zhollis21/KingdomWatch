namespace KingdomWatch.Core.Rng
{
    /// <summary>
    /// What a random draw is FOR. Part of every key, and the main thing keeping
    /// unrelated decisions from drawing the same value.
    /// </summary>
    /// <remarks>
    /// **Give each decision type its own value.** This is the convention that
    /// does the real work, so it is worth stating plainly: two draws can only
    /// collide when they share a domain AND their remaining key components are
    /// numerically equal in the same positions. Components are folded in as
    /// plain 64-bit numbers, so a bare counter can in principle reproduce
    /// another call site's key. Making the domain specific to the decision -
    /// Conception, MarriageProposal, CropFailure, SkillGain - means two draws
    /// can only collide when they are genuinely the same decision about the
    /// same entities, which is exactly when they SHOULD be the same draw.
    ///
    /// Resist the temptation to reuse a broad domain for a new decision because
    /// it is roughly related. A collision does not crash, fail a test, or break
    /// the cross-platform hash - the world stays perfectly reproducible. It
    /// shows up much later as two things that should be independent moving in
    /// lockstep, which is close to undebuggable from the outside.
    ///
    /// Values are explicit and must never be renumbered or reordered.
    /// Renumbering a domain changes every roll derived from it, which rewrites
    /// the entire history of every existing world. Append new domains at the
    /// end.
    ///
    /// The three below are the ones section 5 names. More arrive with the
    /// systems that need them.
    /// </remarks>
    public enum RandomDomain
    {
        /// <summary>Not a valid domain. Guards against a defaulted field.</summary>
        None = 0,

        Combat = 1,
        Conception = 2,
        Social = 3,
    }
}
