using System;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Relationships;

namespace KingdomWatch.Core.Lifecycle
{
    /// <summary>
    /// Who may partner, and what happens when they do. Section 6's family
    /// formation: eligibility over age, sex, existing partnerships, mourning,
    /// kinship and housing; then a partnership, a new household in a new
    /// home, and the couple's dependent children moving in with them.
    /// </summary>
    /// <remarks>
    /// This is the rulebook, not the matchmaker. Choosing WHO pairs off is a
    /// social decision (#38) and draws on the RNG; nothing here does. What
    /// this owns is the answer to "may these two?" and the mechanics of
    /// "they do", so the decision system has one thing to ask and one thing
    /// to call.
    ///
    /// **The kinship ban** covers everything <see cref="Genealogy.Kinship"/>
    /// can name short of first cousins: parent and child, siblings (half
    /// included, since sharing a parent is what sibling means there),
    /// grandparent and grandchild, and aunt or uncle with niece or nephew.
    /// Section 6 lists the ban as "through grandparents" without naming the
    /// last pair; they are closer than the cousins it makes a taboo, so
    /// leaving them out would permit what the taboo refuses. First cousins
    /// are a setting (<see cref="FamilyFormationSettings.FirstCousinsPermitted"/>)
    /// until culture exists to drift it.
    ///
    /// Section 6 also names race fertility, settlement distance and the
    /// relationship between the two as inputs. None is checked, because none
    /// exists: races are #34, settlements #54, and the social tie is the
    /// decision system's to weigh before it asks (#38).
    ///
    /// Both people must be in the <see cref="Genealogy"/>; a person outside
    /// it cannot be checked for kinship and the genealogy throws rather than
    /// guess. Worldgen records everyone, founders with no parents.
    /// </remarks>
    public sealed class FamilyFormation
    {
        private readonly DomainEventBus _bus;
        private readonly PersonStore _people;
        private readonly Genealogy _genealogy;
        private readonly Partnerships _partnerships;
        private readonly Households _households;
        private readonly FamilyFormationSettings _settings;

        public FamilyFormation(
            DomainEventBus bus,
            PersonStore people,
            Genealogy genealogy,
            Partnerships partnerships,
            Households households,
            FamilyFormationSettings settings)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _people = people ?? throw new ArgumentNullException(nameof(people));
            _genealogy = genealogy ?? throw new ArgumentNullException(nameof(genealogy));
            _partnerships = partnerships ?? throw new ArgumentNullException(nameof(partnerships));
            _households = households ?? throw new ArgumentNullException(nameof(households));
            _settings = settings;
        }

        public FamilyFormationSettings Settings => _settings;

        /// <summary>
        /// Whether two people may partner right now, and if not, the first
        /// reason why. Changes nothing.
        /// </summary>
        public PartnerRefusal Evaluate(PersonHandle a, PersonHandle b)
        {
            if (a == b)
            {
                return PartnerRefusal.SamePerson;
            }

            if (!AgeStages.IsAdult(_people.GetAgeStage(a)) || !AgeStages.IsAdult(_people.GetAgeStage(b)))
            {
                return PartnerRefusal.NotAdult;
            }

            if (_people.GetSex(a) == _people.GetSex(b))
            {
                return PartnerRefusal.SameSex;
            }

            var idA = _people.GetId(a);
            var idB = _people.GetId(b);

            if (!_partnerships.ActivePartnerOf(idA).IsNone || !_partnerships.ActivePartnerOf(idB).IsNone)
            {
                return PartnerRefusal.AlreadyPartnered;
            }

            if (IsMourning(idA) || IsMourning(idB))
            {
                return PartnerRefusal.Mourning;
            }

            switch (_genealogy.Kinship(idA, idB))
            {
                case KinshipDegree.ParentChild:
                case KinshipDegree.Sibling:
                case KinshipDegree.Grandparent:
                case KinshipDegree.AuntUncle:
                    return PartnerRefusal.KinshipBanned;

                case KinshipDegree.FirstCousin when !_settings.FirstCousinsPermitted:
                    return PartnerRefusal.CousinTaboo;
            }

            if (!_households.HasVacancy)
            {
                return PartnerRefusal.NoHomeAvailable;
            }

            return PartnerRefusal.None;
        }

        /// <summary>
        /// Partners two eligible people: announces the marriage with the
        /// caller's reasons, records the partnership against that event,
        /// forms a household in a new home, and moves both in along with
        /// any dependent children of theirs from the households they leave.
        /// A household left empty is dissolved. Throws when
        /// <see cref="Evaluate"/> would refuse.
        /// </summary>
        /// <remarks>
        /// Announced first, for the reason the death cascade announces first:
        /// the partnership is recorded as formed BY the event, so the event
        /// has to exist before the record can name it. Subscribers hearing
        /// the marriage see the world from just before it - they listen and
        /// book reactions into a later phase, so nothing they can do depends
        /// on the household already standing.
        /// </remarks>
        public Household Partner(PersonHandle a, PersonHandle b, Reasons reasons)
        {
            var refusal = Evaluate(a, b);

            if (refusal != PartnerRefusal.None)
            {
                throw new InvalidOperationException(
                    a + " and " + b + " may not partner: " + refusal + ".");
            }

            var idA = _people.GetId(a);
            var idB = _people.GetId(b);
            var formedBy = _bus.Publish(DomainEventKind.MarriageFormed, idA, idB, reasons);

            _partnerships.Form(idA, idB, formedBy, _bus.Clock.Now);

            var household = _households.Form();
            MoveIn(a, household);
            MoveIn(b, household);

            return household;
        }

        private bool IsMourning(EntityId person)
        {
            var history = _partnerships.History(person);

            if (history.Length == 0)
            {
                return false;
            }

            // The last record is the latest ending: Evaluate has already
            // established that none is active, and Partnerships keeps history
            // in time order.
            var endedAt = history[history.Length - 1].EndedAt;
            return endedAt.TicksUntil(_bus.Clock.Now) < _settings.MourningTicks;
        }

        // Leaves the old household, joins the new one, and brings along any
        // of this person's own children who are dependents in the old
        // household. Children of the couple appear twice, once per parent;
        // the second pass finds them already moved and leaves them be.
        private void MoveIn(PersonHandle person, Household household)
        {
            var previous = _households.Of(person);

            if (previous is null)
            {
                _households.Join(household, person);
                return;
            }

            _households.Leave(person);
            _households.Join(household, person);

            var children = _genealogy.Children(_people.GetId(person));

            for (var i = 0; i < children.Length; i++)
            {
                if (!_people.TryGetHandle(children[i], out var child)
                    || _people.GetHousehold(child) != previous.Id
                    || !AgeStages.IsDependent(_people.GetAgeStage(child)))
                {
                    continue;
                }

                _households.Leave(child);
                _households.Join(household, child);
            }

            if (previous.Members.Count == 0)
            {
                _households.Dissolve(previous);
            }
        }
    }
}
