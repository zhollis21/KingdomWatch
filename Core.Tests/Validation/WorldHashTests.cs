using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Lifecycle;
using KingdomWatch.Core.Tests.Lifecycle;
using KingdomWatch.Core.Tests.Work;
using KingdomWatch.Core.Nomadic;
using KingdomWatch.Core.Validation;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Validation
{
    /// <summary>
    /// The canonical world hash (#13): the same logical world hashes the same,
    /// a different one does not, and representation is not part of it.
    /// </summary>
    [TestFixture]
    public sealed class WorldHashTests
    {
        private static DemographicSettings Quiet() => new DemographicSettings
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
        public void The_same_run_hashes_the_same()
        {
            // Two independent worlds from the same seed, not one world twice:
            // the claim is that the run is reproducible, not that the hash is
            // a pure function of an object already in memory.
            var first = HashOf(Populate(Build()));
            var second = HashOf(Populate(Build()));

            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void One_changed_field_changes_the_hash()
        {
            // Every field the hash folds in, one at a time. A field added to
            // PersonRecord and forgotten here is a field whose divergence the
            // comparison would not report, which is the failure mode that
            // makes a hash worse than useless - it reads as coverage.
            Assert.Multiple(() =>
            {
                AssertMoves("health", static (ref PersonRecord r) => r.Health++);
                AssertMoves("position", static (ref PersonRecord r) => r.Position = new WorldPosition(r.Position.X + 1, r.Position.Y));
                AssertMoves("age stage", static (ref PersonRecord r) => r.AgeStage = r.AgeStage == AgeStage.Elder ? AgeStage.Adult : AgeStage.Elder);
                AssertMoves("sex", static (ref PersonRecord r) => r.Sex = r.Sex == Sex.Male ? Sex.Female : Sex.Male);
                AssertMoves("born tick", static (ref PersonRecord r) => r.BornTick--);
                AssertMoves("birth culture", static (ref PersonRecord r) => r.BirthCulture++);
                AssertMoves("assimilation", static (ref PersonRecord r) => r.Assimilation++);
                AssertMoves("last fed", static (ref PersonRecord r) => r.LastFedAt = r.LastFedAt.Plus(1L));
                AssertMoves("job", static (ref PersonRecord r) => r.Job = r.Job == JobKind.Forager ? JobKind.Woodcutter : JobKind.Forager);
                AssertMoves("household", static (ref PersonRecord r) => r.Household = new EntityId(EntityKind.Household, 999UL));
                AssertMoves("pregnancy", static (ref PersonRecord r) => r.PregnancyDue = new EventId(9_999UL));
                AssertMoves("mortality check", static (ref PersonRecord r) => r.PendingMortalityCheck = new EventId(9_998UL));
                AssertMoves("age stage boundary", static (ref PersonRecord r) => r.PendingAgeStage = new EventId(9_997UL));
                AssertMoves("id", static (ref PersonRecord r) => r.Id = new EntityId(EntityKind.Person, 12_345UL));
            });
        }

        [Test]
        public void The_storage_handle_is_not_part_of_the_hash()
        {
            // Section 5's whole point: sort by durable id and serialise
            // deterministic fields, "otherwise desktop and IL2CPP disagree
            // because representation differs even when the logical world is
            // identical". A slot index and a generation are representation.
            // The people section alone, deliberately. Rewriting generations
            // makes every handle stored elsewhere stale, so the household
            // section would change too - and correctly, because a member list
            // that no longer resolves is a different world. What is being
            // pinned here is only that the handle sitting in the record is not
            // itself folded in.
            var world = Populate(Build());
            var before = new WorldHash().AddPeople(world.People).Value;
            var records = world.People.RecordSpan();

            for (var i = 0; i < records.Length; i++)
            {
                if (!records[i].Id.IsNone)
                {
                    records[i].Handle = new PersonHandle(records[i].Handle.Index, records[i].Handle.Generation + 2);
                }
            }

            Assert.That(new WorldHash().AddPeople(world.People).Value, Is.EqualTo(before));
        }

        [Test]
        public void A_section_that_was_not_added_is_not_the_same_as_an_empty_one()
        {
            // Each Add folds in its own tag and count before any content, so
            // two callers covering different ground cannot collide by
            // accident - which is what stops a partial comparison reading as
            // agreement.
            var world = Populate(Build());

            var peopleOnly = new WorldHash().AddPeople(world.People).Value;
            var peopleAndHouseholds = new WorldHash()
                .AddPeople(world.People)
                .AddHouseholds(world.Households, world.People)
                .Value;

            Assert.That(peopleOnly, Is.Not.EqualTo(peopleAndHouseholds));
        }

        [Test]
        public void Section_order_is_part_of_the_hash()
        {
            var world = Populate(Build());

            var forward = new WorldHash()
                .AddPeople(world.People)
                .AddHouseholds(world.Households, world.People)
                .Value;
            var backward = new WorldHash()
                .AddHouseholds(world.Households, world.People)
                .AddPeople(world.People)
                .Value;

            Assert.That(forward, Is.Not.EqualTo(backward));
        }

        [Test]
        public void Booking_an_event_changes_the_pending_section()
        {
            // Pending events are state. A world that agrees on its people and
            // disagrees on what it has booked has already diverged - section
            // 17 makes the same point from the save side.
            var world = Populate(Build());
            var before = new WorldHash().AddPending(world.Clock).Value;

            world.Clock.Schedule(
                world.Clock.Now.Plus(SimulationTime.TicksPerDay),
                SimulationPhase.Physical,
                ScheduledEventKind.WorkDayDue,
                new EntityId(EntityKind.MobileGroup, 77UL),
                EntityId.None);

            Assert.That(new WorldHash().AddPending(world.Clock).Value, Is.Not.EqualTo(before));
        }

        [Test]
        public void Settlements_and_their_supplies_are_folded_in()
        {
            // The one section DemographicWorld cannot reach, so it needs a
            // world that founds something. Without this the section was
            // written, shipped and never once executed.
            var world = new WorkWorld();
            var band = world.NewWanderingBand(WorkWorld.Camp, 0);

            world.JoinAdults(band, 60);
            band.SharedSupplies.Gather(ResourceKind.Food, WorkWorld.PlentifulFood(60) * 4);

            // Settling is pressure over councils, and a council is every
            // sixtieth day; NomadicBandsTests drives it the same way.
            var councils = (NomadicBands.SettlingPressure + 59L) / 60L;
            world.Advance((councils + 60L) * SimulationTime.TicksPerDay);

            Assert.That(world.Founding.All, Is.Not.Empty, "the band never settled, so nothing was hashed");

            var before = new WorldHash().AddSettlements(world.Founding, world.People).Value;

            world.Founding.All[0].SharedSupplies.Open(ResourceKind.Stone, 7);

            Assert.Multiple(() =>
            {
                Assert.That(
                    new WorldHash().AddSettlements(world.Founding, world.People).Value,
                    Is.Not.EqualTo(before),
                    "a settlement's stores are part of the world");
                Assert.That(
                    () => new WorldHash().AddSettlements(world.Founding, null!),
                    Throws.ArgumentNullException);
            });
        }

        [Test]
        public void A_reused_hash_must_be_reset()
        {
            var world = Populate(Build());
            var hash = new WorldHash();

            var once = hash.AddPeople(world.People).Value;
            var twice = hash.AddPeople(world.People).Value;
            var reset = hash.Reset().AddPeople(world.People).Value;

            Assert.Multiple(() =>
            {
                Assert.That(twice, Is.Not.EqualTo(once), "folding the same section in again is not idempotent");
                Assert.That(reset, Is.EqualTo(once), "a reset instance starts over");
            });
        }

        [Test]
        public void Every_section_refuses_null()
        {
            var world = Populate(Build());

            Assert.Multiple(() =>
            {
                Assert.That(() => new WorldHash().AddPeople(null!), Throws.ArgumentNullException);
                Assert.That(
                    () => new WorldHash().AddHouseholds(null!, world.People), Throws.ArgumentNullException);
                Assert.That(
                    () => new WorldHash().AddHouseholds(world.Households, null!), Throws.ArgumentNullException);
                Assert.That(() => new WorldHash().AddSettlements(null!, world.People), Throws.ArgumentNullException);
                Assert.That(() => new WorldHash().AddPending(null!), Throws.ArgumentNullException);
            });
        }

        // Span<PersonRecord> cannot be captured by a lambda, so the change is
        // a delegate taking the record by reference - which is also how a real
        // caller would corrupt one through the bulk path.
        private delegate void Mutation(ref PersonRecord record);

        private static void AssertMoves(string what, Mutation change)
        {
            var world = Populate(Build());
            var before = HashOf(world);
            var records = world.People.RecordSpan();

            for (var i = 0; i < records.Length; i++)
            {
                if (!records[i].Id.IsNone)
                {
                    change(ref records[i]);
                    break;
                }
            }

            Assert.That(HashOf(world), Is.Not.EqualTo(before), what + " is not folded into the hash");
        }

        private static DemographicWorld Build() => new DemographicWorld(Quiet(), 7UL);

        private static DemographicWorld Populate(DemographicWorld world)
        {
            var band = world.NewBand();

            foreach (var member in world.Generator.Generate(20, new WorldPosition(3, 3)).Members)
            {
                band.AddMember(member);
            }

            world.AdvanceYears(3L);
            return world;
        }

        private static ulong HashOf(DemographicWorld world) =>
            new WorldHash()
                .AddPeople(world.People)
                .AddHouseholds(world.Households, world.People)
                .AddPending(world.Clock)
                .Value;
    }
}
