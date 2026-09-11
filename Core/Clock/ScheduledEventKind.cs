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
    /// The three below are the scheduled-detail examples section 4 names. More
    /// arrive with the systems that need them.
    /// </remarks>
    public enum ScheduledEventKind
    {
        /// <summary>Not a valid kind. Guards against a defaulted field.</summary>
        None = 0,

        /// <summary>A person's current task has run to its end time.</summary>
        TaskCompleted = 1,

        /// <summary>A household's periodic chance of conceiving comes due.</summary>
        BirthCheck = 2,

        /// <summary>A person's low-frequency personal decision pass comes due.</summary>
        SocialDecision = 3,
    }
}
