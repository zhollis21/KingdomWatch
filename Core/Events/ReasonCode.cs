namespace KingdomWatch.Core.Events
{
    /// <summary>
    /// One contributing reason behind a decision that surfaces in the feed.
    /// A <see cref="DomainEvent"/> carries up to four of these in
    /// <see cref="Reasons"/>.
    /// </summary>
    /// <remarks>
    /// The README's "the game can answer why" is philosophy until the code
    /// computing a decision writes down what it weighed. These are the
    /// vocabulary for that. They are emitted AT the decision site by the code
    /// that ran the utility calculation - never reconstructed afterwards by a
    /// separate explain function, which drifts from the real calculation and
    /// eventually lies to the player. Utility scores themselves are never
    /// exposed; only the reasons.
    ///
    /// Values are explicit and persisted with the event that carries them.
    /// Renumbering silently rewrites why everything in history happened.
    /// Append.
    ///
    /// The codes below are the ones section 5's two worked examples name -
    /// the war declaration and Mira leaving Oakshire. Systems add the codes
    /// they actually emit as they arrive; a code nothing emits is worthless.
    /// See docs/design/kingdom-watch-plan-v7.1.md section 5.
    /// </remarks>
    public enum ReasonCode
    {
        /// <summary>Not a reason. Guards against a defaulted field.</summary>
        None = 0,

        FoodShortage = 1,
        SpouseDied = 2,
        KinLiveThere = 3,
        AcceptsMigrants = 4,
        TradersAttacked = 5,
        RelationsDeteriorated = 6,
        TerritoryClaim = 7,
    }
}
