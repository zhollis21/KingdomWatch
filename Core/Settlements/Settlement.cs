using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using KingdomWatch.Core.Data;

namespace KingdomWatch.Core.Settlements
{
    /// <summary>
    /// People and stores with a fixed place. What a band becomes when it
    /// settles (#54), and the spatial container section 3 pairs with
    /// <see cref="MobileGroup"/>.
    /// </summary>
    /// <remarks>
    /// Deliberately no more than an <see cref="ICommunity"/> that cannot
    /// move. Section 12's settlement - layout, buildings, housing stock,
    /// territory, a ruler - is M3 and after: the town planner is #23, the
    /// housing stock that regulates household formation is #69, founding and
    /// abandonment with a population floor are #35, and polities are M7.
    /// Until #69, a settlement's households are housed by
    /// <see cref="Lifecycle.CampSpace"/> exactly as the band's were, which
    /// means nothing but food bounds its population; that is the gap #69
    /// names, not a decision made here.
    ///
    /// A plain class, for the reason <see cref="MobileGroup"/> is one: there
    /// will be a few dozen of these, not thousands.
    /// </remarks>
    public sealed class Settlement : ICommunity
    {
        // A List, never a HashSet or Dictionary, for the reason MobileGroup's
        // is: systems act on members in iteration order.
        private readonly List<PersonHandle> _members = new List<PersonHandle>();

        // Wrapped once, so that iterating allocates nothing and nothing can
        // downcast past AddMember.
        private readonly ReadOnlyCollection<PersonHandle> _membersView;

        public Settlement(EntityId id, WorldPosition position)
        {
            if (id.Kind != EntityKind.Settlement)
            {
                throw new ArgumentException(
                    "A Settlement needs an EntityId of kind Settlement, not " + id.Kind + ".",
                    nameof(id));
            }

            Id = id;
            Position = position;
            _membersView = _members.AsReadOnly();
            SharedSupplies = new ResourceLedger();
        }

        /// <summary>Durable identity, safe to reference from history.</summary>
        public EntityId Id { get; }

        /// <summary>Where the settlement stands. Fixed for life.</summary>
        public WorldPosition Position { get; }

        /// <summary>A settlement never travels.</summary>
        public WorldPosition? Destination => null;

        /// <summary>The settlement's stores.</summary>
        public ResourceLedger SharedSupplies { get; }

        /// <summary>Members in a stable, deterministic order.</summary>
        public IReadOnlyList<PersonHandle> Members => _membersView;

        public void AddMember(PersonHandle member)
        {
            if (member.IsNone)
            {
                throw new ArgumentException(
                    "Cannot add PersonHandle.None to a settlement.", nameof(member));
            }

            if (_members.Contains(member))
            {
                throw new ArgumentException(
                    member + " is already a member of " + Id + ".", nameof(member));
            }

            _members.Add(member);
        }

        public bool RemoveMember(PersonHandle member) => _members.Remove(member);
    }
}
