using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;

namespace KingdomWatch.Core.Lifecycle
{
    /// <summary>
    /// Every household in the world, and the only thing that changes who is
    /// in one. Forms and dissolves households, moves people in and out, and
    /// keeps <see cref="Household.Members"/> and
    /// <see cref="PersonRecord.Household"/> agreeing.
    /// </summary>
    /// <remarks>
    /// Owns the <see cref="IHousing"/>: a household is formed by claiming a
    /// home and dissolved by releasing it, and having one owner for both
    /// halves is what keeps a home from being claimed twice or released
    /// never. Nothing else touches housing.
    ///
    /// A dictionary for lookup by id, plus a list for iteration: the
    /// dictionary's order is not stable, and section 5 forbids acting on the
    /// order of an unordered collection, so anything that walks households
    /// walks <see cref="All"/>.
    ///
    /// Forming allocates - a household is a new entity, like a person - and
    /// so is not the tick-loop path in the sense section 18 measures.
    /// Join, Leave and Dissolve are allocation-free.
    /// </remarks>
    public sealed class Households
    {
        private readonly DomainEventBus _bus;
        private readonly SimulationClock _clock;
        private readonly PersonStore _people;
        private readonly IHousing _housing;

        private readonly Dictionary<EntityId, Household> _byId = new Dictionary<EntityId, Household>();
        private readonly List<Household> _ordered = new List<Household>();
        private readonly ReadOnlyCollection<Household> _orderedView;

        /// <param name="bus">
        /// Where formations and dissolutions are announced, and where the
        /// clock and id allocator come from, for the reason
        /// <see cref="Needs.Hunger"/> takes its bus alone: one source of
        /// "when" and "which".
        /// </param>
        public Households(DomainEventBus bus, PersonStore people, IHousing housing)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _people = people ?? throw new ArgumentNullException(nameof(people));
            _housing = housing ?? throw new ArgumentNullException(nameof(housing));
            _clock = bus.Clock;
            _orderedView = _ordered.AsReadOnly();
        }

        /// <summary>How many households exist.</summary>
        public int Count => _ordered.Count;

        /// <summary>Every household, oldest first. The order to iterate in.</summary>
        public IReadOnlyList<Household> All => _orderedView;

        /// <summary>Whether <see cref="Form"/> would find a home right now.</summary>
        public bool HasVacancy => _housing.HasVacancy;

        /// <summary>
        /// Forms an empty household in a newly claimed home and announces it.
        /// Throws when the housing has no vacancy - ask <see cref="HasVacancy"/>
        /// first, as <see cref="FamilyFormation"/> does.
        /// </summary>
        /// <remarks>
        /// Empty at birth so that the members who then join are recorded
        /// against a household that already exists; the alternative, a Form
        /// that takes founders, would have to be undone if a founder turned
        /// out to be in another household already.
        ///
        /// Announced before recorded, as <see cref="Needs.Hunger"/> announces
        /// a famine before it flips its flag: a refused publish is a wiring
        /// bug the bus throws for, and the registry should not then hold a
        /// household nobody heard of. The home claimed for it is not given
        /// back on that path - by then the world has thrown out of its tick
        /// and is being discarded, and unwinding housing for a bug would be a
        /// transaction layer this simulation does not have; what counts as a
        /// resumable state is the snapshot's (#15).
        /// </remarks>
        public Household Form()
        {
            if (!_housing.HasVacancy)
            {
                throw new InvalidOperationException("No home is available for a new household.");
            }

            var home = _housing.Claim();
            var household = new Household(_clock.Ids.Next(EntityKind.Household), home, _clock.Now);

            _bus.Publish(DomainEventKind.HouseholdFormed, household.Id, home);
            _byId.Add(household.Id, household);
            _ordered.Add(household);

            return household;
        }

        /// <summary>
        /// Ends an empty household and returns its home. Refuses one with
        /// members: where they go is a decision - adoption, a move - that
        /// belongs to the caller, and dissolving under them would leave people
        /// pointing at a household that is gone.
        /// </summary>
        public void Dissolve(Household household)
        {
            RequireKnown(household);

            if (household.Members.Count > 0)
            {
                throw new InvalidOperationException(
                    household + " still has members; move them out before dissolving it.");
            }

            // Announced first, for the reason Form is: a refused publish
            // leaves the household exactly as it was.
            _bus.Publish(DomainEventKind.HouseholdDissolved, household.Id, household.Home);
            _byId.Remove(household.Id);
            _ordered.Remove(household);
            _housing.Release(household.Home);
        }

        /// <summary>
        /// Adds a person to a household. Refuses someone already in one -
        /// <see cref="Leave"/> first - so that a person is in at most one, and
        /// membership never has to be reconciled.
        /// </summary>
        public void Join(Household household, PersonHandle person)
        {
            RequireKnown(household);

            var current = _people.GetHousehold(person);

            if (!current.IsNone)
            {
                throw new InvalidOperationException(
                    person + " is already in " + current + "; they leave that before joining another.");
            }

            household.AddMember(person);
            _people.SetHousehold(person, household.Id);
        }

        /// <summary>
        /// Removes a person from their household, leaving it standing however
        /// few remain. Dissolving an emptied household is the caller's
        /// explicit step. Refuses someone in no household.
        /// </summary>
        public void Leave(PersonHandle person)
        {
            var household = Of(person);

            if (household is null)
            {
                throw new InvalidOperationException(person + " is not in a household.");
            }

            household.RemoveMember(person);
            _people.SetHousehold(person, EntityId.None);
        }

        /// <summary>The household a person is in, or null for someone in none.</summary>
        public Household? Of(PersonHandle person)
        {
            var id = _people.GetHousehold(person);
            return id.IsNone ? null : _byId[id];
        }

        /// <summary>
        /// Finds a household by id. False for one that has dissolved or never
        /// existed - the ids in history keep naming households after they end.
        /// </summary>
        public bool TryGet(EntityId id, out Household household) => _byId.TryGetValue(id, out household);

        /// <summary>
        /// Whether anyone in the household is an adult - the question that
        /// decides whether its dependents need adopting.
        /// </summary>
        public bool HasAdult(Household household)
        {
            RequireKnown(household);

            var members = household.Members;

            for (var i = 0; i < members.Count; i++)
            {
                if (AgeStages.IsAdult(_people.GetAgeStage(members[i])))
                {
                    return true;
                }
            }

            return false;
        }

        private void RequireKnown(Household household)
        {
            if (household is null)
            {
                throw new ArgumentNullException(nameof(household));
            }

            if (!_byId.TryGetValue(household.Id, out var known) || !ReferenceEquals(known, household))
            {
                throw new InvalidOperationException(household + " is not a household this registry knows.");
            }
        }
    }
}
