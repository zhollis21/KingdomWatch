using System.Collections.Generic;

namespace KingdomWatch.Core.Data
{
    /// <summary>
    /// People who live together and share a ledger: section 3's current
    /// spatial container, which is a <see cref="MobileGroup"/> or a
    /// <see cref="Settlements.Settlement"/>. What the systems that feed,
    /// work, breed and bury a population track, so that a band settling
    /// (#54) is a handover between two of these rather than four systems
    /// learning a second type.
    /// </summary>
    /// <remarks>
    /// Membership here is spatial only - a person also belongs to a
    /// household, and will one day call a settlement home from an army 40 km
    /// away. The validator rule is that every living person has exactly one
    /// of these at a time.
    ///
    /// <see cref="Members"/> is ordered, and the order is part of the
    /// determinism contract: everything that walks a community walks it in
    /// this order and acts as it goes. Implementations wrap the list once,
    /// so that iterating allocates nothing and nothing can downcast and
    /// mutate past <see cref="AddMember"/>.
    ///
    /// <see cref="Destination"/> is on the interface, null for anything that
    /// cannot travel, so that <see cref="Work.Jobs"/> can tell a community on
    /// the road from one at rest without knowing which kind it has.
    /// </remarks>
    public interface ICommunity
    {
        /// <summary>Durable identity, safe to reference from history.</summary>
        EntityId Id { get; }

        /// <summary>Where the community is.</summary>
        WorldPosition Position { get; }

        /// <summary>Where it is heading, or null when it is not travelling.</summary>
        WorldPosition? Destination { get; }

        /// <summary>Members in a stable, deterministic order.</summary>
        IReadOnlyList<PersonHandle> Members { get; }

        /// <summary>Food, tools and materials held in common.</summary>
        ResourceLedger SharedSupplies { get; }

        /// <summary>
        /// Adds a member. Throws when the person is already here: present
        /// twice breaks the one-presence rule, and a silent no-op would hide
        /// the bug that caused it.
        /// </summary>
        void AddMember(PersonHandle member);

        /// <summary>Removes a member. Returns false when they were not here.</summary>
        bool RemoveMember(PersonHandle member);
    }
}
