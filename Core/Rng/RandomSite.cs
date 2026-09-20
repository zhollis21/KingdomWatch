namespace KingdomWatch.Core.Rng
{
    /// <summary>
    /// WHERE a random draw is taken from. Mixed immediately after
    /// <see cref="RandomDomain"/> and before anything else, so that two
    /// different decisions in one domain can never share a key.
    /// </summary>
    /// <remarks>
    /// The domain says what a draw is FOR; this says which of a domain's
    /// draws it is. Two systems can legitimately share a domain - worldgen
    /// lays down terrain and rivers under the same one - and before this
    /// existed they kept apart by mixing a file-local <c>const int</c> of
    /// their own invention. Two files had already done exactly that,
    /// independently, which is how a convention announces that it wants to
    /// be a type.
    ///
    /// **Every call site declares one.** There is no way to start a key
    /// without it: <see cref="DeterministicRng.Key"/> takes a domain and a
    /// site together, and no overload takes a domain alone. A draw that
    /// forgets to say where it comes from does not compile, which is the
    /// enforcement #57 asked for - a check rather than a comment.
    ///
    /// Give a new decision its own value rather than borrowing a neighbour's.
    /// Because the site is mixed at a fixed position, two distinct sites
    /// start from distinct key states, and the remaining components would
    /// have to drive two different states back together to collide - a
    /// 2^-64 accident rather than the reachable one that bare counters made.
    /// Borrowing a value throws that away and puts you back where #57
    /// started.
    ///
    /// Values are explicit and must never be renumbered or reordered, for
    /// the reason <see cref="RandomDomain"/>'s are: renumbering rewrites
    /// every roll derived from that site in every world. Append at the end.
    ///
    /// There is deliberately no None. A domain needs one because a defaulted
    /// field would otherwise look like a real domain; a site is never stored,
    /// only passed, and leaving the guard out means there is no value that
    /// means "unspecified" for a call site to reach for.
    /// </remarks>
    public enum RandomSite
    {
        /// <summary>Which equally good camp a band walks to next (#54).</summary>
        CampChoice = 1,

        /// <summary>A person's roll against the life table at a birthday (#11).</summary>
        LifeTableRoll = 2,

        /// <summary>Whether a household's fertile couple conceives today (#11).</summary>
        ConceptionRoll = 3,

        /// <summary>The sex of a child at term (#11).</summary>
        NewbornSex = 4,

        /// <summary>Whether two eligible people marry this year (#54's placeholder for #38).</summary>
        MarriageRoll = 5,

        /// <summary>A cell's terrain in the placeholder map (#16).</summary>
        Terrain = 6,

        /// <summary>How far the placeholder river wanders at each row (#16).</summary>
        RiverDrift = 7,

        /// <summary>The age of a starting band's founding adults (#54).</summary>
        FounderAge = 8,

        /// <summary>The age of a starting band's children (#54).</summary>
        BandChildAge = 9,

        /// <summary>The sex of a starting band's children (#54).</summary>
        BandChildSex = 10,

        /// <summary>
        /// A battle's result. Declared ahead of the system that will draw it,
        /// as <see cref="RandomDomain.Combat"/> is: the level-of-detail
        /// argument for keyed draws is written against combat, so the tests
        /// that make it need a site to name.
        /// </summary>
        BattleOutcome = 11,
    }
}
