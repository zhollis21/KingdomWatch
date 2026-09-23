namespace KingdomWatch.Core.Clock
{
    /// <summary>
    /// What a scheduled event IS. Its numeric value doubles as the priority
    /// that orders two events sharing an instant, phase and primary entity.
    /// </summary>
    /// <remarks>
    /// Kind and priority are one field rather than two because two fields can
    /// disagree: the same kind scheduled with different priorities by different
    /// call sites orders inconsistently, and nothing would catch it. Collapsing
    /// them makes the priority a property of the kind, and puts the whole
    /// ordering rule in one readable list.
    ///
    /// **Give each distinct occurrence its own value**, the same convention
    /// <see cref="Rng.RandomDomain"/> asks for and for a closely related
    /// reason: a kind reused across two unrelated occurrences makes them
    /// indistinguishable to the scheduler, to history and to the validator.
    ///
    /// Threshold crossings are scheduled through this enum like anything else -
    /// there is no separate threshold queue, because a predicted crossing and a
    /// discrete event are both "wake me at T" and differ only in where they
    /// came from. Each specific threshold gets its own kind, added by the
    /// system that predicts it: food depletion and starvation with needs
    /// (#51), pregnancy and birth with the demographic model (#11), age stages
    /// with lifecycle (#9). Adding a single catch-all ThresholdCrossed value
    /// would be the reuse this remark warns against.
    ///
    /// Values are explicit and ordering-significant. Renumbering reorders every
    /// event in every existing world, which rewrites its history. Append.
    ///
    /// The first three below are the scheduled-detail examples section 4
    /// names. More arrive with the systems that need them.
    /// </remarks>
    public enum ScheduledEventKind
    {
        /// <summary>Not a valid kind. Guards against a defaulted field.</summary>
        None = 0,

        /// <summary>
        /// A person's current task has run to its end time: they are home
        /// with the output. Owned by <see cref="Work.Jobs"/>.
        /// </summary>
        TaskCompleted = 1,

        /// <summary>
        /// A household's periodic chance of conceiving comes due. Owned by
        /// <see cref="Lifecycle.Fertility"/>.
        /// </summary>
        BirthCheck = 2,

        /// <summary>A person's low-frequency personal decision pass comes due.</summary>
        SocialDecision = 3,

        /// <summary>
        /// A food holder's daily meal comes due: its members draw rations from
        /// its ledger. Owned by <see cref="Needs.Hunger"/>.
        /// </summary>
        MealDue = 4,
        /// <summary>
        /// A person's age reaches the next stage boundary. Owned by
        /// <see cref="Lifecycle.Aging"/>.
        /// </summary>
        AgeStageDue = 5,
        /// <summary>
        /// A person's yearly roll against the life table comes due, on their
        /// birthday. Owned by <see cref="Lifecycle.Mortality"/>.
        /// </summary>
        MortalityCheck = 6,
        /// <summary>
        /// A missed meal has taken a person's health to zero. Raised by
        /// <see cref="Needs.Hunger"/> at the meal that does it, for the same
        /// instant in the lifecycle phase; answered by
        /// <see cref="Lifecycle.Mortality"/>.
        /// </summary>
        StarvationCritical = 7,
        /// <summary>
        /// A pregnancy reaches term. Owned by <see cref="Lifecycle.Fertility"/>;
        /// the mother's record names the pending one.
        /// </summary>
        BirthDue = 8,
        /// <summary>
        /// A band's work day begins: its free workers pick jobs and set out.
        /// Owned by <see cref="Work.Jobs"/>.
        /// </summary>
        WorkDayDue = 9,
        /// <summary>
        /// A band's council sits at first light: settle, move, or stay.
        /// Owned by <see cref="Nomadic.NomadicBands"/>; the band names the
        /// pending one. Numerically after <see cref="WorkDayDue"/>, which
        /// does not matter: the council sits an hour before dawn.
        /// </summary>
        CouncilDue = 10,
        /// <summary>
        /// A travelling band reaches its destination and makes camp. Owned
        /// by <see cref="Nomadic.NomadicBands"/>; the band names the pending
        /// one.
        /// </summary>
        BandArrival = 11,
        /// <summary>
        /// A community's yearly pairing-off. Owned by
        /// <see cref="Lifecycle.Matchmaking"/>; the community names the
        /// pending one.
        /// </summary>
        CourtshipDue = 12,

        /// <summary>
        /// A community's evening fires: in winter each hearth burns its wood
        /// or goes cold. Owned by <see cref="Needs.Warmth"/>; the community
        /// names the pending one (#53).
        /// </summary>
        WarmthDue = 13,

        /// <summary>
        /// A cold night has taken a person's health to zero. Raised by
        /// <see cref="Needs.Warmth"/> at the evening that does it, for the
        /// same instant in the lifecycle phase; answered by
        /// <see cref="Lifecycle.Mortality"/> - the shape of
        /// <see cref="StarvationCritical"/>, with its own kind so the death
        /// can say why (#53).
        /// </summary>
        ExposureCritical = 14,
    }
}
