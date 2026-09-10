namespace KingdomWatch.Core.Data
{
    /// <summary>
    /// The kind of entity a durable <see cref="EntityId"/> refers to.
    /// </summary>
    /// <remarks>
    /// Values are explicit and must never be renumbered or reordered. They are
    /// written into saves and history entries, so changing one silently
    /// repoints every existing world's durable references at the wrong kind of
    /// thing. Append new kinds at the end.
    /// </remarks>
    public enum EntityKind
    {
        /// <summary>No entity. The kind of a default <see cref="EntityId"/>.</summary>
        None = 0,

        Person = 1,
        Household = 2,
        Settlement = 3,
        Polity = 4,
        Dynasty = 5,
        MobileGroup = 6,
        Animal = 7,
    }
}
