using System;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.History;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.History
{
    [TestFixture]
    public sealed class EventJournalTests
    {
        private static readonly SimulationTime Noon = SimulationTime.FromHours(12L);
        private static readonly SimulationTime Dusk = SimulationTime.FromHours(18L);

        private static EntityId Person(ulong value) => new EntityId(EntityKind.Person, value);

        private static DomainEvent Event(ulong id, SimulationTime time, DomainEventKind kind = DomainEventKind.PersonBorn) =>
            new DomainEvent(new EventId(id), time, kind, Person(id), EntityId.None, Reasons.None);

        [Test]
        public void A_journal_needs_room_for_at_least_one_event()
        {
            Assert.Multiple(() =>
            {
                Assert.That(() => new EventJournal(0), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => new EventJournal(-1), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(new EventJournal(1).Count, Is.Zero);
            });
        }

        [Test]
        public void Events_are_kept_in_the_order_they_were_published()
        {
            var journal = new EventJournal(4);
            journal.On(Event(1UL, Noon, DomainEventKind.PersonBorn));
            journal.On(Event(2UL, Noon, DomainEventKind.PersonDied));
            journal.On(Event(3UL, Dusk, DomainEventKind.MarriageFormed));

            Assert.Multiple(() =>
            {
                Assert.That(journal.Count, Is.EqualTo(3));
                Assert.That(journal[0].Kind, Is.EqualTo(DomainEventKind.PersonBorn));
                Assert.That(journal[1].Kind, Is.EqualTo(DomainEventKind.PersonDied));
                Assert.That(journal[2].Kind, Is.EqualTo(DomainEventKind.MarriageFormed));
                Assert.That(journal.AsSpan().Length, Is.EqualTo(3));
                Assert.That(journal.AsSpan()[2].Id, Is.EqualTo(new EventId(3UL)));
            });
        }

        [Test]
        public void The_indexer_stops_at_the_count()
        {
            var journal = new EventJournal(8);
            journal.On(Event(1UL, Noon));

            Assert.Multiple(() =>
            {
                Assert.That(() => journal[1], Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => journal[-1], Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void The_journal_grows_past_its_capacity()
        {
            var journal = new EventJournal(2);

            for (var i = 1UL; i <= 9UL; i++)
            {
                journal.On(Event(i, Noon));
            }

            Assert.Multiple(() =>
            {
                Assert.That(journal.Count, Is.EqualTo(9));
                Assert.That(journal[8].Id, Is.EqualTo(new EventId(9UL)));
                Assert.That(journal[0].Id, Is.EqualTo(new EventId(1UL)));
            });
        }

        [Test]
        public void A_defaulted_event_is_refused_rather_than_recorded()
        {
            // default(DomainEvent) skips the constructor that requires an id.
            var journal = new EventJournal(4);
            journal.On(Event(1UL, Noon));

            Assert.Multiple(() =>
            {
                Assert.That(() => journal.On(default), Throws.TypeOf<ArgumentException>());
                Assert.That(journal.Count, Is.EqualTo(1));
            });
        }

        [Test]
        public void Time_may_stand_still_between_entries_but_never_run_backwards()
        {
            var journal = new EventJournal(4);
            journal.On(Event(1UL, Dusk));
            journal.On(Event(2UL, Dusk));

            Assert.Multiple(() =>
            {
                Assert.That(() => journal.On(Event(3UL, Noon)), Throws.TypeOf<InvalidOperationException>());
                Assert.That(journal.Count, Is.EqualTo(2));
            });
        }

        [Test]
        public void Wired_to_a_bus_it_records_what_was_published()
        {
            var ids = new IdAllocator();
            var clock = new SimulationClock(ids);
            var bus = new DomainEventBus(clock);
            var journal = new EventJournal(4);
            bus.Subscribe(journal);

            var born = bus.Publish(DomainEventKind.PersonBorn, Person(1UL), Person(2UL));
            var died = bus.Publish(
                DomainEventKind.PersonDied, Person(3UL), EntityId.None, new Reasons(ReasonCode.FoodShortage));

            Assert.Multiple(() =>
            {
                Assert.That(journal.Count, Is.EqualTo(2));
                Assert.That(journal[0].Id, Is.EqualTo(born));
                Assert.That(journal[1].Id, Is.EqualTo(died));
                Assert.That(journal[1].Reasons.Contains(ReasonCode.FoodShortage), Is.True);
            });
        }

        [Test]
        public void The_digest_is_the_history_folded_in_order()
        {
            // The journal's side of the world hash (#104): the same events in
            // the same order give the same digest, however the array grew,
            // and any field of any event, or their order, moves it.
            DomainEvent With(
                ulong id = 1UL,
                SimulationTime? time = null,
                DomainEventKind kind = DomainEventKind.PersonBorn,
                ulong primary = 1UL,
                ulong secondary = 0UL,
                Reasons reasons = default) =>
                new DomainEvent(
                    new EventId(id),
                    time ?? Noon,
                    kind,
                    Person(primary),
                    secondary == 0UL ? EntityId.None : Person(secondary),
                    reasons);

            ulong DigestOf(int capacity, params DomainEvent[] events)
            {
                var journal = new EventJournal(capacity);

                foreach (var published in events)
                {
                    journal.On(published);
                }

                return journal.Digest;
            }

            var tail = With(2UL, Dusk);
            var baseline = DigestOf(8, With(), tail);
            var two = new Reasons(ReasonCode.FoodShortage, ReasonCode.SpouseDied);

            Assert.Multiple(() =>
            {
                Assert.That(new EventJournal(1).Digest, Is.Zero, "nothing recorded yet");
                Assert.That(DigestOf(1, With(), tail), Is.EqualTo(baseline), "growth is not history");
                Assert.That(DigestOf(8, With()), Is.Not.EqualTo(baseline), "one event fewer");
                Assert.That(DigestOf(8, With(id: 3UL), tail), Is.Not.EqualTo(baseline), "id");
                Assert.That(DigestOf(8, With(time: SimulationTime.FromHours(13L)), tail), Is.Not.EqualTo(baseline), "time");
                Assert.That(DigestOf(8, With(kind: DomainEventKind.PersonDied), tail), Is.Not.EqualTo(baseline), "kind");
                Assert.That(DigestOf(8, With(primary: 5UL), tail), Is.Not.EqualTo(baseline), "primary");
                Assert.That(DigestOf(8, With(secondary: 5UL), tail), Is.Not.EqualTo(baseline), "secondary");
                Assert.That(DigestOf(8, With(reasons: two), tail), Is.Not.EqualTo(baseline), "reasons");
                Assert.That(
                    DigestOf(8, With(reasons: new Reasons(ReasonCode.SpouseDied, ReasonCode.FoodShortage)), tail),
                    Is.Not.EqualTo(DigestOf(8, With(reasons: two), tail)),
                    "reason order");
                Assert.That(
                    DigestOf(8, With(2UL), With(1UL, primary: 2UL)),
                    Is.Not.EqualTo(DigestOf(8, With(1UL, primary: 2UL), With(2UL))),
                    "event order");
            });
        }
    }
}
