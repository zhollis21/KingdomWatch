using System;
using System.Collections.Generic;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;

namespace KingdomWatch.Core.Relationships
{
    /// <summary>
    /// Who is, and was, partnered with whom. Permanent: a partnership that
    /// ends is marked ended and kept, so a widow's history and a dynasty's
    /// chronicle both still read correctly. See
    /// docs/design/kingdom-watch-plan-v7.1.md section 6.
    /// </summary>
    /// <remarks>
    /// Each record is held in both partners' lists, so <see cref="History"/>
    /// is one lookup and a span. Ending a partnership updates both copies;
    /// the alternative - one list of records plus per-person index lists -
    /// could not hand a person's history out as a span at all.
    ///
    /// A person has at most one active partnership. Remarriage after a death
    /// (section 6) is a second record, not a change to the first.
    ///
    /// The death cascade (#9) calls <see cref="End"/> with its PersonDied
    /// event. This store does not subscribe to the bus itself: subscribers
    /// listen and do not mutate, and the cascade is where the ordering of
    /// what a death touches is decided.
    /// </remarks>
    public sealed class Partnerships
    {
        private readonly Dictionary<EntityId, SpanList<Partnership>> _byPerson =
            new Dictionary<EntityId, SpanList<Partnership>>();

        /// <summary>
        /// Forms a partnership. Refused when either person already has an
        /// active one.
        /// </summary>
        public void Form(EntityId a, EntityId b, EventId formedBy, SimulationTime formedAt)
        {
            RelationshipGuard.RequirePerson(a, nameof(a));
            RelationshipGuard.RequirePerson(b, nameof(b));
            RelationshipGuard.RequireDistinct(a, b, nameof(b));
            RelationshipGuard.RequireEvent(formedBy, nameof(formedBy));
            RequireUnpartnered(a, nameof(a));
            RequireUnpartnered(b, nameof(b));

            var first = a < b ? a : b;
            var second = a < b ? b : a;
            var partnership = new Partnership(
                first, second, formedBy, formedAt, EventId.None, SimulationTime.Zero);

            ListFor(a).Add(partnership);
            ListFor(b).Add(partnership);
        }

        /// <summary>
        /// Ends the active partnership between two people. Refused when they
        /// have none; the record stays, marked with the event that ended it.
        /// </summary>
        public void End(EntityId a, EntityId b, EventId endedBy, SimulationTime endedAt)
        {
            RelationshipGuard.RequirePerson(a, nameof(a));
            RelationshipGuard.RequirePerson(b, nameof(b));
            RelationshipGuard.RequireDistinct(a, b, nameof(b));
            RelationshipGuard.RequireEvent(endedBy, nameof(endedBy));

            var ofA = ActiveIndex(a);

            if (ofA < 0 || !_byPerson[a][ofA].Involves(b))
            {
                throw new InvalidOperationException(
                    a + " and " + b + " have no active partnership to end.");
            }

            ref var record = ref _byPerson[a][ofA];

            if (endedAt < record.FormedAt)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(endedAt),
                    endedAt,
                    "A partnership cannot end before it formed, at " + record.FormedAt + ".");
            }

            var ended = record.Ended(endedBy, endedAt);
            record = ended;
            _byPerson[b][ActiveIndex(b)] = ended;
        }

        /// <summary>
        /// The current partner, or <see cref="EntityId.None"/> for someone
        /// with no active partnership.
        /// </summary>
        public EntityId ActivePartnerOf(EntityId person)
        {
            RelationshipGuard.RequirePerson(person, nameof(person));

            var index = ActiveIndex(person);
            return index < 0 ? EntityId.None : _byPerson[person][index].PartnerOf(person);
        }

        /// <summary>
        /// Every partnership the person has had, oldest first, ended ones
        /// included. Empty for someone never partnered.
        /// </summary>
        public ReadOnlySpan<Partnership> History(EntityId person)
        {
            RelationshipGuard.RequirePerson(person, nameof(person));

            return _byPerson.TryGetValue(person, out var list)
                ? list.AsSpan()
                : ReadOnlySpan<Partnership>.Empty;
        }

        private void RequireUnpartnered(EntityId person, string paramName)
        {
            var active = ActiveIndex(person);

            if (active >= 0)
            {
                throw new InvalidOperationException(
                    person + " is already partnered with " + _byPerson[person][active].PartnerOf(person)
                    + "; that partnership must end first.");
            }
        }

        private SpanList<Partnership> ListFor(EntityId person)
        {
            if (!_byPerson.TryGetValue(person, out var list))
            {
                list = new SpanList<Partnership>(1);
                _byPerson.Add(person, list);
            }

            return list;
        }

        // At most one record per person is active, so the first hit is the
        // only one. Walked from the end because that is where it will be.
        private int ActiveIndex(EntityId person)
        {
            if (!_byPerson.TryGetValue(person, out var list))
            {
                return -1;
            }

            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].IsActive)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
