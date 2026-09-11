namespace KingdomWatch.Core.Clock
{
    /// <summary>
    /// The stage of a single instant that an event belongs to. Reactions flow
    /// forward through the phases, which is what keeps a death cascade from
    /// becoming recursion.
    /// </summary>
    /// <remarks>
    /// A single death touches households, inheritance, job assignment,
    /// succession, relationships, the event feed and history. Letting each
    /// subscriber call the next turns that into callback spaghetti with an
    /// ordering nobody controls. Phases replace nesting with sequence:
    /// everything physical settles, then lifecycle, then the household and
    /// social reactions to it, and so on.
    ///
    /// Section 4 says the exact phase list can evolve. The rule that must not
    /// is that there is no reentrant event handling with arbitrary subscriber
    /// order - see <see cref="SimulationClock"/>, which enforces it by refusing
    /// to schedule at or before the position it is currently dispatching.
    ///
    /// Values are explicit and ordering-significant. Renumbering them reorders
    /// every event in every existing world, which rewrites its history; append
    /// rather than renumber, exactly as with
    /// <see cref="Rng.RandomDomain"/>. See
    /// docs/design/kingdom-watch-plan-v7.1.md section 4.
    /// </remarks>
    public enum SimulationPhase
    {
        /// <summary>Not a valid phase. Guards against a defaulted field.</summary>
        None = 0,

        /// <summary>Physical and resource changes.</summary>
        Physical = 1,

        /// <summary>Birth, ageing, injury, death.</summary>
        Lifecycle = 2,

        /// <summary>Household and social reactions to the above.</summary>
        HouseholdAndSocial = 3,

        /// <summary>Political reactions.</summary>
        Political = 4,

        /// <summary>Derived state and history notifications.</summary>
        Derived = 5,
    }
}
