namespace KingdomWatch.Core.Events
{
    /// <summary>
    /// What a <see cref="DomainEvent"/> IS: something meaningful that happened
    /// in the world, which other systems may want to react to.
    /// </summary>
    /// <remarks>
    /// This is a domain-event vocabulary rather than event sourcing. Not every
    /// axe swing becomes an event; a death does, because it touches households,
    /// job assignment, inheritance, succession, the feed and history at once.
    /// Contrast <see cref="Clock.ScheduledEventKind"/>, which names a future
    /// wake-up rather than a fact - a BirthCheck that fails to conceive is a
    /// scheduled event and no domain event at all.
    ///
    /// **Give each distinct occurrence its own value**, the convention
    /// <see cref="Clock.ScheduledEventKind"/> and <see cref="Rng.RandomDomain"/>
    /// share: a kind reused across two unrelated occurrences makes them
    /// indistinguishable to subscribers, to history and to the validator.
    ///
    /// Values are explicit and persisted - the journal stores them, and saves
    /// will. Renumbering silently relabels every event in every existing
    /// world's history. Append.
    ///
    /// The twelve below are section 5's own list. More arrive with the systems
    /// that publish them. See docs/design/kingdom-watch-plan-v7.1.md section 5.
    /// </remarks>
    public enum DomainEventKind
    {
        /// <summary>Not a valid kind. Guards against a defaulted field.</summary>
        None = 0,

        PersonBorn = 1,
        PersonDied = 2,
        MarriageFormed = 3,
        HouseholdFormed = 4,
        SettlementFounded = 5,
        SettlementAbandoned = 6,
        RulerSucceeded = 7,
        WarDeclared = 8,
        BattleEnded = 9,
        DivineActWitnessed = 10,
        BridgeDestroyed = 11,
        FamineStarted = 12,
    }
}
