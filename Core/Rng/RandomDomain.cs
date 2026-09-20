namespace KingdomWatch.Core.Rng
{
    /// <summary>
    /// What a random draw is FOR. Part of every key, and the main thing keeping
    /// unrelated decisions from drawing the same value.
    /// </summary>
    /// <remarks>
    /// **Give each decision type its own value.** Two draws can only collide
    /// when they share a domain AND their remaining key components are
    /// numerically equal in the same positions. Making the domain specific to
    /// the decision - Conception, MarriageProposal, CropFailure, SkillGain -
    /// means two draws can only collide when they are genuinely the same
    /// decision about the same entities, which is exactly when they SHOULD be
    /// the same draw.
    ///
    /// Resist the temptation to reuse a broad domain for a new decision because
    /// it is roughly related. A collision does not crash, fail a test, or break
    /// the cross-platform hash - the world stays perfectly reproducible. It
    /// shows up much later as two things that should be independent moving in
    /// lockstep, which is close to undebuggable from the outside.
    ///
    /// Sharing a domain is nonetheless legitimate - worldgen lays down terrain
    /// and rivers under this one - and that is what <see cref="RandomSite"/>
    /// is for. Every key declares a site as well as a domain, mixed at a fixed
    /// position straight afterwards, so two call sites inside one domain start
    /// from different states. The domain remains the coarse separation and the
    /// one that carries meaning; the site is what makes the guarantee
    /// enforceable rather than advisory (#57).
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
        /// <summary>A person's yearly roll against the life table (#11).</summary>
        Mortality = 4,
        /// <summary>The sex of a newborn (#11).</summary>
        ChildSex = 5,
        /// <summary>Terrain laid down by the placeholder map (#16); #33 subdivides it.</summary>
        WorldGen = 6,
        /// <summary>Which of the equally good camps a band walks to next (#54).</summary>
        Wandering = 7,
        /// <summary>Whether two eligible people marry this year (#54's placeholder for #38).</summary>
        Courtship = 8,
        /// <summary>A starting band's ages and sexes (#54); terrain stays with WorldGen.</summary>
        BandGeneration = 9,
    }
}
