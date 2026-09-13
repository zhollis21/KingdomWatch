using System;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Events
{
    [TestFixture]
    public sealed class DomainEventTests
    {
        private static readonly SimulationTime Noon = SimulationTime.FromHours(12L);
        private static readonly SimulationTime Dusk = SimulationTime.FromHours(18L);

        private static EntityId Person(ulong value) => new EntityId(EntityKind.Person, value);

        private static DomainEvent Build(
            ulong id = 1UL,
            SimulationTime? time = null,
            DomainEventKind kind = DomainEventKind.PersonDied,
            EntityId primary = default,
            EntityId secondary = default,
            Reasons reasons = default) =>
            new DomainEvent(new EventId(id), time ?? Noon, kind, primary, secondary, reasons);

        [Test]
        public void An_event_needs_a_durable_id()
        {
            Assert.That(() => Build(id: 0UL), Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void An_undefined_kind_is_rejected()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    () => Build(kind: (DomainEventKind)999),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => Build(kind: (DomainEventKind)(-1)),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void The_none_kind_is_a_guard_not_an_occurrence()
        {
            Assert.That(
                () => Build(kind: DomainEventKind.None),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Every_defined_kind_but_none_is_accepted()
        {
            foreach (DomainEventKind kind in Enum.GetValues(typeof(DomainEventKind)))
            {
                if (kind == DomainEventKind.None)
                {
                    continue;
                }

                Assert.That(Build(kind: kind).Kind, Is.EqualTo(kind));
            }
        }

        [Test]
        public void Both_entities_may_be_absent_and_reasons_default_to_none()
        {
            var famine = Build(kind: DomainEventKind.FamineStarted);

            Assert.Multiple(() =>
            {
                Assert.That(famine.PrimaryEntity, Is.EqualTo(EntityId.None));
                Assert.That(famine.SecondaryEntity, Is.EqualTo(EntityId.None));
                Assert.That(famine.Reasons, Is.EqualTo(Reasons.None));
            });
        }

        [Test]
        public void Every_component_is_kept()
        {
            var reasons = new Reasons(ReasonCode.FoodShortage, ReasonCode.SpouseDied);
            var left = Build(
                id: 7UL,
                time: Dusk,
                kind: DomainEventKind.MarriageFormed,
                primary: Person(1UL),
                secondary: Person(2UL),
                reasons: reasons);

            Assert.Multiple(() =>
            {
                Assert.That(left.Id, Is.EqualTo(new EventId(7UL)));
                Assert.That(left.Time, Is.EqualTo(Dusk));
                Assert.That(left.Kind, Is.EqualTo(DomainEventKind.MarriageFormed));
                Assert.That(left.PrimaryEntity, Is.EqualTo(Person(1UL)));
                Assert.That(left.SecondaryEntity, Is.EqualTo(Person(2UL)));
                Assert.That(left.Reasons, Is.EqualTo(reasons));
            });
        }

        [Test]
        public void Events_differing_in_any_single_component_are_unequal()
        {
            var reasons = new Reasons(ReasonCode.FoodShortage);
            var baseline = Build(
                id: 1UL,
                time: Noon,
                kind: DomainEventKind.PersonDied,
                primary: Person(1UL),
                secondary: Person(2UL),
                reasons: reasons);
            var same = Build(
                id: 1UL,
                time: Noon,
                kind: DomainEventKind.PersonDied,
                primary: Person(1UL),
                secondary: Person(2UL),
                reasons: reasons);

            Assert.Multiple(() =>
            {
                Assert.That(baseline, Is.EqualTo(same));
                Assert.That(baseline == same, Is.True);
                Assert.That(baseline.GetHashCode(), Is.EqualTo(same.GetHashCode()));
                Assert.That(baseline.Equals((object)same), Is.True);
                Assert.That(baseline.Equals("not an event"), Is.False);

                Assert.That(baseline != Build(id: 2UL, primary: Person(1UL), secondary: Person(2UL), reasons: reasons), Is.True);
                Assert.That(baseline != Build(time: Dusk, primary: Person(1UL), secondary: Person(2UL), reasons: reasons), Is.True);
                Assert.That(baseline != Build(kind: DomainEventKind.PersonBorn, primary: Person(1UL), secondary: Person(2UL), reasons: reasons), Is.True);
                Assert.That(baseline != Build(primary: Person(3UL), secondary: Person(2UL), reasons: reasons), Is.True);
                Assert.That(baseline != Build(primary: Person(1UL), secondary: Person(3UL), reasons: reasons), Is.True);
                Assert.That(baseline != Build(primary: Person(1UL), secondary: Person(2UL)), Is.True);
            });
        }

        [Test]
        public void ToString_names_the_event_its_parties_its_instant_and_its_reasons()
        {
            var plain = Build(id: 3UL, kind: DomainEventKind.PersonBorn, primary: Person(4UL));
            var decided = Build(
                id: 5UL,
                kind: DomainEventKind.WarDeclared,
                primary: new EntityId(EntityKind.Polity, 1UL),
                secondary: new EntityId(EntityKind.Polity, 2UL),
                reasons: new Reasons(ReasonCode.TradersAttacked, ReasonCode.TerritoryClaim));

            Assert.Multiple(() =>
            {
                Assert.That(plain.ToString(), Is.EqualTo("Event#3 PersonBorn Person#4->None @ " + Noon));
                Assert.That(
                    decided.ToString(),
                    Is.EqualTo("Event#5 WarDeclared Polity#1->Polity#2 @ " + Noon + " because [TradersAttacked, TerritoryClaim]"));
            });
        }
    }
}
