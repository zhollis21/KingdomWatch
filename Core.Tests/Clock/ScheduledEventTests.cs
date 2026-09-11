using System;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Clock
{
    [TestFixture]
    public sealed class ScheduledEventTests
    {
        private static readonly SimulationTime Noon = SimulationTime.FromHours(12L);
        private static readonly SimulationTime Dusk = SimulationTime.FromHours(18L);

        private static EntityId Person(ulong value) => new EntityId(EntityKind.Person, value);

        private static ScheduledEvent Build(
            ulong id,
            SimulationTime time,
            SimulationPhase phase,
            ScheduledEventKind kind,
            EntityId primary,
            EntityId secondary) =>
            new ScheduledEvent(new EventId(id), time, phase, kind, primary, secondary);

        [Test]
        public void An_event_needs_a_durable_id()
        {
            // The id is the last component of the ordering key. Without one,
            // two otherwise identical events have no defined order and the
            // queue decides, which is the failure the sixth component exists
            // to remove.
            Assert.That(
                () => Build(
                    0UL,
                    Noon,
                    SimulationPhase.Physical,
                    ScheduledEventKind.TaskCompleted,
                    Person(1UL),
                    EntityId.None),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void An_undefined_phase_is_rejected()
        {
            // An enum is an int with names, and this one is persisted: an
            // unrecognised phase reaching a save can never be resolved again.
            Assert.Multiple(() =>
            {
                Assert.That(
                    () => Build(
                        1UL,
                        Noon,
                        (SimulationPhase)999,
                        ScheduledEventKind.TaskCompleted,
                        Person(1UL),
                        EntityId.None),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => Build(
                        1UL,
                        Noon,
                        (SimulationPhase)(-1),
                        ScheduledEventKind.TaskCompleted,
                        Person(1UL),
                        EntityId.None),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void The_none_phase_is_a_guard_not_a_phase()
        {
            Assert.That(
                () => Build(
                    1UL,
                    Noon,
                    SimulationPhase.None,
                    ScheduledEventKind.TaskCompleted,
                    Person(1UL),
                    EntityId.None),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void An_undefined_kind_is_rejected()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    () => Build(
                        1UL,
                        Noon,
                        SimulationPhase.Physical,
                        (ScheduledEventKind)999,
                        Person(1UL),
                        EntityId.None),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => Build(
                        1UL,
                        Noon,
                        SimulationPhase.Physical,
                        (ScheduledEventKind)(-1),
                        Person(1UL),
                        EntityId.None),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void The_none_kind_is_a_guard_not_an_occurrence()
        {
            Assert.That(
                () => Build(
                    1UL,
                    Noon,
                    SimulationPhase.Physical,
                    ScheduledEventKind.None,
                    Person(1UL),
                    EntityId.None),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Both_entities_may_be_absent()
        {
            // A world-level event - a season turning - is about nobody in
            // particular, and most events have no second party.
            var scheduled = Build(
                1UL,
                Noon,
                SimulationPhase.Physical,
                ScheduledEventKind.TaskCompleted,
                EntityId.None,
                EntityId.None);

            Assert.Multiple(() =>
            {
                Assert.That(scheduled.PrimaryEntity, Is.EqualTo(EntityId.None));
                Assert.That(scheduled.SecondaryEntity, Is.EqualTo(EntityId.None));
            });
        }

        [Test]
        public void Time_outranks_everything_after_it()
        {
            // Each of these ordering tests sets every LATER component to
            // contradict the expected answer, so it can only pass if the
            // earlier component really does win.
            var earlier = Build(
                9UL,
                Noon,
                SimulationPhase.Derived,
                ScheduledEventKind.SocialDecision,
                Person(99UL),
                Person(99UL));
            var later = Build(
                1UL,
                Dusk,
                SimulationPhase.Physical,
                ScheduledEventKind.TaskCompleted,
                Person(1UL),
                Person(1UL));

            Assert.Multiple(() =>
            {
                Assert.That(earlier.CompareTo(later), Is.Negative);
                Assert.That(later.CompareTo(earlier), Is.Positive);
            });
        }

        [Test]
        public void Phase_outranks_everything_after_it()
        {
            var earlier = Build(
                9UL,
                Noon,
                SimulationPhase.Physical,
                ScheduledEventKind.SocialDecision,
                Person(99UL),
                Person(99UL));
            var later = Build(
                1UL,
                Noon,
                SimulationPhase.Lifecycle,
                ScheduledEventKind.TaskCompleted,
                Person(1UL),
                Person(1UL));

            Assert.Multiple(() =>
            {
                Assert.That(earlier.CompareTo(later), Is.Negative);
                Assert.That(later.CompareTo(earlier), Is.Positive);
            });
        }

        [Test]
        public void The_primary_entity_outranks_the_kind()
        {
            // Deliberate: everything happening to one person at one instant
            // stays together, which is what makes a dispatch log readable.
            var earlier = Build(
                9UL,
                Noon,
                SimulationPhase.Physical,
                ScheduledEventKind.SocialDecision,
                Person(1UL),
                Person(99UL));
            var later = Build(
                1UL,
                Noon,
                SimulationPhase.Physical,
                ScheduledEventKind.TaskCompleted,
                Person(2UL),
                Person(1UL));

            Assert.Multiple(() =>
            {
                Assert.That(earlier.CompareTo(later), Is.Negative);
                Assert.That(later.CompareTo(earlier), Is.Positive);
            });
        }

        [Test]
        public void Kind_outranks_the_secondary_entity()
        {
            var earlier = Build(
                9UL,
                Noon,
                SimulationPhase.Physical,
                ScheduledEventKind.TaskCompleted,
                Person(1UL),
                Person(99UL));
            var later = Build(
                1UL,
                Noon,
                SimulationPhase.Physical,
                ScheduledEventKind.BirthCheck,
                Person(1UL),
                Person(1UL));

            Assert.Multiple(() =>
            {
                Assert.That(earlier.CompareTo(later), Is.Negative);
                Assert.That(later.CompareTo(earlier), Is.Positive);
            });
        }

        [Test]
        public void The_secondary_entity_outranks_the_id()
        {
            var earlier = Build(
                9UL,
                Noon,
                SimulationPhase.Physical,
                ScheduledEventKind.TaskCompleted,
                Person(1UL),
                Person(1UL));
            var later = Build(
                1UL,
                Noon,
                SimulationPhase.Physical,
                ScheduledEventKind.TaskCompleted,
                Person(1UL),
                Person(2UL));

            Assert.Multiple(() =>
            {
                Assert.That(earlier.CompareTo(later), Is.Negative);
                Assert.That(later.CompareTo(earlier), Is.Positive);
            });
        }

        [Test]
        public void The_id_settles_what_section_four_leaves_tied()
        {
            // Two hauling trips by the same person to the same granary, landing
            // in the same simulated second. Section 4's five components are
            // identical; only the id separates them, and it does so by
            // scheduling order because ids are handed out in sequence.
            var first = Build(
                1UL,
                Noon,
                SimulationPhase.Physical,
                ScheduledEventKind.TaskCompleted,
                Person(1UL),
                new EntityId(EntityKind.Settlement, 4UL));
            var second = Build(
                2UL,
                Noon,
                SimulationPhase.Physical,
                ScheduledEventKind.TaskCompleted,
                Person(1UL),
                new EntityId(EntityKind.Settlement, 4UL));

            Assert.Multiple(() =>
            {
                Assert.That(first.CompareTo(second), Is.Negative);
                Assert.That(second.CompareTo(first), Is.Positive);
                Assert.That(first < second, Is.True);
                Assert.That(second > first, Is.True);
            });
        }

        [Test]
        public void Position_ignores_the_id_that_the_full_order_falls_back_on()
        {
            // The distinction the scheduler's forward-only guard rests on.
            // These two are different events - CompareTo separates them - but
            // they occupy the same position, and a reaction landing on its own
            // cause has to be recognisable as exactly that.
            var cause = Build(
                1UL,
                Noon,
                SimulationPhase.Physical,
                ScheduledEventKind.TaskCompleted,
                Person(1UL),
                EntityId.None);
            var sameSpot = Build(
                2UL,
                Noon,
                SimulationPhase.Physical,
                ScheduledEventKind.TaskCompleted,
                Person(1UL),
                EntityId.None);

            Assert.Multiple(() =>
            {
                Assert.That(cause.ComparePositionTo(sameSpot), Is.Zero);
                Assert.That(sameSpot.ComparePositionTo(cause), Is.Zero);
                Assert.That(cause.CompareTo(sameSpot), Is.Negative);
            });
        }

        [Test]
        public void Position_agrees_with_the_full_order_on_every_other_component()
        {
            var earlier = Build(
                9UL,
                Noon,
                SimulationPhase.Physical,
                ScheduledEventKind.TaskCompleted,
                Person(1UL),
                Person(1UL));
            var laterTime = Build(
                1UL,
                Dusk,
                SimulationPhase.Physical,
                ScheduledEventKind.TaskCompleted,
                Person(1UL),
                Person(1UL));
            var laterPhase = Build(
                1UL,
                Noon,
                SimulationPhase.Lifecycle,
                ScheduledEventKind.TaskCompleted,
                Person(1UL),
                Person(1UL));
            var laterPrimary = Build(
                1UL,
                Noon,
                SimulationPhase.Physical,
                ScheduledEventKind.TaskCompleted,
                Person(2UL),
                Person(1UL));
            var laterKind = Build(
                1UL,
                Noon,
                SimulationPhase.Physical,
                ScheduledEventKind.BirthCheck,
                Person(1UL),
                Person(1UL));
            var laterSecondary = Build(
                1UL,
                Noon,
                SimulationPhase.Physical,
                ScheduledEventKind.TaskCompleted,
                Person(1UL),
                Person(2UL));

            Assert.Multiple(() =>
            {
                Assert.That(earlier.ComparePositionTo(laterTime), Is.Negative);
                Assert.That(earlier.ComparePositionTo(laterPhase), Is.Negative);
                Assert.That(earlier.ComparePositionTo(laterPrimary), Is.Negative);
                Assert.That(earlier.ComparePositionTo(laterKind), Is.Negative);
                Assert.That(earlier.ComparePositionTo(laterSecondary), Is.Negative);
                Assert.That(laterTime.ComparePositionTo(earlier), Is.Positive);
                Assert.That(laterPhase.ComparePositionTo(earlier), Is.Positive);
                Assert.That(laterPrimary.ComparePositionTo(earlier), Is.Positive);
                Assert.That(laterKind.ComparePositionTo(earlier), Is.Positive);
                Assert.That(laterSecondary.ComparePositionTo(earlier), Is.Positive);
            });
        }

        [Test]
        public void Nothing_but_an_event_and_itself_compares_equal()
        {
            // The whole point of the sixth component: a zero from CompareTo
            // means the same event, never two events the queue must choose
            // between.
            var scheduled = Build(
                1UL,
                Noon,
                SimulationPhase.Physical,
                ScheduledEventKind.TaskCompleted,
                Person(1UL),
                EntityId.None);
            var same = Build(
                1UL,
                Noon,
                SimulationPhase.Physical,
                ScheduledEventKind.TaskCompleted,
                Person(1UL),
                EntityId.None);

            Assert.Multiple(() =>
            {
                Assert.That(scheduled.CompareTo(scheduled), Is.Zero);
                Assert.That(scheduled.CompareTo(same), Is.Zero);
                Assert.That(scheduled, Is.EqualTo(same));
                Assert.That(scheduled.GetHashCode(), Is.EqualTo(same.GetHashCode()));
                Assert.That(scheduled == same, Is.True);
                Assert.That(scheduled != same, Is.False);
                Assert.That(scheduled.Equals((object)same), Is.True);
                Assert.That(scheduled <= same, Is.True);
                Assert.That(scheduled >= same, Is.True);
            });
        }

        [Test]
        public void Nothing_that_is_not_an_event_is_equal_to_one()
        {
            var scheduled = Build(
                1UL,
                Noon,
                SimulationPhase.Physical,
                ScheduledEventKind.TaskCompleted,
                Person(1UL),
                EntityId.None);

            Assert.Multiple(() =>
            {
                Assert.That(scheduled.Equals(null), Is.False);
                Assert.That(scheduled.Equals("Event#1"), Is.False);
                Assert.That(scheduled.Equals(scheduled.Id), Is.False);
            });
        }

        [Test]
        public void Events_differing_in_any_single_component_are_unequal()
        {
            var baseline = Build(
                1UL,
                Noon,
                SimulationPhase.Physical,
                ScheduledEventKind.TaskCompleted,
                Person(1UL),
                Person(2UL));

            Assert.Multiple(() =>
            {
                Assert.That(
                    baseline,
                    Is.Not.EqualTo(Build(
                        2UL,
                        Noon,
                        SimulationPhase.Physical,
                        ScheduledEventKind.TaskCompleted,
                        Person(1UL),
                        Person(2UL))));
                Assert.That(
                    baseline,
                    Is.Not.EqualTo(Build(
                        1UL,
                        Dusk,
                        SimulationPhase.Physical,
                        ScheduledEventKind.TaskCompleted,
                        Person(1UL),
                        Person(2UL))));
                Assert.That(
                    baseline,
                    Is.Not.EqualTo(Build(
                        1UL,
                        Noon,
                        SimulationPhase.Lifecycle,
                        ScheduledEventKind.TaskCompleted,
                        Person(1UL),
                        Person(2UL))));
                Assert.That(
                    baseline,
                    Is.Not.EqualTo(Build(
                        1UL,
                        Noon,
                        SimulationPhase.Physical,
                        ScheduledEventKind.BirthCheck,
                        Person(1UL),
                        Person(2UL))));
                Assert.That(
                    baseline,
                    Is.Not.EqualTo(Build(
                        1UL,
                        Noon,
                        SimulationPhase.Physical,
                        ScheduledEventKind.TaskCompleted,
                        Person(9UL),
                        Person(2UL))));
                Assert.That(
                    baseline,
                    Is.Not.EqualTo(Build(
                        1UL,
                        Noon,
                        SimulationPhase.Physical,
                        ScheduledEventKind.TaskCompleted,
                        Person(1UL),
                        Person(9UL))));
            });
        }

        [Test]
        public void ToString_names_the_event_its_parties_and_its_instant()
        {
            var scheduled = Build(
                7UL,
                Noon,
                SimulationPhase.Physical,
                ScheduledEventKind.TaskCompleted,
                Person(1UL),
                new EntityId(EntityKind.Settlement, 4UL));

            Assert.That(
                scheduled.ToString(),
                Is.EqualTo(
                    "Event#7 TaskCompleted Person#1->Settlement#4 Physical @ day 0 12:00:00 (tick 43200)"));
        }
    }
}
