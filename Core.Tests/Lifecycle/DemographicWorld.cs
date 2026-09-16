using System.Collections.Generic;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.History;
using KingdomWatch.Core.Lifecycle;
using KingdomWatch.Core.Needs;
using KingdomWatch.Core.Relationships;
using KingdomWatch.Core.Rng;
using KingdomWatch.Core.Traversal;

namespace KingdomWatch.Core.Tests.Lifecycle
{
    // A HouseholdWorld with the demographic model on top: ageing, mortality,
    // fertility and hunger all registered on one router, people announced
    // the way worldgen will announce them. Shared by the fixtures that need
    // time to pass.
    internal sealed class DemographicWorld
    {
        // Every founder eats at tick zero; the band is fed for centuries so
        // that hunger only matters in the tests that starve someone.
        internal const int PlentifulFood = 100_000_000;

        internal DemographicWorld()
            : this(DemographicSettings.Default, 1UL)
        {
        }

        internal DemographicWorld(DemographicSettings settings, ulong seed)
            : this(settings, seed, new TerrainGrid(8, 8, TerrainKind.Plains))
        {
        }

        internal DemographicWorld(DemographicSettings settings, ulong seed, TerrainGrid grid)
        {
            Base = new HouseholdWorld(FamilyFormationSettings.Default, new CampSpace(), grid);
            Settings = settings;
            Rng = new DeterministicRng(seed);
            Aging = new Aging(Bus, People, settings);
            Mortality = new Mortality(Bus, People, Deaths, Rng, settings);
            Fertility = new Fertility(Bus, People, Genealogy, Partnerships, Households, Rng, settings);
            Hunger = new Hunger(Bus, People);
            Bus.Subscribe(Aging);
            Bus.Subscribe(Mortality);
            Bus.Subscribe(Fertility);

            Router = new ScheduledEventRouter();
            Router.Register(ScheduledEventKind.AgeStageDue, Aging);
            Router.Register(ScheduledEventKind.MortalityCheck, Mortality);
            Router.Register(ScheduledEventKind.StarvationCritical, Mortality);
            Router.Register(ScheduledEventKind.BirthCheck, Fertility);
            Router.Register(ScheduledEventKind.BirthDue, Fertility);
            Router.Register(ScheduledEventKind.MealDue, Hunger);
        }

        internal HouseholdWorld Base { get; }

        internal DemographicSettings Settings { get; }

        internal DeterministicRng Rng { get; }

        internal Aging Aging { get; }

        internal Mortality Mortality { get; }

        internal Fertility Fertility { get; }

        internal Hunger Hunger { get; }

        internal ScheduledEventRouter Router { get; }

        internal SimulationClock Clock => Base.Clock;

        internal DomainEventBus Bus => Base.Bus;

        internal EventJournal Journal => Base.Journal;

        internal PersonStore People => Base.People;

        internal Genealogy Genealogy => Base.Genealogy;

        internal Partnerships Partnerships => Base.Partnerships;

        internal Households Households => Base.Households;

        internal FamilyFormation Family => Base.Family;

        internal Deaths Deaths => Base.Deaths;

        // A founder of the given age, announced: recorded in the genealogy
        // with no parents, and PersonBorn published so ageing and mortality
        // book their first wake-ups. The stage is what the table says for
        // the age unless a test wants otherwise.
        internal PersonHandle NewPerson(long ageYears, Sex sex) =>
            NewPerson(ageYears, sex, Settings.StageAt(ageYears));

        internal PersonHandle NewPerson(long ageYears, Sex sex, AgeStage stage) =>
            NewPersonBornAt(Clock.Now.Ticks - ageYears * SimulationTime.TicksPerYear, sex, stage);

        // The stage the table says for the age, with the age read back from
        // the store so that a birth the clock has outrun saturates the way
        // it does everywhere else. Announced only once the stage is right.
        internal PersonHandle NewPersonBornAt(long bornTick, Sex sex)
        {
            var id = Base.Ids.Next(EntityKind.Person);
            var handle = People.Add(id, default, 100, AgeStage.Infant, sex, 0, 0, Clock.Now, bornTick);
            People.SetAgeStage(handle, Settings.StageAt(People.GetAgeYears(handle, Clock.Now)));
            Genealogy.Record(id, EntityId.None, EntityId.None);
            Bus.Publish(DomainEventKind.PersonBorn, id, EntityId.None);
            return handle;
        }

        internal PersonHandle NewPersonBornAt(long bornTick, Sex sex, AgeStage stage) =>
            NewPersonBornAt(bornTick, sex, stage, EntityId.None, EntityId.None);

        // A child of two people already in the world, announced the same way.
        internal PersonHandle NewChild(long ageYears, Sex sex, PersonHandle mother, PersonHandle father) =>
            NewPersonBornAt(
                Clock.Now.Ticks - ageYears * SimulationTime.TicksPerYear,
                sex,
                Settings.StageAt(ageYears),
                IdOf(mother),
                IdOf(father));

        internal PersonHandle NewPersonBornAt(long bornTick, Sex sex, AgeStage stage, EntityId mother, EntityId father)
        {
            var id = Base.Ids.Next(EntityKind.Person);
            var handle = People.Add(id, default, 100, stage, sex, 0, 0, Clock.Now, bornTick);
            Genealogy.Record(id, mother, father);
            Bus.Publish(DomainEventKind.PersonBorn, id, EntityId.None);
            return handle;
        }

        // A couple in their twenties, partnered and housed together, the way
        // worldgen seeds one. Returns their household.
        internal Household NewCouple(out PersonHandle wife, out PersonHandle husband)
        {
            wife = NewPerson(22L, Sex.Female);
            husband = NewPerson(25L, Sex.Male);
            return Family.Partner(wife, husband, Reasons.None);
        }

        // A band the cascade strikes the dead from and newborns join, with
        // food enough that nobody starves unless a test takes it away.
        internal MobileGroup NewBand()
        {
            var band = NewStarvingBand();
            band.SharedSupplies.Gather(ResourceKind.Food, PlentifulFood);
            return band;
        }

        // The same, with nothing to eat.
        internal MobileGroup NewStarvingBand()
        {
            var band = Base.NewBand();
            Fertility.Track(band);
            Hunger.Track(band);
            return band;
        }

        internal void Advance(long ticks) => Clock.AdvanceTo(Clock.Now.Plus(ticks), Router);

        internal void AdvanceTo(SimulationTime time) => Clock.AdvanceTo(time, Router);

        internal void AdvanceYears(long years) => Advance(years * SimulationTime.TicksPerYear);

        internal EntityId IdOf(PersonHandle person) => People.GetId(person);

        internal SimulationTime BirthdayOf(PersonHandle person, long years) =>
            new SimulationTime(People.GetBornTick(person) + years * SimulationTime.TicksPerYear);

        internal List<DomainEvent> Published(DomainEventKind kind)
        {
            var matching = new List<DomainEvent>();
            var events = Journal.AsSpan();

            for (var i = 0; i < events.Length; i++)
            {
                if (events[i].Kind == kind)
                {
                    matching.Add(events[i]);
                }
            }

            return matching;
        }
    }
}
