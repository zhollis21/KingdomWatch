using System.Collections.Generic;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.History;
using KingdomWatch.Core.Knowledge;
using KingdomWatch.Core.Lifecycle;
using KingdomWatch.Core.Relationships;
using KingdomWatch.Core.Tests.Lifecycle;
using KingdomWatch.Core.Tests.Work;
using KingdomWatch.Core.Nomadic;
using KingdomWatch.Core.Traversal;
using KingdomWatch.Core.Validation;
using KingdomWatch.Core.Work;
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
                AssertMoves("last warmed", static (ref PersonRecord r) => r.LastWarmedAt = r.LastWarmedAt.Plus(1L));
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
        public void A_queue_that_left_as_data_and_came_back_hashes_the_same()
        {
            // The save-side half of section 17 (#15): pending events are
            // exported with their ids and restored as they were, never
            // rebuilt from entity state. A clock rebuilt from the export of
            // a lived-in world is the same clock to the hash.
            var world = Populate(Build());
            var before = new WorldHash().AddPending(world.Clock).Value;

            var exported = new List<ScheduledEvent>();
            world.Clock.CopyPendingTo(exported);
            var ids = new IdAllocator();
            ids.ResumeEventsFrom(world.Base.Ids.PeekNextEvent());
            var restored = new SimulationClock(ids, world.Clock.Now, exported);

            Assert.Multiple(() =>
            {
                Assert.That(exported, Is.Not.Empty, "a lived-in world has commitments to carry");
                Assert.That(new WorldHash().AddPending(restored).Value, Is.EqualTo(before));
            });
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
            var afterStores = new WorldHash().AddSettlements(world.Founding, world.People).Value;

            // Member order is the settlement's contract too (Settlement's own
            // comment): the same people listed differently is a different world.
            var settlement = world.Founding.All[0];
            var first = settlement.Members[0];
            settlement.RemoveMember(first);
            settlement.AddMember(first);

            Assert.Multiple(() =>
            {
                Assert.That(afterStores, Is.Not.EqualTo(before), "a settlement's stores are part of the world");
                Assert.That(
                    new WorldHash().AddSettlements(world.Founding, world.People).Value,
                    Is.Not.EqualTo(afterStores),
                    "so is the order it lists its members in");
                Assert.That(
                    () => new WorldHash().AddSettlements(world.Founding, null!),
                    Throws.ArgumentNullException);
            });
        }

        [Test]
        public void What_each_system_has_booked_is_folded_in()
        {
            // The other side of the pending section. Two runs that agree on
            // the queue and disagree on who believes they own which entry
            // have diverged; it just surfaces later, as a stream that doubled
            // or stopped.
            var bookings = Bookings(Populate(Build()));

            Assert.That(bookings, Is.Not.Empty, "nothing was booked to hash");

            var all = new WorldHash().AddBookings(bookings).Value;
            var fewer = bookings.GetRange(0, bookings.Count - 1);

            Assert.Multiple(() =>
            {
                Assert.That(new WorldHash().AddBookings(fewer).Value, Is.Not.EqualTo(all));
                Assert.That(() => new WorldHash().AddBookings(null!), Throws.ArgumentNullException);
            });
        }

        [Test]
        public void The_order_bookings_arrive_in_is_not_part_of_the_hash()
        {
            // They are gathered from several systems, so the order they
            // arrive in is an artefact of the caller rather than of the
            // world. AddBookings sorts them into their own canonical order
            // before folding any in.
            var forward = Bookings(Populate(Build()));

            Assert.That(forward, Has.Count.GreaterThan(1), "one booking cannot be reordered");

            var backward = new List<PendingBooking>(forward);
            backward.Reverse();

            Assert.That(
                new WorldHash().AddBookings(backward).Value,
                Is.EqualTo(new WorldHash().AddBookings(forward).Value));
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
        public void Wandering_bands_and_their_state_are_folded_in()
        {
            // A band is the world's only community until it settles, and its
            // stores, route and leader drive every decision it makes next.
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, 30);
            var adults = w.JoinAdults(band, 4);
            band.Leader = adults[0];
            var bands = new List<MobileGroup> { band };

            ulong Hash() => new WorldHash().AddBands(bands, w.People).Value;

            var before = Hash();
            var changes = new List<(string What, ulong Hash)>();

            band.Position = WorkWorld.ForestCell;
            changes.Add(("position", Hash()));
            band.Position = WorkWorld.Camp;

            band.Destination = WorkWorld.HillsCell;
            changes.Add(("destination", Hash()));

            // The origin cell is what an unset destination would fold in as,
            // so heading there must still differ from heading nowhere.
            band.Destination = new WorldPosition(0, 0);
            changes.Add(("destination at the origin", Hash()));
            band.Destination = null;

            band.Leader = adults[1];
            changes.Add(("leader", Hash()));
            band.Leader = adults[0];

            band.SharedSupplies.Gather(ResourceKind.Wood, 1);
            changes.Add(("supplies", Hash()));

            Assert.Multiple(() =>
            {
                foreach (var (what, hash) in changes)
                {
                    Assert.That(hash, Is.Not.EqualTo(before), what);
                }

                Assert.That(new WorldHash().AddBands(new List<MobileGroup>(), w.People).Value, Is.Not.EqualTo(before));
            });
        }

        [Test]
        public void A_band_s_member_order_is_part_of_the_hash()
        {
            // Hunger feeds, Jobs picks and Matchmaking proposes in member
            // order (MobileGroup's own contract), so the same people listed
            // differently is a different world. The #103 review.
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, 0);
            var adults = w.JoinAdults(band, 3);
            var bands = new List<MobileGroup> { band };
            var before = new WorldHash().AddBands(bands, w.People).Value;

            band.RemoveMember(adults[0]);
            band.AddMember(adults[0]);

            Assert.That(new WorldHash().AddBands(bands, w.People).Value, Is.Not.EqualTo(before));
        }

        [Test]
        public void A_household_s_member_order_is_part_of_the_hash()
        {
            // Fertility takes the first eligible couple in household order.
            var world = Populate(Build());
            Household? household = null;

            foreach (var candidate in world.Households.All)
            {
                if (candidate.Members.Count >= 3)
                {
                    household = candidate;
                    break;
                }
            }

            Assert.That(household, Is.Not.Null, "the populated world has a family of three or more");
            var before = new WorldHash().AddHouseholds(world.Households, world.People).Value;
            var first = household!.Members[0];

            world.Households.Leave(first);
            world.Households.Join(household, first);

            Assert.That(
                new WorldHash().AddHouseholds(world.Households, world.People).Value,
                Is.Not.EqualTo(before));
        }

        [Test]
        public void The_order_bands_arrive_in_is_not_part_of_the_hash()
        {
            var w = new WorkWorld();
            var first = w.NewBand(WorkWorld.Camp, 0);
            var second = w.NewBand(WorkWorld.HillsCell, 0);

            Assert.That(
                new WorldHash().AddBands(new List<MobileGroup> { second, first }, w.People).Value,
                Is.EqualTo(new WorldHash().AddBands(new List<MobileGroup> { first, second }, w.People).Value));
        }

        [Test]
        public void Known_maps_are_folded_in_whatever_order_holders_were_tracked()
        {
            // The maps live in a dictionary, whose order is the one thing the
            // hash must never read. Two stores holding the same knowledge,
            // tracked the other way round, have to agree.
            var grid = WorkWorld.DefaultMap();
            var west = new EntityId(EntityKind.MobileGroup, 1UL);
            var east = new EntityId(EntityKind.MobileGroup, 2UL);

            KnownMaps Maps(params EntityId[] order)
            {
                var maps = new KnownMaps(grid);

                foreach (var holder in order)
                {
                    maps.Track(holder);
                }

                maps.Reveal(west, WorkWorld.Camp, 2);
                maps.Reveal(east, WorkWorld.HillsCell, 2);
                return maps;
            }

            var forward = Maps(west, east);
            var backward = Maps(east, west);
            var before = new WorldHash().AddKnownMaps(forward).Value;
            forward.Reveal(west, new WorldPosition(WorkWorld.Width - 1, WorkWorld.Height - 1), 0);

            Assert.Multiple(() =>
            {
                Assert.That(new WorldHash().AddKnownMaps(backward).Value, Is.EqualTo(before), "tracking order is not the world");
                Assert.That(new WorldHash().AddKnownMaps(forward).Value, Is.Not.EqualTo(before), "a cell seen is");
            });
        }
        [Test]
        public void Terrain_is_folded_in_cell_by_cell()
        {
            // The pathfinder and Jobs read it, worldgen draws it from the
            // seed, and bridges (#35) will rewrite it: a world that disagrees
            // on one cell has diverged. The #103 review.
            var grid = WorkWorld.DefaultMap();
            var before = new WorldHash().AddTerrain(grid).Value;

            grid.Set(new WorldPosition(WorkWorld.Width - 1, WorkWorld.Height - 1), TerrainKind.Hills);

            Assert.That(new WorldHash().AddTerrain(grid).Value, Is.Not.EqualTo(before));
        }

        [Test]
        public void The_next_ids_to_be_handed_out_are_folded_in()
        {
            // The next person, household or event id depends on these, and
            // they belong to no system: a world that has handed out one more
            // id than another has diverged even before the id is seen.
            var ids = new IdAllocator();
            var before = new WorldHash().AddIds(ids).Value;
            ids.Next(EntityKind.Animal);
            var afterEntity = new WorldHash().AddIds(ids).Value;
            ids.NextEvent();
            var afterEvent = new WorldHash().AddIds(ids).Value;

            Assert.Multiple(() =>
            {
                Assert.That(afterEntity, Is.Not.EqualTo(before), "an entity id handed out");
                Assert.That(afterEvent, Is.Not.EqualTo(afterEntity), "an event id handed out");
            });
        }

        [Test]
        public void Partnerships_are_folded_in_whatever_order_they_were_formed()
        {
            // Who is partnered with whom is read by Fertility and Matchmaking,
            // and households hash their members, not the links (#104).
            var ids = new IdAllocator();
            var aldric = ids.Next(EntityKind.Person);
            var mira = ids.Next(EntityKind.Person);
            var bram = ids.Next(EntityKind.Person);
            var tove = ids.Next(EntityKind.Person);
            var first = ids.NextEvent();
            var second = ids.NextEvent();

            var forward = new Partnerships();
            forward.Form(aldric, mira, first, SimulationTime.FromDays(1));
            forward.Form(bram, tove, second, SimulationTime.FromDays(2));

            var backward = new Partnerships();
            backward.Form(bram, tove, second, SimulationTime.FromDays(2));
            backward.Form(aldric, mira, first, SimulationTime.FromDays(1));

            var before = new WorldHash().AddPartnerships(forward).Value;
            forward.End(aldric, mira, ids.NextEvent(), SimulationTime.FromDays(3));

            Assert.Multiple(() =>
            {
                Assert.That(new WorldHash().AddPartnerships(backward).Value, Is.EqualTo(before), "forming order is not the world");
                Assert.That(new WorldHash().AddPartnerships(forward).Value, Is.Not.EqualTo(before), "an ending is");
                Assert.That(new WorldHash().AddPartnerships(new Partnerships()).Value, Is.Not.EqualTo(before), "and so is having none");
            });
        }

        [Test]
        public void Genealogy_is_folded_in_parents_and_children_alike()
        {
            // Founders recorded in either order are the same tree; a child
            // recorded, or two siblings recorded the other way round, is not.
            var ids = new IdAllocator();
            var mother = ids.Next(EntityKind.Person);
            var father = ids.Next(EntityKind.Person);
            var elder = ids.Next(EntityKind.Person);
            var younger = ids.Next(EntityKind.Person);

            Genealogy Tree(bool foundersForward, params EntityId[] children)
            {
                var tree = new Genealogy();
                tree.Record(foundersForward ? mother : father, EntityId.None, EntityId.None);
                tree.Record(foundersForward ? father : mother, EntityId.None, EntityId.None);

                foreach (var child in children)
                {
                    tree.Record(child, mother, father);
                }

                return tree;
            }

            var before = new WorldHash().AddGenealogy(Tree(true, elder)).Value;

            Assert.Multiple(() =>
            {
                Assert.That(new WorldHash().AddGenealogy(Tree(false, elder)).Value, Is.EqualTo(before), "founding order is not the world");
                Assert.That(new WorldHash().AddGenealogy(Tree(true, elder, younger)).Value, Is.Not.EqualTo(before), "a child recorded");
                Assert.That(
                    new WorldHash().AddGenealogy(Tree(true, younger, elder)).Value,
                    Is.Not.EqualTo(new WorldHash().AddGenealogy(Tree(true, elder, younger)).Value),
                    "children are kept in the order they were recorded");
            });
        }

        [Test]
        public void Memories_are_folded_in_with_their_witnesses()
        {
            // Nothing writes one in M1; the section is there for the first
            // thing that does (#104).
            var settings = new MemorySettings(4, SimulationTime.TicksPerDay, SimulationTime.TicksPerDay * 10, 4);
            var ids = new IdAllocator();
            var oakshire = ids.Next(EntityKind.Settlement);
            var mira = ids.Next(EntityKind.Person);
            var bram = ids.Next(EntityKind.Person);
            var raid = ids.NextEvent();
            var feast = ids.NextEvent();

            Memories Remember(bool oakshireFirst)
            {
                var memories = new Memories(settings);
                var witnesses = new[] { mira };

                if (oakshireFirst)
                {
                    memories.Record(oakshire, raid, bram, -40, witnesses, SimulationTime.Zero);
                    memories.Record(mira, feast, bram, 10, witnesses, SimulationTime.Zero);
                }
                else
                {
                    memories.Record(mira, feast, bram, 10, witnesses, SimulationTime.Zero);
                    memories.Record(oakshire, raid, bram, -40, witnesses, SimulationTime.Zero);
                }

                return memories;
            }

            var remembered = Remember(true);
            var before = new WorldHash().AddMemories(remembered).Value;
            var empty = new WorldHash().AddMemories(new Memories(settings)).Value;
            remembered.Teach(oakshire, raid, bram);

            Assert.Multiple(() =>
            {
                Assert.That(before, Is.Not.EqualTo(empty), "something remembered");
                Assert.That(new WorldHash().AddMemories(Remember(false)).Value, Is.EqualTo(before), "recording order across holders is not the world");
                Assert.That(new WorldHash().AddMemories(remembered).Value, Is.Not.EqualTo(before), "a witness taught");
            });
        }

        [Test]
        public void Work_in_hand_and_each_band_s_working_day_are_folded_in()
        {
            // A task's route and timings, and the band's dawn counts and site
            // search, none of which reaches the queue (#104). Driven through
            // Jobs' own handlers, the way a run changes them.
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, WorkWorld.PlentifulFood(2));
            var adults = w.JoinAdults(band, 2);

            ulong Hash() => new WorldHash().AddWork(w.Jobs, w.People).Value;

            var tracked = Hash();
            w.AdvanceToDawn();
            var working = Hash();
            w.Jobs.Vacate(adults[0]);
            var vacated = Hash();

            // Moved by hand after dawn: the next pick refreshes the search
            // from here, and until it does the old one is what is held.
            band.Position = WorkWorld.ForestCell;
            var moved = Hash();
            w.Jobs.RefreshSites(band);
            var refreshed = Hash();

            Assert.Multiple(() =>
            {
                Assert.That(working, Is.Not.EqualTo(tracked), "dawn assigned tasks and found sites");
                Assert.That(vacated, Is.Not.EqualTo(working), "a task given up");
                Assert.That(moved, Is.EqualTo(vacated), "the band's position is the bands section's, not this one's");
                Assert.That(refreshed, Is.Not.EqualTo(moved), "a fresh site search");
            });
        }

        [Test]
        public void Work_is_folded_in_whatever_order_bands_were_tracked()
        {
            static ulong HashWith(bool westFirst)
            {
                var w = new WorkWorld();
                var ids = w.Demographics.Base.Ids;
                var west = new MobileGroup(ids.Next(EntityKind.MobileGroup), MobileGroupPurpose.NomadicBand, WorkWorld.Camp);
                var east = new MobileGroup(ids.Next(EntityKind.MobileGroup), MobileGroupPurpose.NomadicBand, WorkWorld.HillsCell);

                foreach (var band in westFirst ? new[] { west, east } : new[] { east, west })
                {
                    w.KnownMaps.Track(band.Id);
                    w.KnownMaps.Reveal(band.Id, band.Position, Jobs.RevealRadius);
                    w.Jobs.Track(band);
                }

                w.Join(west, 30L);
                w.Join(east, 30L);
                w.AdvanceToDawn();
                return new WorldHash().AddWork(w.Jobs, w.People).Value;
            }

            Assert.That(HashWith(false), Is.EqualTo(HashWith(true)));
        }

        [Test]
        public void Tasks_are_folded_in_by_worker_id_not_by_storage_slot()
        {
            // The same two people, stored in each other's slots: a slot index
            // is representation (section 5), so the worlds must agree. Member
            // order is behaviour and is kept the same in both.
            static ulong HashWith(bool firstInFirstSlot)
            {
                var w = new WorkWorld();
                var band = w.NewBand(WorkWorld.Camp, 0);
                WorkWorld.FillWoodAndStone(band);
                var first = new EntityId(EntityKind.Person, 1_000UL);
                var second = new EntityId(EntityKind.Person, 1_001UL);
                var bornTick = w.Now.Ticks - (30L * SimulationTime.TicksPerYear);

                PersonHandle Add(EntityId id) =>
                    w.People.Add(id, WorkWorld.Camp, 100, AgeStage.Adult, Sex.Male, 0, 0, w.Now, bornTick);

                var one = firstInFirstSlot ? Add(first) : Add(second);
                var other = firstInFirstSlot ? Add(second) : Add(first);
                band.AddMember(firstInFirstSlot ? one : other);
                band.AddMember(firstInFirstSlot ? other : one);
                w.AdvanceToDawn();
                Assert.That(w.Jobs.HasTask(one) && w.Jobs.HasTask(other), Is.True, "both out foraging");
                return new WorldHash().AddWork(w.Jobs, w.People).Value;
            }

            Assert.That(HashWith(false), Is.EqualTo(HashWith(true)));
        }

        [Test]
        public void Famine_is_folded_in_whatever_order_communities_were_tracked()
        {
            static ulong HashWith(bool westFirst)
            {
                var w = new WorkWorld();
                var ids = w.Demographics.Base.Ids;
                var west = new MobileGroup(ids.Next(EntityKind.MobileGroup), MobileGroupPurpose.NomadicBand, WorkWorld.Camp);
                var east = new MobileGroup(ids.Next(EntityKind.MobileGroup), MobileGroupPurpose.NomadicBand, WorkWorld.HillsCell);

                foreach (var band in westFirst ? new[] { west, east } : new[] { east, west })
                {
                    w.Hunger.Track(band, inFamine: band == west);
                }

                return new WorldHash().AddFamine(w.Hunger).Value;
            }

            Assert.That(HashWith(false), Is.EqualTo(HashWith(true)));
        }

        [Test]
        public void A_band_s_council_is_folded_in()
        {
            // Pressure and days at camp decide when a band moves and settles
            // (#104); a council adds to both.
            var w = new WorkWorld();
            var band = w.NewWanderingBand(WorkWorld.Camp, WorkWorld.PlentifulFood(1));
            w.JoinAdults(band, 1);

            ulong Hash() => new WorldHash().AddCouncils(w.Nomads).Value;

            var before = Hash();
            w.AdvanceToFirstLight();

            Assert.Multiple(() =>
            {
                Assert.That(w.Nomads.DaysAtCamp(band), Is.EqualTo(1), "the council sat");
                Assert.That(Hash(), Is.Not.EqualTo(before));
            });
        }

        [Test]
        public void Councils_are_folded_in_whatever_order_bands_were_tracked()
        {
            static ulong HashWith(bool westFirst)
            {
                var w = new WorkWorld();
                var ids = w.Demographics.Base.Ids;
                var west = new MobileGroup(ids.Next(EntityKind.MobileGroup), MobileGroupPurpose.NomadicBand, WorkWorld.Camp);
                var east = new MobileGroup(ids.Next(EntityKind.MobileGroup), MobileGroupPurpose.NomadicBand, WorkWorld.HillsCell);
                w.Join(west, 30L);
                w.Join(west, 31L);
                w.Join(east, 30L);

                foreach (var band in westFirst ? new[] { west, east } : new[] { east, west })
                {
                    w.Nomads.Track(band);
                }

                w.AdvanceToFirstLight();
                return new WorldHash().AddCouncils(w.Nomads).Value;
            }

            Assert.That(HashWith(false), Is.EqualTo(HashWith(true)));
        }

        [Test]
        public void Famine_is_folded_in()
        {
            // The flag decides whether the next meal publishes FamineStarted
            // or FamineEnded (#104). A band with no food and nobody old enough
            // to forage, fed through Hunger's own handler, is the way a run
            // sets it.
            var w = new WorkWorld();
            var hungry = w.NewBand(WorkWorld.Camp, 0);
            var fed = w.NewBand(WorkWorld.HillsCell, WorkWorld.PlentifulFood(1));
            w.Join(hungry, 8L);
            w.JoinAdults(fed, 1);

            ulong Hash() => new WorldHash().AddFamine(w.Hunger).Value;

            var before = Hash();
            w.Advance(SimulationTime.TicksPerDay);

            Assert.Multiple(() =>
            {
                Assert.That(w.Hunger.IsInFamine(hungry), Is.True, "the empty band went hungry");
                Assert.That(w.Hunger.IsInFamine(fed), Is.False);
                Assert.That(Hash(), Is.Not.EqualTo(before));
            });
        }

        [Test]
        public void The_recorded_history_is_folded_in()
        {
            // The count and the digest both: two histories of the same length
            // that differ in one event are different worlds.
            static EventJournal Journal(params DomainEventKind[] kinds)
            {
                var journal = new EventJournal(4);

                for (var i = 0; i < kinds.Length; i++)
                {
                    journal.On(new DomainEvent(
                        new EventId((ulong)i + 1UL), SimulationTime.Zero, kinds[i],
                        new EntityId(EntityKind.Person, 1UL), EntityId.None, Reasons.None));
                }

                return journal;
            }

            var born = new WorldHash().AddJournal(Journal(DomainEventKind.PersonBorn)).Value;

            Assert.Multiple(() =>
            {
                Assert.That(new WorldHash().AddJournal(Journal()).Value, Is.Not.EqualTo(born), "nothing recorded");
                Assert.That(new WorldHash().AddJournal(Journal(DomainEventKind.PersonDied)).Value, Is.Not.EqualTo(born), "another event");
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
                Assert.That(() => new WorldHash().AddBands(null!, world.People), Throws.ArgumentNullException);
                Assert.That(() => new WorldHash().AddBands(new List<MobileGroup>(), null!), Throws.ArgumentNullException);
                Assert.That(() => new WorldHash().AddKnownMaps(null!), Throws.ArgumentNullException);
                Assert.That(() => new WorldHash().AddTerrain(null!), Throws.ArgumentNullException);
                Assert.That(() => new WorldHash().AddIds(null!), Throws.ArgumentNullException);
                Assert.That(() => new WorldHash().AddPartnerships(null!), Throws.ArgumentNullException);
                Assert.That(() => new WorldHash().AddGenealogy(null!), Throws.ArgumentNullException);
                Assert.That(() => new WorldHash().AddMemories(null!), Throws.ArgumentNullException);
                Assert.That(() => new WorldHash().AddWork(null!, world.People), Throws.ArgumentNullException);
                Assert.That(() => new WorldHash().AddWork(new WorkWorld().Jobs, null!), Throws.ArgumentNullException);
                Assert.That(() => new WorldHash().AddCouncils(null!), Throws.ArgumentNullException);
                Assert.That(() => new WorldHash().AddFamine(null!), Throws.ArgumentNullException);
                Assert.That(() => new WorldHash().AddJournal(null!), Throws.ArgumentNullException);
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

        // Two systems' worth, so the order they are concatenated in is a
        // choice this test makes rather than one the world made.
        private static List<PendingBooking> Bookings(DemographicWorld world)
        {
            var gathered = new List<PendingBooking>();
            var scratch = new List<PendingBooking>();

            world.Hunger.CopyBookingsTo(scratch);
            gathered.AddRange(scratch);
            world.Fertility.CopyBookingsTo(scratch);
            gathered.AddRange(scratch);
            world.Matchmaking.CopyBookingsTo(scratch);
            gathered.AddRange(scratch);

            return gathered;
        }

        private static ulong HashOf(DemographicWorld world) =>
            new WorldHash()
                .AddPeople(world.People)
                .AddHouseholds(world.Households, world.People)
                .AddPending(world.Clock)
                .Value;
    }
}
