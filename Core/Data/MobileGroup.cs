using System;
using System.Collections.Generic;

namespace KingdomWatch.Core.Data
{
    /// <summary>
    /// People and supplies in transit, with no fixed place. Covers nomadic
    /// bands, founding parties and armies.
    /// </summary>
    /// <remarks>
    /// The game opens with six wandering bands and no settlements, so this is
    /// the starting state of the entire world rather than a late addition. See
    /// docs/design/kingdom-watch-plan-v7.1.md section 3.
    ///
    /// Spatial membership is separate from social membership: a person can
    /// belong to a household, call a settlement home, and be marching 40 km
    /// away in an army all at once. Membership here is the spatial half only.
    ///
    /// SharedSupplies is part of this entity in section 3 but is deliberately
    /// absent for now - it needs the authoritative resource ledger, and a
    /// placeholder here would become a second resource representation the day
    /// that lands. Tracked by issue #12.
    ///
    /// A plain class rather than dense records behind an accessor layer: the
    /// storage rules in section 5 are aimed at the roughly 1,650 people, and
    /// there are six of these at launch. If it ever needs a store it should
    /// follow PersonStore's pattern (issue #6).
    /// </remarks>
    public sealed class MobileGroup
    {
        // A List, never a HashSet or Dictionary. Systems iterate members and act
        // on them, so iteration order is part of the determinism contract -
        // section 5 forbids acting on the order of an unordered collection.
        private readonly List<PersonHandle> _members = new List<PersonHandle>();

        public MobileGroup(EntityId id, MobileGroupPurpose purpose, WorldPosition position)
        {
            if (id.Kind != EntityKind.MobileGroup)
            {
                throw new ArgumentException(
                    "A MobileGroup needs an EntityId of kind MobileGroup, not " + id.Kind + ".",
                    nameof(id));
            }

            if (purpose == MobileGroupPurpose.None)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(purpose), purpose, "A MobileGroup needs a real purpose.");
            }

            Id = id;
            Purpose = purpose;
            Position = position;
        }

        /// <summary>Durable identity, safe to reference from history.</summary>
        public EntityId Id { get; }

        public MobileGroupPurpose Purpose { get; }

        public WorldPosition Position { get; set; }

        /// <summary>Where the group is heading, or null when it is not travelling.</summary>
        public WorldPosition? Destination { get; set; }

        /// <summary>Who leads. <see cref="PersonHandle.None"/> when leaderless.</summary>
        public PersonHandle Leader { get; set; }

        /// <summary>Members in a stable, deterministic order.</summary>
        public IReadOnlyList<PersonHandle> Members => _members;

        /// <summary>
        /// Adds a member. Throws when the person is already in this group,
        /// because a person present twice breaks the validator rule that every
        /// living person has exactly one current spatial presence, and a silent
        /// no-op would hide the bug that caused it.
        /// </summary>
        public void AddMember(PersonHandle member)
        {
            if (member.IsNone)
            {
                throw new ArgumentException(
                    "Cannot add PersonHandle.None to a group.", nameof(member));
            }

            if (_members.Contains(member))
            {
                throw new ArgumentException(
                    member + " is already a member of " + Id + ".", nameof(member));
            }

            _members.Add(member);
        }

        /// <summary>Removes a member. Returns false when they were not in the group.</summary>
        public bool RemoveMember(PersonHandle member) => _members.Remove(member);
    }
}
