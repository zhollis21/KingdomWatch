using System;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;

namespace KingdomWatch.Harness
{
    /// <summary>
    /// The invariants <see cref="WorldValidator"/> checks. Named rather than
    /// numbered in the report so a seed sweep's output says what broke, not
    /// just that something did.
    /// </summary>
    /// <remarks>
    /// Appended to, never renumbered - a rule's value ends up in recorded
    /// output, and the project's standing rule for persisted enums applies
    /// (see <c>AGENTS.md</c>).
    /// </remarks>
    public enum ValidationRule
    {
        None = 0,

        /// <summary><c>PersonStore.Count</c> disagrees with the occupied slots.</summary>
        PersonCount = 1,

        /// <summary>An occupied slot's handle does not name that slot.</summary>
        PersonHandleSlot = 2,

        /// <summary>A durable id does not resolve back to the handle holding it.</summary>
        PersonIdRoundTrip = 3,

        /// <summary>Two people share a durable id.</summary>
        DuplicateEntityId = 4,

        /// <summary>An identity field holds a value the field cannot mean.</summary>
        PersonFieldUndefined = 5,

        /// <summary>The cached age stage disagrees with the age.</summary>
        AgeStageStale = 6,

        /// <summary>Someone was born after the current instant.</summary>
        BornInFuture = 7,

        /// <summary>A person and their household disagree about membership.</summary>
        HouseholdMembership = 8,

        /// <summary>A household lists someone the store no longer holds.</summary>
        HouseholdMemberMissing = 9,

        /// <summary>Somebody the genealogy is expected to know has no record in it.</summary>
        GenealogyMissing = 10,

        /// <summary>A parent link points at something that is not a person.</summary>
        ParentInvalid = 11,

        /// <summary>Somebody is their own ancestor, by some line of descent.</summary>
        KinshipCycle = 12,

        /// <summary>A scheduled event names a person, community or household that does not resolve.</summary>
        ScheduledTargetMissing = 13,

        /// <summary>A person or a system names a booked event the queue does not hold.</summary>
        PendingEventMissing = 14,

        /// <summary>Someone the store no longer holds still has a work task.</summary>
        TaskOutlivedWorker = 15,

        /// <summary>The job mirror on a record disagrees with the task.</summary>
        JobMirrorStale = 16,

        /// <summary>A community lists someone the store no longer holds.</summary>
        CommunityMemberMissing = 17,

        /// <summary>One person is in two communities at once.</summary>
        DoubleMembership = 18,

        /// <summary>A resource count went below zero.</summary>
        NegativeResource = 19,

        /// <summary>A ledger's flows do not account for its stock.</summary>
        ConservationBroken = 20,
    }

    /// <summary>
    /// One broken invariant: which rule, when, and enough context to find it.
    /// </summary>
    /// <remarks>
    /// Section 5 wants <em>"seed 39274 broke at year 347 because an orphan was
    /// adopted into a household deleted six ticks earlier"</em> rather than a
    /// bare failure, so the subject and the detail are both carried and the
    /// seed is added by whoever ran the sweep.
    /// </remarks>
    public readonly struct ValidationFinding
    {
        public ValidationFinding(ValidationRule rule, SimulationTime at, EntityId subject, string detail)
        {
            // Enum.IsDefined rather than Core's EnumGuard, which is internal
            // to that assembly; this type never leaves the desktop, so the
            // reflection cost does not reach anything that has to be fast or
            // deterministic. The rule still stands: an enum parameter is an
            // int with names, and a cast-in value would print as a number in
            // a report nobody could trace back.
            if (rule == ValidationRule.None || !Enum.IsDefined(typeof(ValidationRule), rule))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(rule), rule, "Not a defined ValidationRule, or None.");
            }

            Rule = rule;
            At = at;
            Subject = subject;
            Detail = detail ?? throw new ArgumentNullException(nameof(detail));
        }

        public ValidationRule Rule { get; }

        public SimulationTime At { get; }

        /// <summary>What the finding is about, or <see cref="EntityId.None"/> for a world-level rule.</summary>
        public EntityId Subject { get; }

        public string Detail { get; }

        public override string ToString() =>
            Rule + " at " + At + (Subject.IsNone ? string.Empty : " for " + Subject) + ": " + Detail;
    }
}
