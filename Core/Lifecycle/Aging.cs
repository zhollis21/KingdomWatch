using System;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;

namespace KingdomWatch.Core.Lifecycle
{
    /// <summary>
    /// People grow up. Owns <see cref="ScheduledEventKind.AgeStageDue"/>: one
    /// wake-up per stage boundary per lifetime, on the exact birthday, at
    /// which the person's <see cref="AgeStage"/> becomes what their age says.
    /// </summary>
    /// <remarks>
    /// **A threshold crossing, not a poll.** A person's boundaries are known
    /// the moment they are born - <see cref="PersonRecord.BornTick"/> plus
    /// the table's birthdays - so each is booked when the previous one
    /// lands, and a thirty-year jump dispatches the three crossings in it in
    /// order. Section 4 names age-stage transitions among the thresholds
    /// that make compression trustworthy; this is that.
    ///
    /// **The age is the truth and the stage is its reading.** Every wake-up
    /// sets the stage to <see cref="DemographicSettings.StageAt"/> rather
    /// than advancing by one: a founder seeded at the wrong stage for their
    /// years is corrected at their next boundary rather than carried through
    /// life one stage adrift. What a stage means to each system - who may
    /// marry, who is a dependent, who eats first - stays with that system
    /// (<see cref="AgeStages"/>); the adolescent-to-apprenticeship mapping
    /// is #22's.
    ///
    /// **People arrive through <see cref="DomainEventKind.PersonBorn"/>**,
    /// and only that way. This class subscribes, and books the first
    /// boundary for whoever the event names - a newborn from
    /// <see cref="Fertility"/>, or a founder worldgen announces at tick zero
    /// - so there is one way in and nothing to call twice. Listen and book,
    /// as section 6 asks of subscribers; the stage itself moves in the
    /// lifecycle phase when the boundary comes due.
    ///
    /// A boundary that comes due for the dead is ignored: the id names
    /// nobody alive, and durable ids are never reused, so there is nobody
    /// it could wrongly age. Nothing is published at a crossing - a
    /// sixteenth birthday is not on section 5's list of domain events, and
    /// the chronicle can derive it from the birth.
    ///
    /// Allocation-free: the handler reads a record, writes a stage and
    /// books one event.
    /// </remarks>
    public sealed class Aging : IScheduledEventHandler, IDomainEventSubscriber
    {
        /// <summary>Section 4: birth, ageing, injury, death.</summary>
        public const SimulationPhase Phase = SimulationPhase.Lifecycle;

        private readonly SimulationClock _clock;
        private readonly PersonStore _people;
        private readonly DemographicSettings _settings;

        /// <param name="bus">
        /// Where births are heard, and where the clock comes from: boundaries
        /// are booked on the clock the bus stamps events with, so a birth and
        /// the birthdays that follow it can never disagree about when.
        /// </param>
        public Aging(DomainEventBus bus, PersonStore people, DemographicSettings settings)
        {
            if (bus is null)
            {
                throw new ArgumentNullException(nameof(bus));
            }

            _people = people ?? throw new ArgumentNullException(nameof(people));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _settings.Validate();
            _clock = bus.Clock;
        }

        public void On(in DomainEvent published)
        {
            if (published.Kind != DomainEventKind.PersonBorn)
            {
                return;
            }

            // Announced by whoever added the person, so they are in the
            // store; a PersonBorn naming nobody is that caller's bug, and
            // the store's throw is the right place to find it.
            if (!_people.TryGetHandle(published.PrimaryEntity, out var person))
            {
                throw new InvalidOperationException(
                    published + " names nobody in the store; a birth is announced after the person is added.");
            }

            ScheduleNextBoundary(person, published.PrimaryEntity);
        }

        public void Handle(ScheduledEvent scheduled, SimulationClock clock)
        {
            if (scheduled.Kind != ScheduledEventKind.AgeStageDue)
            {
                throw new InvalidOperationException(
                    "Aging owns " + ScheduledEventKind.AgeStageDue + ", but was handed " + scheduled + ".");
            }

            if (!ReferenceEquals(clock, _clock))
            {
                throw new InvalidOperationException(
                    "Aging schedules on its bus's clock, but was dispatched by another.");
            }

            if (!_people.TryGetHandle(scheduled.PrimaryEntity, out var person))
            {
                return;
            }

            var age = _people.GetAgeYears(person, clock.Now);
            _people.SetAgeStage(person, _settings.StageAt(age));
            ScheduleNextBoundary(person, scheduled.PrimaryEntity);
        }

        // Booked at the birthday itself, not "in N years from now", so a
        // founder aged fourteen and a half becomes an adult on the day and
        // not a year and a half late.
        private void ScheduleNextBoundary(PersonHandle person, EntityId id)
        {
            var age = _people.GetAgeYears(person, _clock.Now);

            if (!_settings.TryNextBoundary(age, out var boundaryYears))
            {
                return;
            }

            var born = _people.GetBornTick(person);
            var offset = boundaryYears * SimulationTime.TicksPerYear;

            // A birthday past the last representable instant is one that
            // never comes, not one to throw for: the stream ends with time
            // itself, as Hunger's does.
            if (born > long.MaxValue - offset)
            {
                return;
            }

            _clock.Schedule(new SimulationTime(born + offset), Phase, ScheduledEventKind.AgeStageDue, id, EntityId.None);
        }
    }
}
