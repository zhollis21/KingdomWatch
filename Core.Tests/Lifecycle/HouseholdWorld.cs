using System.Collections.Generic;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.History;
using KingdomWatch.Core.Knowledge;
using KingdomWatch.Core.Lifecycle;
using KingdomWatch.Core.Relationships;
using KingdomWatch.Core.Traversal;
using KingdomWatch.Core.Work;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Lifecycle
{
    // Everything family formation and the death cascade touch, wired the way
    // a world will wire it. Shared by the fixtures in this namespace.
    internal sealed class HouseholdWorld
    {
        internal HouseholdWorld()
            : this(FamilyFormationSettings.Default, new CampSpace())
        {
        }

        internal HouseholdWorld(FamilyFormationSettings settings, IHousing housing)
            : this(settings, housing, new TerrainGrid(8, 8, TerrainKind.Plains))
        {
        }

        internal HouseholdWorld(FamilyFormationSettings settings, IHousing housing, TerrainGrid grid)
        {
            Ids = new IdAllocator();
            Clock = new SimulationClock(Ids);
            Bus = new DomainEventBus(Clock);
            Journal = new EventJournal(64);
            Bus.Subscribe(Journal);
            People = new PersonStore();
            Genealogy = new Genealogy();
            Partnerships = new Partnerships();
            Memories = new Memories(new MemorySettings(4, 0L, 0L, 4));
            Housing = housing;
            Households = new Households(Bus, People, housing);
            Family = new FamilyFormation(Bus, People, Genealogy, Partnerships, Households, settings);
            // The default map is a patch of plains: enough for the cascade to
            // have a Jobs to vacate through. Fixtures that need people to walk
            // somewhere pass their own (Work.WorkWorld).
            Grid = grid;
            Pathfinder = new Pathfinder(Grid, TerrainRules.Default);
            KnownMaps = new KnownMaps(Grid);
            Jobs = new Jobs(Clock, People, Pathfinder, KnownMaps);
            Deaths = new Deaths(Bus, People, Genealogy, Partnerships, Memories, Households, Jobs);
        }

        internal IdAllocator Ids { get; }

        internal SimulationClock Clock { get; }

        internal DomainEventBus Bus { get; }

        internal EventJournal Journal { get; }

        internal PersonStore People { get; }

        internal Genealogy Genealogy { get; }

        internal Partnerships Partnerships { get; }

        internal Memories Memories { get; }

        internal IHousing Housing { get; }

        internal Households Households { get; }

        internal FamilyFormation Family { get; }

        internal TerrainGrid Grid { get; }

        internal Pathfinder Pathfinder { get; }

        internal Jobs Jobs { get; }

        internal KnownMaps KnownMaps { get; }

        internal Deaths Deaths { get; }

        // A founder: recorded in the genealogy with no parents.
        internal PersonHandle NewPerson(AgeStage stage, Sex sex) =>
            NewPerson(stage, sex, EntityId.None, EntityId.None);

        internal PersonHandle NewPerson(AgeStage stage, Sex sex, PersonHandle mother, PersonHandle father) =>
            NewPerson(stage, sex, People.GetId(mother), People.GetId(father));

        internal PersonHandle NewPerson(AgeStage stage, Sex sex, EntityId mother, EntityId father)
        {
            var id = Ids.Next(EntityKind.Person);
            var handle = People.Add(id, default, 100, stage, sex, 0, 0, Clock.Now, 0L);
            Genealogy.Record(id, mother, father);
            return handle;
        }

        internal EntityId IdOf(PersonHandle person) => People.GetId(person);

        // A couple already partnered and housed together, the way worldgen
        // seeds one. Returns their household.
        internal Household NewCouple(out PersonHandle wife, out PersonHandle husband)
        {
            wife = NewPerson(AgeStage.Adult, Sex.Female);
            husband = NewPerson(AgeStage.Adult, Sex.Male);
            return Family.Partner(wife, husband, Reasons.None);
        }

        internal PersonHandle NewChildOf(Household household, PersonHandle mother, PersonHandle father, AgeStage stage)
        {
            var child = NewPerson(stage, Sex.Female, mother, father);
            Households.Join(household, child);
            return child;
        }

        internal MobileGroup NewBand()
        {
            var band = new MobileGroup(Ids.Next(EntityKind.MobileGroup), MobileGroupPurpose.NomadicBand, default);
            Deaths.Track(band);
            return band;
        }

        internal void Advance(long ticks) => Clock.AdvanceTo(Clock.Now.Plus(ticks), new ScheduledEventRouter());

        internal List<DomainEventKind> Published()
        {
            var kinds = new List<DomainEventKind>();
            var events = Journal.AsSpan();

            for (var i = 0; i < events.Length; i++)
            {
                kinds.Add(events[i].Kind);
            }

            return kinds;
        }

        internal DomainEvent LastPublished() => Journal[Journal.Count - 1];

        // The bookkeeping Households maintains by hand: every member's record
        // names the household it is listed in, every living person's household
        // lists them, and nobody is listed twice.
        internal void AssertHouseholdsConsistent()
        {
            var listed = 0;

            foreach (var household in Households.All)
            {
                foreach (var member in household.Members)
                {
                    Assert.That(People.IsAlive(member), Is.True, member + " is listed but dead");
                    Assert.That(People.GetHousehold(member), Is.EqualTo(household.Id), member + " is listed in " + household + " but records another");
                    listed++;
                }
            }

            var housed = 0;

            foreach (var person in People.Alive())
            {
                if (People.GetHousehold(person).IsNone)
                {
                    continue;
                }

                housed++;
                Assert.That(Households.TryGet(People.GetHousehold(person), out var household), Is.True, person + " records a household that does not exist");
                Assert.That(household.Members, Does.Contain(person));
            }

            Assert.That(listed, Is.EqualTo(housed), "someone is listed twice or the dead are still listed");
        }
    }
}
