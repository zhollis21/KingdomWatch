using System;
using System.Collections.Generic;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Relationships;
using KingdomWatch.Core.Work;

namespace KingdomWatch.Core.Lifecycle
{
    /// <summary>
    /// The death cascade. Section 6: most of what follows a death is
    /// mechanical and must happen, or it generates bugs - so it happens here,
    /// in one place and one order, whoever decided the person should die.
    /// </summary>
    /// <remarks>
    /// **Deciding a death is not this class's job.** <see cref="Mortality"/>
    /// rolls it and answers starvation; injury (M4) will raise its own. Each
    /// calls <see cref="Die"/>, and the world after the call is consistent: the
    /// event is published, the partnership ended, memories pruned, a
    /// pregnancy cancelled, the task and job vacated, the household
    /// adjusted, the band's roster shortened and the storage slot freed.
    ///
    /// **One synchronous operation, not a chain of reactions.** Section 4's
    /// phase model exists so that REACTIONS to a death - succession, a job
    /// reposted, a grudge formed - land in a later phase rather than running
    /// inside each other. The cascade is not a reaction; it is what the death
    /// IS, and splitting it across phases would leave a dead person in a
    /// household for the length of a phase. Reactions still get their turn:
    /// <see cref="DomainEventKind.PersonDied"/> is published, and subscribers
    /// book what they need into a later phase as usual.
    ///
    /// **Published first, then mutated.** The partnership record names the
    /// event that ended it, so the event has to exist before the record can.
    /// The consequence is that a subscriber hearing PersonDied sees the world
    /// from just before it - the partnership still active, the person still
    /// in the store - which is fine, because subscribers listen and book;
    /// they do not act on state during the publish.
    ///
    /// The relationship stores are called directly rather than subscribing,
    /// as section 6 records: bus subscribers listen, they do not mutate.
    ///
    /// **Tasks and the job are vacated before the person leaves anything**
    /// (#52): the pending completion is cancelled and any inputs in process
    /// return to the band's ledger before they leave the household and the
    /// band, so nothing later in the cascade sees a worker mid-task. Section
    /// 6's "work manager reposts it" happens implicitly rather than as a step:
    /// the next free hand sees the shortfall (see <see cref="Jobs"/>).
    ///
    /// **What is not here, and where it is.** Cancelling reservations (#24),
    /// breaking an apprenticeship and passing on a master's tools (#22), and
    /// folding personal wealth into the household (#68) each join this
    /// cascade when the thing they act on exists. A step is added here, not
    /// subscribed.
    ///
    /// Allocation-free after <see cref="Track"/>: genealogy walks are over
    /// spans, membership changes are list removals, and publishing is the
    /// bus's allocation-free path.
    /// </remarks>
    public sealed class Deaths
    {
        private readonly DomainEventBus _bus;
        private readonly PersonStore _people;
        private readonly Genealogy _genealogy;
        private readonly Partnerships _partnerships;
        private readonly Memories _memories;
        private readonly Households _households;
        private readonly Jobs _jobs;

        // The communities a dead person may need striking from. Nothing on
        // the record says which one holds someone, so the cascade scans the
        // few it is told about.
        private readonly List<ICommunity> _groups = new List<ICommunity>();

        public Deaths(
            DomainEventBus bus,
            PersonStore people,
            Genealogy genealogy,
            Partnerships partnerships,
            Memories memories,
            Households households,
            Jobs jobs)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _people = people ?? throw new ArgumentNullException(nameof(people));
            _genealogy = genealogy ?? throw new ArgumentNullException(nameof(genealogy));
            _partnerships = partnerships ?? throw new ArgumentNullException(nameof(partnerships));
            _memories = memories ?? throw new ArgumentNullException(nameof(memories));
            _households = households ?? throw new ArgumentNullException(nameof(households));
            _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        }

        /// <summary>How many communities the cascade will strike the dead from.</summary>
        public int TrackedCount => _groups.Count;

        /// <summary>Whether this community is tracked here.</summary>
        public bool IsTracked(ICommunity community) =>
            IndexOf((community ?? throw new ArgumentNullException(nameof(community))).Id) >= 0;

        /// <summary>
        /// Registers a community whose members may die. Refuses one already
        /// tracked - a person is in one community, and finding them twice
        /// would mean the same one listed twice.
        /// </summary>
        public void Track(ICommunity group)
        {
            if (group is null)
            {
                throw new ArgumentNullException(nameof(group));
            }

            if (IndexOf(group.Id) >= 0)
            {
                throw new InvalidOperationException(group.Id + " is already tracked.");
            }

            _groups.Add(group);
        }

        /// <summary>
        /// Stops striking the dead from a community: a band that has settled
        /// (#54) hands its people to the settlement, which is tracked in its
        /// place. Throws when it was never tracked, since untracking nothing
        /// is a wiring bug at the caller.
        /// </summary>
        public void Untrack(ICommunity group)
        {
            if (group is null)
            {
                throw new ArgumentNullException(nameof(group));
            }

            var index = IndexOf(group.Id);

            if (index < 0)
            {
                throw new InvalidOperationException(group.Id + " is not tracked by Deaths.");
            }

            _groups.RemoveAt(index);
        }

        private int IndexOf(EntityId community)
        {
            for (var i = 0; i < _groups.Count; i++)
            {
                if (_groups[i].Id == community)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Kills a living person, with the reasons the caller has, and runs
        /// the cascade. Refuses the dead: nobody dies twice, and a stale
        /// handle here is a bug at the caller worth finding.
        /// </summary>
        public void Die(PersonHandle person, Reasons reasons)
        {
            if (!_people.IsAlive(person))
            {
                throw new InvalidOperationException(person + " is not alive; nobody dies twice.");
            }

            var id = _people.GetId(person);
            var now = _bus.Clock.Now;
            var died = _bus.Publish(DomainEventKind.PersonDied, id, EntityId.None, reasons);

            var partner = _partnerships.ActivePartnerOf(id);

            if (!partner.IsNone)
            {
                _partnerships.End(id, partner, died, now);
            }

            _memories.WitnessDied(id);

            EndPregnancy(person);
            EndMortalityCheck(person);
            EndAgeStage(person);
            _jobs.Vacate(person);
            LeaveHousehold(person);
            LeaveGroup(person);

            _people.Remove(person);
        }

        // A pregnancy dies with the mother. The record and the queue name
        // the same event, so both are cleared here, together: Fertility
        // would ignore a birth due for the dead, but a cancelled event is
        // one the queue never has to carry and a save never has to keep.
        private void EndPregnancy(PersonHandle person)
        {
            var due = _people.GetPregnancyDue(person);

            if (due.IsNone)
            {
                return;
            }

            _bus.Clock.Cancel(due);
            _people.SetPregnancyDue(person, EventId.None);
        }

        // The same rule for the yearly roll (#80). Mortality would ignore a
        // check for the dead - nobody's id is ever reused - but the record
        // names this one, so cancelling it is a queue entry saved and, once
        // section 17's saves exist, one less future commitment to keep.
        private void EndMortalityCheck(PersonHandle person)
        {
            var booked = _people.GetPendingMortalityCheck(person);

            if (booked.IsNone)
            {
                return;
            }

            _bus.Clock.Cancel(booked);
            _people.SetPendingMortalityCheck(person, EventId.None);
        }

        // And the same for the next stage boundary. Aging was the one
        // periodic stream #80 missed - it rebooks from inside its own handler
        // like the rest, but discarded the id it booked, so a boundary for
        // someone who had died stayed in the queue until the day it came due
        // and was silently dropped. Harmless to behaviour and a standing
        // breach of section 5's "no scheduled event targets a dead handle",
        // found by the validator's seed sweep (#13) on every seed it ran.
        private void EndAgeStage(PersonHandle person)
        {
            var booked = _people.GetPendingAgeStage(person);

            if (booked.IsNone)
            {
                return;
            }

            _bus.Clock.Cancel(booked);
            _people.SetPendingAgeStage(person, EventId.None);
        }

        // Out of the household; then, if nobody grown is left, the dependents
        // go to kin; then, if nobody at all is left, the household ends and
        // its home goes back. Dependents with no kin to take them keep the
        // household: section 6 says nearest kin adopts and nothing about the
        // case where there is none, and inventing a guardian would be a
        // decision this class has no business making. The validator (#13)
        // can flag a household with no adult; the chronicle can tell it.
        private void LeaveHousehold(PersonHandle person)
        {
            var household = _households.Of(person);

            if (household is null)
            {
                return;
            }

            _households.Leave(person);

            if (household.Members.Count > 0 && !_households.HasAdult(household))
            {
                AdoptOut(household);
            }

            if (household.Members.Count == 0)
            {
                _households.Dissolve(household);
            }
        }

        // Every member is a dependent here - that is what having no adult
        // means. Walked without advancing past a member who moved out, since
        // the list shrinks under the walk.
        private void AdoptOut(Household orphaned)
        {
            var i = 0;

            while (i < orphaned.Members.Count)
            {
                var dependent = orphaned.Members[i];
                var kin = NearestKinHousehold(dependent, orphaned);

                if (kin is null)
                {
                    i++;
                    continue;
                }

                _households.Leave(dependent);
                _households.Join(kin, dependent);
            }
        }

        // Nearest by degree - a surviving parent, then adult siblings,
        // grandparents, aunts and uncles, first cousins - and within a
        // degree the lowest id, so two runs agree on who took the child. A
        // dependent outside the genealogy has no kin to find, and the walk
        // reports none rather than throwing halfway through a death.
        private Household? NearestKinHousehold(PersonHandle dependent, Household orphaned)
        {
            var self = _people.GetId(dependent);

            if (!_genealogy.IsRecorded(self))
            {
                return null;
            }

            var parents = _genealogy.Parents(self);
            var best = new Candidate(orphaned);

            best.Consider(parents.Mother, this);
            best.Consider(parents.Father, this);

            if (best.Found)
            {
                return best.Household;
            }

            ConsiderChildrenOf(parents.Mother, self, ref best);
            ConsiderChildrenOf(parents.Father, self, ref best);

            if (best.Found)
            {
                return best.Household;
            }

            var maternal = ParentsOf(parents.Mother);
            var paternal = ParentsOf(parents.Father);

            best.Consider(maternal.Mother, this);
            best.Consider(maternal.Father, this);
            best.Consider(paternal.Mother, this);
            best.Consider(paternal.Father, this);

            if (best.Found)
            {
                return best.Household;
            }

            ConsiderChildrenOf(maternal.Mother, parents.Mother, ref best);
            ConsiderChildrenOf(maternal.Father, parents.Mother, ref best);
            ConsiderChildrenOf(paternal.Mother, parents.Father, ref best);
            ConsiderChildrenOf(paternal.Father, parents.Father, ref best);

            if (best.Found)
            {
                return best.Household;
            }

            ConsiderGrandchildrenOf(maternal.Mother, parents.Mother, ref best);
            ConsiderGrandchildrenOf(maternal.Father, parents.Mother, ref best);
            ConsiderGrandchildrenOf(paternal.Mother, parents.Father, ref best);
            ConsiderGrandchildrenOf(paternal.Father, parents.Father, ref best);

            return best.Found ? best.Household : null;
        }

        // A None parent is a founder's missing record: no parents, no children.
        private ParentLinks ParentsOf(EntityId person) =>
            person.IsNone ? ParentLinks.None : _genealogy.Parents(person);

        // The children of one person, except one - the dependent themself
        // when looking for siblings, their parent when looking for aunts and
        // uncles.
        private void ConsiderChildrenOf(EntityId parent, EntityId except, ref Candidate best)
        {
            if (parent.IsNone)
            {
                return;
            }

            var children = _genealogy.Children(parent);

            for (var i = 0; i < children.Length; i++)
            {
                if (children[i] != except)
                {
                    best.Consider(children[i], this);
                }
            }
        }

        // First cousins: the children of a grandparent's other children.
        private void ConsiderGrandchildrenOf(EntityId grandparent, EntityId exceptChild, ref Candidate best)
        {
            if (grandparent.IsNone)
            {
                return;
            }

            var children = _genealogy.Children(grandparent);

            for (var i = 0; i < children.Length; i++)
            {
                if (children[i] != exceptChild)
                {
                    ConsiderChildrenOf(children[i], EntityId.None, ref best);
                }
            }
        }

        // The household a living, grown relative lives in, if they are in one
        // other than the orphaned household itself.
        private Household? AdoptiveHouseholdOf(EntityId relative, Household orphaned)
        {
            if (relative.IsNone || !_people.TryGetHandle(relative, out var handle))
            {
                return null;
            }

            if (!AgeStages.IsAdult(_people.GetAgeStage(handle)))
            {
                return null;
            }

            var household = _households.Of(handle);
            return household is null || ReferenceEquals(household, orphaned) ? null : household;
        }

        private void LeaveGroup(PersonHandle person)
        {
            for (var i = 0; i < _groups.Count; i++)
            {
                var group = _groups[i];

                if (!group.RemoveMember(person))
                {
                    continue;
                }

                // Who leads next is the band's (#54) or the polity's (#39)
                // decision; a dead leader is simply no leader. Only a band
                // has one: a settlement's ruler is the polity's, and M7's.
                if (group is MobileGroup band && band.Leader == person)
                {
                    band.Leader = PersonHandle.None;
                }

                return;
            }
        }

        // The best adopter seen so far within one degree: the lowest id among
        // those with a household to offer. A struct passed by ref so the walk
        // allocates nothing.
        private struct Candidate
        {
            private readonly Household _orphaned;
            private EntityId _id;

            public Candidate(Household orphaned)
            {
                _orphaned = orphaned;
                _id = EntityId.None;
                Household = null;
            }

            public Household? Household { get; private set; }

            public bool Found => Household != null;

            public void Consider(EntityId relative, Deaths deaths)
            {
                if (relative.IsNone || (Found && relative >= _id))
                {
                    return;
                }

                var household = deaths.AdoptiveHouseholdOf(relative, _orphaned);

                if (household != null)
                {
                    _id = relative;
                    Household = household;
                }
            }
        }
    }
}
