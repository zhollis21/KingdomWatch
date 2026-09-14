using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;

namespace KingdomWatch.Core.Lifecycle
{
    /// <summary>
    /// One or more people sharing a home, food access and wealth. Section 3's
    /// social container: a person belongs to a household and occupies a home
    /// through it, wherever they physically are.
    /// </summary>
    /// <remarks>
    /// A plain class, like <see cref="MobileGroup"/>, rather than a
    /// <see cref="PersonStore"/>-style slot store. There are a few hundred of
    /// these, they hold a list and two ids, and nothing iterates them in a
    /// tight loop - so a recycled-handle store would be machinery for a
    /// cache-miss problem that does not exist. The durable id is enough to
    /// address one and to fail loudly when it has dissolved.
    ///
    /// Membership is edited only through <see cref="Households"/>, which
    /// keeps this list and each member's <see cref="PersonRecord.Household"/>
    /// telling the same story. That is why the mutators are internal.
    ///
    /// No ledger. Section 6 gives the household food ACCESS, not food: meals
    /// draw from the settlement's - today the band's - stock, and the
    /// household decides who eats first (see <see cref="Needs.Hunger"/>).
    /// Accumulated wealth and personal property are #68.
    /// </remarks>
    public sealed class Household
    {
        // A List, never a set. Adoption and the death cascade act on members
        // in order, so iteration order is part of the determinism contract.
        private readonly List<PersonHandle> _members = new List<PersonHandle>();

        // Wrapped once, for the same two reasons MobileGroup wraps its members:
        // handing out the list would let a caller bypass the registry, and
        // wrapping per call would allocate.
        private readonly ReadOnlyCollection<PersonHandle> _membersView;

        internal Household(EntityId id, EntityId home, SimulationTime formedAt)
        {
            Id = id;
            Home = home;
            FormedAt = formedAt;
            _membersView = _members.AsReadOnly();
        }

        /// <summary>Durable identity, kind <see cref="EntityKind.Household"/>.</summary>
        public EntityId Id { get; }

        /// <summary>
        /// The home this household occupies, or <see cref="EntityId.None"/>
        /// while it is housed by camp space - section 15: temporary dwellings
        /// satisfy the housing requirement in nomadic mode. A real home
        /// arrives with the housing stock (#69).
        /// </summary>
        public EntityId Home { get; }

        public SimulationTime FormedAt { get; }

        /// <summary>Members in join order. Never contains the same handle twice.</summary>
        public IReadOnlyList<PersonHandle> Members => _membersView;

        internal void AddMember(PersonHandle member)
        {
            if (_members.Contains(member))
            {
                throw new InvalidOperationException(member + " is already a member of " + Id + ".");
            }

            _members.Add(member);
        }

        internal bool RemoveMember(PersonHandle member) => _members.Remove(member);

        public override string ToString() => Id + " (" + _members.Count + " members)";
    }
}
