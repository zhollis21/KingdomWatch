using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Lifecycle;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Lifecycle
{
    /// <summary>
    /// The periodic streams name the event they booked and run no other (#80).
    /// </summary>
    /// <remarks>
    /// A stream that rebooks from inside its own handler doubles for good if a
    /// second event of its kind ever arrives for the same owner: the stray one
    /// runs the handler and books its own successor, and from then on the owner
    /// has two. Nothing produces a foreign event today, so the exposure is a bug
    /// or a save rebuilt against a world that disagrees with it (#42) - both of
    /// which are exactly when a silent doubling is hardest to spot, because the
    /// symptom is a rate the demographic model is tuned on quietly running at
    /// twice its value.
    ///
    /// <see cref="Needs.Hunger"/> and <see cref="Work.Jobs"/> already apply the
    /// rule; these are the two that predate it.
    /// </remarks>
    [TestFixture]
    public sealed class PendingStreamTests
    {
        private static DemographicSettings Immortal() => new DemographicSettings
        {
            InfantMortalityPerMille = 0,
            ChildMortalityPerMille = 0,
            AdolescentMortalityPerMille = 0,
            AdultMortalityPerMille = 0,
            ElderMortalityPerMille = 0,
            SoftLifespanYears = 1_000L,
            MaxLifespanYears = 2_000L,
            ConceptionPerMille = 0,
        };

        [Test]
        public void Only_the_mortality_check_the_person_booked_is_rolled()
        {
            // Two streams would roll the life table twice a year, which doubles
            // the yearly hazard for that person and nothing else.
            var w = new DemographicWorld(Immortal(), 5UL);
            var person = w.NewPerson(30L, Sex.Female);
            var id = w.IdOf(person);

            // A second check, landing before the one the person's record names.
            w.Clock.Schedule(
                w.Clock.Now.Plus(1L), Mortality.Phase, ScheduledEventKind.MortalityCheck, id, EntityId.None);

            Assert.That(() => w.Advance(1L), Throws.InvalidOperationException);
        }

        [Test]
        public void Only_the_birth_check_the_household_booked_is_run()
        {
            // Two streams would check twice as often, doubling the conception
            // chance per interval.
            var w = new DemographicWorld(Immortal(), 5UL);
            w.NewCouple(out _, out _);
            var household = w.Households.All[0];

            w.Clock.Schedule(
                w.Clock.Now.Plus(1L), Fertility.Phase, ScheduledEventKind.BirthCheck, household.Id, EntityId.None);

            Assert.That(() => w.Advance(1L), Throws.InvalidOperationException);
        }

        [Test]
        public void A_person_dying_at_their_own_check_names_no_pending_check()
        {
            // The ordering inside Check, pinned. A death during the roll
            // publishes PersonDied while the record is still readable, and a
            // subscriber that saw the in-flight id would be reading a
            // commitment that can never be kept - the event it names is the
            // one being handled.
            // Wired before anything is published: subscription order is
            // notification order, so the bus refuses a late subscriber.
            var w = new DemographicWorld(Certain(), 5UL);
            var witness = new PendingAtDeath(w);
            w.Bus.Subscribe(witness);
            var person = w.NewPerson(30L, Sex.Female);

            w.AdvanceYears(1L);

            Assert.Multiple(() =>
            {
                Assert.That(w.People.IsAlive(person), Is.False, "the table is certain, so the roll kills");
                Assert.That(witness.Saw, Is.True, "the cascade ran");
                Assert.That(witness.Pending, Is.EqualTo(EventId.None));
            });
        }

        [Test]
        public void A_mortality_check_is_cancelled_when_its_person_dies()
        {
            // The record and the queue name the same event, so the death
            // cascade clears both - the rule EndPregnancy already follows.
            var w = new DemographicWorld(Immortal(), 5UL);
            var person = w.NewPerson(30L, Sex.Female);

            Assert.That(w.People.GetPendingMortalityCheck(person).IsNone, Is.False, "a living person is booked");

            w.Deaths.Die(person, Reasons.None);

            Assert.That(() => w.AdvanceYears(3L), Throws.Nothing, "no check survives the person");
        }

        // A table nobody survives, so the birthday roll is certain to kill.
        private static DemographicSettings Certain() => new DemographicSettings
        {
            InfantMortalityPerMille = 1000,
            ChildMortalityPerMille = 1000,
            AdolescentMortalityPerMille = 1000,
            AdultMortalityPerMille = 1000,
            ElderMortalityPerMille = 1000,
            SoftLifespanYears = 1_000L,
            MaxLifespanYears = 2_000L,
            ConceptionPerMille = 0,
        };

        // Reads the dying person's record from inside the death cascade. The
        // person is resolved from the event rather than held: Die announces
        // the death before removing them, so the handle is still good here,
        // and the subscriber has to exist before anyone is born.
        private sealed class PendingAtDeath : IDomainEventSubscriber
        {
            private readonly DemographicWorld _world;

            internal PendingAtDeath(DemographicWorld world)
            {
                _world = world;
            }

            internal bool Saw { get; private set; }

            internal EventId Pending { get; private set; }

            public void On(in DomainEvent published)
            {
                if (published.Kind != DomainEventKind.PersonDied
                    || Saw
                    || !_world.People.TryGetHandle(published.PrimaryEntity, out var dying))
                {
                    return;
                }

                Saw = true;
                Pending = _world.People.GetPendingMortalityCheck(dying);
            }
        }
    }
}
