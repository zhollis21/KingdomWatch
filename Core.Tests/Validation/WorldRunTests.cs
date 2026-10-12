using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Lifecycle;
using KingdomWatch.Core.Settlements;
using KingdomWatch.Core.Traversal;
using KingdomWatch.Core.Validation;
using KingdomWatch.Core.Work;
using KingdomWatch.Harness;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Validation
{
    /// <summary>
    /// The M1 world (#17), run the way the harness runs it. Section 19 asks
    /// for population stability and milestone firing; section 5 for a world
    /// that holds its invariants across seeds.
    /// </summary>
    /// <remarks>
    /// Three seeds for sixty years rather than the harness's eight for two
    /// hundred: settling, the first decades of building and farming, and
    /// the winters that test them all fall inside it. Villages that live
    /// (#69) made the century of eight worlds too slow to run on every test
    /// pass. The full run is <c>KingdomWatch.Harness --seeds 8 --years 200</c>.
    ///
    /// Since #69, #147 and #149 a village's growth is held by its houses, its
    /// forest and its food, and a short winter shrinks it rather than
    /// killing it whole, so survival and a ceiling are asserted.
    ///
    /// This replaced the 30-year fixture sweeps: the validator checks every
    /// rule they checked, after every year, on the world the harness runs.
    /// </remarks>
    [TestFixture]
    public sealed class WorldRunTests
    {
        private const int Seeds = 3;
        private const long Years = 60L;

        // On 8 seeds no village passes 121 by year 75. Villages no longer
        // level off: by year 200 the largest hold several hundred and are
        // still growing (#163), so this bounds the test's sixty years only.
        private const int PopulationCeiling = 200;

        // Each seed run once for the fixture: the tests over them read the
        // same runs, and the worlds' decades are the costly part.
        private static readonly List<WorldRun> Runs = new List<WorldRun>();

        [OneTimeSetUp]
        public void RunEverySeed()
        {
            if (Runs.Count > 0)
            {
                return;
            }

            for (var seed = 1UL; seed <= Seeds; seed++)
            {
                Runs.Add(new WorldRun(seed).RunYears(Years));
            }
        }

        [Test]
        public void Every_seed_holds_its_invariants_every_year()
        {
            var report = new StringBuilder();

            foreach (var run in Runs)
            {
                if (!run.IsClean)
                {
                    report.AppendLine(run.Failure);
                }
            }

            Assert.That(report.ToString(), Is.Empty);
        }

        [Test]
        public void Every_village_lives_and_stays_under_a_ceiling()
        {
            // A short winter is rationed rather than eaten through (#69,
            // #147, #149), and each village is held to it on its own, so a
            // growing neighbour cannot hide one dying out (#163).
            Assert.Multiple(() =>
            {
                foreach (var run in Runs)
                {
                    var seed = "seed " + run.World.Seed;
                    Assert.That(run.DiedOut, Is.Empty, seed + ": a village died out");

                    foreach (var year in run.Years)
                    {
                        foreach (var village in year.Villages)
                        {
                            Assert.That(village.Living, Is.LessThanOrEqualTo(PopulationCeiling), seed + " " + village.Village + ", year " + year.Year);
                        }
                    }
                }
            });
        }

        [Test]
        public void Each_village_s_numbers_add_up_year_on_year()
        {
            // Nobody moves between villages in M1, so a village's people
            // last year, plus its births, less its deaths, are its people
            // now. Every birth and death counted for the wrong village - a
            // newborn not given their mother's - breaks it somewhere (#163).
            Assert.Multiple(() =>
            {
                foreach (var run in Runs)
                {
                    var before = run.Founding;

                    foreach (var year in run.Years)
                    {
                        foreach (var start in before)
                        {
                            var now = year.Of(start.Village);
                            Assert.That(now.Living, Is.EqualTo(start.Living + now.Tally.Births - now.Tally.Deaths), "seed " + run.World.Seed + " " + start.Village + ", year " + year.Year);
                        }

                        Assert.That(year.Villages, Has.Count.EqualTo(before.Count), "seed " + run.World.Seed + ": no village appears in M1");
                        before = year.Villages;
                    }
                }
            });
        }

        [Test]
        public void No_year_s_hunger_and_cold_take_a_quarter_of_a_village()
        {
            // A village that runs short shrinks; before #149 one winter took
            // nearly everyone in it. Each village against its own size, so a
            // big one cannot hide a small one's winter (the #156 review). A
            // village of fewer than ten is too small for a share to mean
            // anything: one death is a tenth of it.
            Assert.Multiple(() =>
            {
                foreach (var run in Runs)
                {
                    var before = run.Founding;

                    foreach (var year in run.Years)
                    {
                        foreach (var start in before)
                        {
                            var lost = year.Of(start.Village).Tally.Starved + year.Of(start.Village).Tally.Froze;

                            if (start.Living >= 10)
                            {
                                Assert.That(lost * 4, Is.LessThan(start.Living), "seed " + run.World.Seed + " " + start.Village + ", year " + year.Year + ": " + lost + " of " + start.Living);
                            }
                        }

                        before = year.Villages;
                    }
                }
            });
        }

        [Test]
        public void The_milestones_M1_can_reach_fire_on_every_seed()
        {
            // Economy ladder section 9's first milestone: since #146 the
            // world starts with its villages standing, and none of them was
            // ever a wandering camp.
            Assert.Multiple(() =>
            {
                foreach (var run in Runs)
                {
                    Assert.That(run.FirstSettlement, Is.EqualTo(SimulationTime.Zero), "seed " + run.World.Seed + ": villages at world start");
                    Assert.That(run.FirstCamp, Is.Null, "seed " + run.World.Seed + ": a starting village never camped");
                }
            });
        }

        [Test]
        public void A_run_a_day_at_a_time_calls_back_each_day_and_still_validates()
        {
            var days = 0;
            var run = new WorldRun(1UL).RunYears(1L, world => days++);
            var plain = new WorldRun(1UL).RunYears(1L);

            Assert.Multiple(() =>
            {
                Assert.That(days, Is.EqualTo(SimulationTime.DaysPerYear));
                Assert.That(run.Years, Has.Count.EqualTo(1));
                Assert.That(run.IsClean, Is.True);
                Assert.That(run.Hash(), Is.EqualTo(plain.Hash()), "a callback that changes nothing changes nothing");
            });
        }

        [Test]
        public void The_same_seed_reaches_the_same_hash_and_another_does_not()
        {
            var first = new WorldRun(3UL).RunYears(10L).Hash();
            var again = new WorldRun(3UL).RunYears(10L).Hash();
            var other = new WorldRun(4UL).RunYears(10L).Hash();

            Assert.Multiple(() =>
            {
                Assert.That(again, Is.EqualTo(first));
                Assert.That(other, Is.Not.EqualTo(first));
            });
        }

        [Test]
        public void The_run_s_hash_sees_a_wandering_band_s_stores_and_its_map()
        {
            // A band that splits off is its own state until it settles.
            // Copilot's review of #103: the run's hash left it out. The world
            // starts with villages only (#146), so the band is added.
            var world = World.M1(1UL);
            var band = world.AddBand(12, world.Founding.All[0].Position);
            var run = new WorldRun(world);
            var before = run.Hash();

            band.SharedSupplies.Gather(ResourceKind.Wood, 1);
            var afterStores = run.Hash();
            run.World.KnownMaps.Reveal(band.Id, new WorldPosition(0, 0), 0);
            var afterMap = run.Hash();

            Assert.Multiple(() =>
            {
                Assert.That(afterStores, Is.Not.EqualTo(before), "stores");
                Assert.That(afterMap, Is.Not.EqualTo(afterStores), "map");
            });
        }

        [Test]
        public void The_run_s_hash_sees_the_terrain_and_the_next_ids()
        {
            var run = new WorldRun(1UL);
            var before = run.Hash();

            run.World.Grid.Set(new WorldPosition(0, 0), TerrainKind.Rocks);
            var afterTerrain = run.Hash();
            run.World.Ids.Next(EntityKind.Animal);

            Assert.Multiple(() =>
            {
                Assert.That(afterTerrain, Is.Not.EqualTo(before), "terrain");
                Assert.That(run.Hash(), Is.Not.EqualTo(afterTerrain), "next ids");
            });
        }

        [Test]
        public void The_run_s_hash_sees_each_relationship_store_and_the_history()
        {
            // The #104 sweep, over the systems a caller can change on their
            // own. Ids and event ids are made up rather than allocated: the
            // allocator is hashed too, and handing one out would move the hash
            // whether or not the store under test was folded in.
            var run = new WorldRun(1UL).RunYears(1L);
            var world = run.World;
            var stranger = new EntityId(EntityKind.Person, 1_000_000UL);
            var another = new EntityId(EntityKind.Person, 1_000_001UL);
            var hashes = new List<(string What, ulong Hash)> { ("before", run.Hash()) };

            world.Partnerships.Form(stranger, another, new EventId(1_000_000UL), world.Now);
            hashes.Add(("partnerships", run.Hash()));
            world.Genealogy.Record(stranger, EntityId.None, EntityId.None);
            hashes.Add(("genealogy", run.Hash()));
            world.Memories.Record(stranger, new EventId(1_000_001UL), another, 5, ReadOnlySpan<EntityId>.Empty, world.Now);
            hashes.Add(("memories", run.Hash()));
            world.Journal.On(new DomainEvent(
                new EventId(1_000_002UL), world.Now, DomainEventKind.PersonBorn, stranger, EntityId.None, Reasons.None));
            hashes.Add(("journal", run.Hash()));

            Assert.Multiple(() =>
            {
                for (var i = 1; i < hashes.Count; i++)
                {
                    Assert.That(hashes[i].Hash, Is.Not.EqualTo(hashes[i - 1].Hash), hashes[i].What);
                }
            });
        }

        [Test]
        public void The_run_s_hash_sees_work_councils_and_famine_as_the_run_changes_them()
        {
            // The rest of the #104 sweep. Jobs, councils and famine have no
            // writer of their own - every public way to change them changes a
            // section that was already hashed too - so each is watched on its
            // own, day by day through a live run, and has to be seen to move
            // on a day its system acted. A section missing from the run's
            // hash is caught by The_run_s_hash_is_every_section_in_order.
            //
            // The run is left to itself except for its first month, when
            // every community's food is taken each morning: a well-fed run
            // need never starve (seed 1 does not in ten years), and the famine
            // flag has to be seen to flip both ways through Hunger's own meal.
            // The world starts with villages only (#146), so a band is added
            // for the councils to be seen.
            var world = World.M1(1UL);
            world.AddBand(12, world.Founding.All[0].Position);
            var run = new WorldRun(world);
            var workMoved = false;
            var councilMoved = false;
            var famineStarted = 0;
            var famineEnded = 0;
            var famineMissed = new List<string>();
            var communities = new List<ICommunity>();

            for (var day = 0; day < SimulationTime.DaysPerYear; day++)
            {
                if (day < 30)
                {
                    communities.Clear();
                    world.CopyCommunitiesTo(communities);

                    foreach (var band in communities)
                    {
                        var food = band.SharedSupplies.Available(ResourceKind.Food);

                        if (food > 0)
                        {
                            band.SharedSupplies.Consume(ResourceKind.Food, food);
                        }
                    }
                }

                var work = new WorldHash().AddWork(world.Jobs, world.People).Value;
                var council = new WorldHash().AddCouncils(world.Nomads).Value;
                var famine = new WorldHash().AddFamine(world.Hunger).Value;
                world.Hunger.CopyTrackedTo(communities);
                var trackedBefore = communities.Count;
                var published = world.Journal.Count;

                world.Advance(SimulationTime.TicksPerDay);

                workMoved |= new WorldHash().AddWork(world.Jobs, world.People).Value != work;
                councilMoved |= new WorldHash().AddCouncils(world.Nomads).Value != council;
                world.Hunger.CopyTrackedTo(communities);

                if (communities.Count != trackedBefore || !FamineTurned(world, published, ref famineStarted, ref famineEnded))
                {
                    continue;
                }

                if (new WorldHash().AddFamine(world.Hunger).Value == famine)
                {
                    famineMissed.Add("day " + day);
                }
            }

            Assert.Multiple(() =>
            {
                Assert.That(workMoved, Is.True, "work");
                Assert.That(councilMoved, Is.True, "councils");
                Assert.That(famineStarted, Is.GreaterThan(0), "no famine started to watch");
                Assert.That(famineEnded, Is.GreaterThan(0), "no famine ended to watch");
                Assert.That(famineMissed, Is.Empty, "famine turned and the section did not move");
            });
        }

        [Test]
        public void Validation_checks_the_communities_deaths_and_the_nomads_track()
        {
            // Every system's tracked set is checked for members the store no
            // longer holds; Deaths and NomadicBands were the two left out (the
            // #108 review). A stray community each one tracks alone, listing
            // someone nobody holds, has to be reported.
            static string? FailureWith(Action<World, MobileGroup> track)
            {
                var run = new WorldRun(1UL);
                var world = run.World;
                var stray = new MobileGroup(
                    new EntityId(EntityKind.MobileGroup, 1_000_000UL), MobileGroupPurpose.NomadicBand, world.Founding.All[0].Position);
                stray.AddMember(new PersonHandle(1_000_000, 1));
                track(world, stray);
                run.RunYears(1L);
                return run.Failure;
            }

            Assert.Multiple(() =>
            {
                Assert.That(FailureWith(static (world, stray) => world.Deaths.Track(stray)), Is.Not.Null.And.Contain("CommunityMemberMissing"), "deaths");
                Assert.That(FailureWith(static (world, stray) => world.Nomads.Track(stray)), Is.Not.Null.And.Contain("CommunityMemberMissing"), "nomads");
            });
        }

        [Test]
        public void The_run_s_hash_is_every_section_in_order()
        {
            // Wiring, pinned: the run's hash is WorldHash over every system
            // the world holds. A system given a section and never added here
            // would pass its own tests and still be unhashed (#104).
            var run = new WorldRun(2UL).RunYears(1L);
            var world = run.World;
            var bookings = new List<PendingBooking>();
            var tracked = new List<ICommunity>();
            var bands = new List<MobileGroup>();
            world.CopyBookingsTo(bookings);
            world.Nomads.CopyTrackedTo(tracked);

            foreach (var band in tracked)
            {
                bands.Add((MobileGroup)band);
            }

            var expected = new WorldHash()
                .AddTerrain(world.Grid)
                .AddIds(world.Ids)
                .AddPeople(world.People)
                .AddHouseholds(world.Households, world.People)
                .AddSettlements(world.Founding, world.People)
                .AddBands(bands, world.People)
                .AddKnownMaps(world.KnownMaps)
                .AddLand(world.Land)
                .AddBuildings(world.Buildings)
                .AddPartnerships(world.Partnerships)
                .AddGenealogy(world.Genealogy)
                .AddMemories(world.Memories)
                .AddWork(world.Jobs, world.People)
                .AddCouncils(world.Nomads)
                .AddFamine(world.Hunger)
                .AddJournal(world.Journal)
                .AddTracking(world.Deaths, world.Fertility, world.Warmth, world.Matchmaking)
                .AddBookings(bookings)
                .AddPending(world.Clock)
                .Value;

            Assert.That(run.Hash(), Is.EqualTo(expected));
        }

        [Test]
        public void The_world_starts_with_its_villages_standing_two_each_side_of_the_river()
        {
            var world = World.M1(5UL);
            var communities = new List<ICommunity>();
            world.CopyCommunitiesTo(communities);

            Assert.That(communities, Has.Count.EqualTo(2 * World.M1VillagesPerSide));

            Assert.Multiple(() =>
            {
                Assert.That(world.Nomads.TrackedCount, Is.Zero, "nobody wanders at world start");

                for (var i = 0; i < communities.Count; i++)
                {
                    var village = communities[i];
                    var river = RiverColumn(world.Grid, village.Position.Y);
                    Assert.That(village, Is.InstanceOf<Settlement>());
                    Assert.That(village.Members, Has.Count.EqualTo(World.M1VillageSize));
                    Assert.That(village.Position.X < river, Is.EqualTo(i < World.M1VillagesPerSide), "west first, then east");
                    Assert.That(world.Pathfinder.IsPassable(village.Position, Jobs.Mover), Is.True);
                }
            });
        }

        [Test]
        public void The_bookings_gathered_cover_every_stream()
        {
            // The #97 review note on #17: the hash folds in whichever bookings
            // its caller gathers, so the world's gathering must reach every
            // stream. A day in, with a band wandering beside the starting
            // villages, every stream has booked something.
            var world = World.M1(1UL);
            world.AddBand(12, world.Founding.All[0].Position);
            world.Advance(SimulationTime.TicksPerDay);
            var bookings = new List<PendingBooking>();
            world.CopyBookingsTo(bookings);

            var kinds = new HashSet<ScheduledEventKind>();

            foreach (var booking in bookings)
            {
                kinds.Add(booking.Kind);
            }

            Assert.That(kinds, Is.SupersetOf(new[]
            {
                ScheduledEventKind.MealDue,
                ScheduledEventKind.WarmthDue,
                ScheduledEventKind.WorkDayDue,
                ScheduledEventKind.CouncilDue,
                ScheduledEventKind.BirthCheck,
                ScheduledEventKind.CourtshipDue,
            }));
        }

        [Test]
        public void A_run_refuses_a_missing_world_and_counts_a_band_where_there_is_no_river()
        {
            // Villages are counted wherever they stand; sides of a river were
            // what needed one (#163).
            var riverless = new World(1UL, new TerrainGrid(8, 8, TerrainKind.Plains), DemographicSettings.Default);
            var band = riverless.AddBand(12, new WorldPosition(3, 3));
            var run = new WorldRun(riverless);

            Assert.Multiple(() =>
            {
                Assert.That(() => new WorldRun(null!), Throws.ArgumentNullException);
                Assert.That(run.Founding, Has.Count.EqualTo(1));
                Assert.That(run.Founding[0].Village, Is.EqualTo(band.Id));
                Assert.That(run.Founding[0].Living, Is.EqualTo(12));
            });
        }

        [Test]
        public void A_band_that_settles_moves_its_people_and_does_not_die_out()
        {
            // The band's people are the settlement's from its founding on: a
            // death after it is the settlement's, and the band, empty now, is
            // gone without having died out.
            var world = new World(1UL, new TerrainGrid(16, 16, TerrainKind.Plains), DemographicSettings.Default);
            world.Nomads.Settles = false;
            var band = world.AddBand(12, new WorldPosition(8, 8));
            var run = new WorldRun(world);
            var settlement = world.Founding.Found(band, new Reasons(ReasonCode.PopulationPressure, ReasonCode.LandSuitable));

            // Bare plains feed and warm nobody: a year's food and fuel in
            // store, so the year is about who is counted where.
            settlement.SharedSupplies.Gather(ResourceKind.Food, 100_000);
            settlement.SharedSupplies.Gather(ResourceKind.Wood, 10_000);
            world.Deaths.Die(settlement.Members[0], new Reasons(ReasonCode.Froze));

            run.RunYears(1L);
            var year = run.Years[0];

            Assert.Multiple(() =>
            {
                Assert.That(run.IsClean, Is.True);
                Assert.That(run.DiedOut, Is.Empty, "a band that settles has not died out");
                Assert.That(year.Villages, Has.Count.EqualTo(2), "the band, then the settlement it founded");
                Assert.That(year.Of(settlement.Id).Tally.Froze, Is.EqualTo(1), "the death is the settlement's");
                Assert.That(year.Of(band.Id).Living, Is.Zero);
                Assert.That(year.Of(settlement.Id).Living, Is.EqualTo(settlement.Members.Count));
            });
        }

        [Test]
        public void A_village_founded_mid_run_is_ranged_from_its_first_year_not_from_nothing()
        {
            // The PR #165 review: a settlement founded in year 2 is not in the
            // founding list, and the year before it existed is not a year it
            // was empty. Its range starts at its first year's end.
            var world = new World(1UL, new TerrainGrid(16, 16, TerrainKind.Plains), DemographicSettings.Default);
            world.Nomads.Settles = false;
            var band = world.AddBand(12, new WorldPosition(8, 8));
            band.SharedSupplies.Gather(ResourceKind.Food, 100_000);
            band.SharedSupplies.Gather(ResourceKind.Wood, 10_000);
            var run = new WorldRun(world).RunYears(1L);
            var settlement = world.Founding.Found(band, new Reasons(ReasonCode.PopulationPressure, ReasonCode.LandSuitable));
            run.RunYears(1L);

            var village = run.RangeOf(settlement.Id);
            var wanderers = run.RangeOf(band.Id);

            Assert.Multiple(() =>
            {
                Assert.That(run.Years[0].Villages, Has.Count.EqualTo(1), "year 1 has not seen it");
                Assert.That(village.First, Is.EqualTo(run.Years[1].Of(settlement.Id).Living).And.GreaterThan(0), "its first year's end");
                Assert.That(village.Low, Is.EqualTo(village.First), "no empty year before it existed");
                Assert.That(village.Last, Is.EqualTo(settlement.Members.Count));
                Assert.That(wanderers.First, Is.EqualTo(12), "the band from its founding size");
                Assert.That(wanderers.Last, Is.Zero, "and empty once it settled");
                Assert.That(wanderers.High, Is.GreaterThanOrEqualTo(12));
                Assert.That(() => run.RangeOf(new EntityId(EntityKind.Settlement, 999UL)), Throws.ArgumentException, "a village never seen");
            });
        }

        [Test]
        public void A_run_refuses_a_negative_length_or_one_past_the_end_of_time_before_advancing()
        {
            var run = new WorldRun(1UL);
            var start = run.World.Now;

            Assert.Multiple(() =>
            {
                Assert.That(() => run.RunYears(-1L), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => run.RunYears(long.MaxValue / SimulationTime.TicksPerYear + 1L), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(run.World.Now, Is.EqualTo(start), "refused before the first year, not part-way");
                Assert.That(run.Years, Is.Empty);
                Assert.That(run.RunYears(0L).Years, Is.Empty, "zero is a valid, empty run");
            });
        }

        [Test]
        public void A_world_whose_founder_has_died_can_still_be_run()
        {
            // The run counts what happens once it starts; what the journal
            // already holds is history, read for its milestones and not
            // tallied (the #156 review).
            var world = World.M1(1UL);
            world.Deaths.Die(world.Founding.All[0].Members[0], new Reasons(ReasonCode.Froze));

            var run = new WorldRun(world);
            run.RunYears(1L);

            Assert.Multiple(() =>
            {
                Assert.That(run.IsClean, Is.True);
                Assert.That(run.FirstSettlement, Is.EqualTo(SimulationTime.Zero), "history's milestones are still read");
                Assert.That(run.Years[0].Tally.Froze, Is.Zero, "the death before the run is not this year's");
            });
        }

        [Test]
        public void Deaths_are_tallied_for_the_village_they_happened_in()
        {
            // A village's catastrophe must not hide behind another's numbers
            // (the #156 review, and #163 for villages on the same side): the
            // second village's dead are the second village's.
            var run = new WorldRun(1UL);
            var stricken = run.World.Founding.All[1];
            var dead = stricken.Members.Count / 2;

            foreach (var member in new List<PersonHandle>(stricken.Members).GetRange(0, dead))
            {
                run.World.Deaths.Die(member, new Reasons(ReasonCode.Froze));
            }

            run.RunYears(1L);
            var year = run.Years[0];
            var froze = 0;
            var deaths = 0;
            var births = 0;

            foreach (var village in year.Villages)
            {
                froze += village.Tally.Froze;
                deaths += village.Tally.Deaths;
                births += village.Tally.Births;
            }

            Assert.Multiple(() =>
            {
                Assert.That(year.Of(stricken.Id).Tally.Froze, Is.EqualTo(dead));
                Assert.That(froze, Is.EqualTo(dead), "nobody else froze");
                Assert.That(year.Tally.Froze, Is.EqualTo(froze), "the whole is the sum");
                Assert.That(year.Tally.Deaths, Is.EqualTo(deaths));
                Assert.That(year.Tally.Births, Is.EqualTo(births));
                Assert.That(year.Villages, Has.Count.EqualTo(run.Founding.Count), "the same villages, in the same order");
            });
        }

        [TestCase(0, TestName = "A first village that dies out is reported, not failed")]
        [TestCase(1, TestName = "A village beside a living one that dies out is reported, not failed")]
        [TestCase(3, TestName = "A last village that dies out is reported, not failed")]
        public void A_village_that_dies_out_is_reported_not_failed(int index)
        {
            // The #103 review made a die-out fail the run; #26 made it a
            // report, since berries run out and villages can starve until
            // farms. It must still be seen: the year, and the chronicle line,
            // even when the village beside it lives on (#163).
            var run = new WorldRun(1UL);
            var village = run.World.Founding.All[index];

            foreach (var member in new List<PersonHandle>(village.Members))
            {
                run.World.Deaths.Die(member, new Reasons(ReasonCode.Illness));
            }

            run.RunYears(2L);
            var text = new StringWriter();
            Chronicle.Write(run, text);

            Assert.Multiple(() =>
            {
                Assert.That(run.IsClean, Is.True, "a world can die out without breaking a rule");
                Assert.That(run.DiedOut, Has.Count.EqualTo(1), "once, and only that village");
                Assert.That(run.DiedOut[0].Village, Is.EqualTo(village.Id));
                Assert.That(run.DiedOut[0].Year, Is.EqualTo(1L), "the first year that ended with nobody there");
                Assert.That(run.Years[1].Of(village.Id).Living, Is.Zero, "still listed, empty");
                Assert.That(run.Held, Is.True, "reported, not failed");
                Assert.That(text.ToString(), Does.Contain(village.Id + " died out in year 1."));
            });
        }
        [Test]
        public void The_run_s_years_and_the_validator_s_findings_cannot_be_written_through()
        {
            // The #103 review: a list handed out as IReadOnlyList can be cast
            // back and edited, erasing years the chronicle and the sweep read.
            // Wrapped, the way DrawCollisionDetector and Founding hand theirs out.
            var run = new WorldRun(1UL).RunYears(1L);
            var validator = new WorldValidator();

            Assert.Multiple(() =>
            {
                Assert.That(run.Years, Is.Not.InstanceOf<List<YearSummary>>());
                Assert.That(run.Founding, Is.Not.InstanceOf<List<VillageYear>>());
                Assert.That(run.DiedOut, Is.Not.InstanceOf<List<DieOut>>());
                Assert.That(run.Years[0].Villages, Is.Not.InstanceOf<VillageYear[]>(), "a year's villages either");
                Assert.That(validator.Findings, Is.Not.InstanceOf<List<ValidationFinding>>());
            });
        }

        [Test]
        public void A_year_owns_its_villages_and_hands_out_one_read_only_view_of_them()
        {
            // The PR #165 review: the array a year was built from is the
            // caller's, so editing it must not edit the year; and the view
            // is made once, not on every read.
            var villages = new[] { new VillageYear(new EntityId(EntityKind.Settlement, 1UL), 20, default) };
            var year = new YearSummary(1L, villages);
            villages[0] = new VillageYear(villages[0].Village, 0, default);
            var firstRead = year.Villages;
            var secondRead = year.Villages;

            Assert.Multiple(() =>
            {
                Assert.That(firstRead[0].Living, Is.EqualTo(20), "the caller's array is not the year's");
                Assert.That(secondRead, Is.SameAs(firstRead), "one view, made once");
                Assert.That(() => new YearSummary(1L, null!), Throws.ArgumentNullException);
            });
        }

        [Test]
        public void A_default_year_is_an_empty_one()
        {
            // The PR #165 review: a year is a struct, so default(YearSummary)
            // exists; it reads as no villages and nothing happening rather
            // than throwing.
            var year = default(YearSummary);
            var village = new EntityId(EntityKind.Settlement, 1UL);

            Assert.Multiple(() =>
            {
                Assert.That(year.Villages, Is.Empty);
                Assert.That(year.Tally.Births, Is.Zero);
                Assert.That(year.Tally.Deaths, Is.Zero);
                Assert.That(year.Of(village).Living, Is.Zero);
                Assert.That(year.Of(village).Village, Is.EqualTo(village));
            });
        }

        [Test]
        public void The_chronicle_prints_each_year_and_each_village_s_founding_once()
        {
            var run = new WorldRun(1UL).RunYears(3L);
            var text = new StringWriter();

            Chronicle.Write(run, text);
            var written = text.ToString();

            Assert.Multiple(() =>
            {
                Assert.That(written, Does.StartWith("Year 0: " + run.World.Founding.All[0].Id + " " + World.M1VillageSize + ", "));
                Assert.That(Occurrences(written.Split('\n')[0], " " + World.M1VillageSize), Is.EqualTo(run.Founding.Count), "every village on the founding line");
                Assert.That(written, Does.Contain("Year 1: ").And.Contain("Year 2: ").And.Contain("Year 3: "));
                Assert.That(Occurrences(written, " was founded"), Is.EqualTo(2 * World.M1VillagesPerSide), "one per starting village");
                Assert.That(written, Does.Not.Contain("PersonBorn").And.Not.Contain("PersonDied"), "folded into the year lines");
                Assert.That(written, Does.Not.Contain(" other"), "every death this world publishes has a named cause");
                Assert.That(() => Chronicle.Write(null!, text), Throws.ArgumentNullException);
                Assert.That(() => Chronicle.Write(run, null!), Throws.ArgumentNullException);
            });
        }

        [Test]
        public void The_chronicle_prints_each_village_s_first_house_and_first_harvest_once()
        {
            // Seed 1 settles in year 5 - since #69 a band on the road has no
            // children, so its settling pressure builds more slowly - and
            // farms that summer (#100).
            var run = new WorldRun(1UL).RunYears(7L);
            var text = new StringWriter();
            Chronicle.Write(run, text);
            var written = text.ToString();
            var journal = run.World.Journal;
            var built = new HashSet<EntityId>();
            var harvested = new HashSet<EntityId>();

            for (var i = 0; i < journal.Count; i++)
            {
                if (journal[i].Kind == DomainEventKind.BuildingCompleted)
                {
                    built.Add(journal[i].SecondaryEntity);
                }
                else if (journal[i].Kind == DomainEventKind.FieldHarvested)
                {
                    harvested.Add(journal[i].SecondaryEntity);
                }
            }

            Assert.Multiple(() =>
            {
                Assert.That(harvested, Is.Not.Empty, "a village farmed");
                Assert.That(Occurrences(written, "finished its first house"), Is.EqualTo(built.Count), "one per village, not one per building");
                Assert.That(Occurrences(written, "brought in its first harvest"), Is.EqualTo(harvested.Count), "one per village, not one per crop");
            });
        }

        [Test]
        public void The_chronicle_prints_an_event_at_the_final_year_boundary()
        {
            // AdvanceTo runs everything due on or before its target, so an
            // event at exactly the first tick of a year belongs to the year
            // just run - a founder's birthday death, and what it dissolves,
            // lands there. Published by hand at that instant to stand for one.
            var run = new WorldRun(1UL).RunYears(1L);
            var bus = run.World.Bus;
            bus.Publish(DomainEventKind.FamineStarted, new EntityId(EntityKind.Settlement, 999L), EntityId.None);
            var text = new StringWriter();

            Chronicle.Write(run, text);

            Assert.That(text.ToString(), Does.Contain("y1 d0 famine in Settlement#999"));
        }

        private static int Occurrences(string text, string of)
        {
            var count = 0;

            for (var at = text.IndexOf(of, StringComparison.Ordinal); at >= 0; at = text.IndexOf(of, at + 1, StringComparison.Ordinal))
            {
                count++;
            }

            return count;
        }

        private static int RiverColumn(TerrainGrid grid, int row)
        {
            for (var x = 0; x < grid.Width; x++)
            {
                if (grid[new WorldPosition(x, row)] == TerrainKind.SmallRiver)
                {
                    return x;
                }
            }

            return -1;
        }

        // Whether anything published since the journal held this many events
        // started or ended a famine, counting each.
        private static bool FamineTurned(World world, int since, ref int started, ref int ended)
        {
            var turned = false;

            for (var i = since; i < world.Journal.Count; i++)
            {
                var kind = world.Journal[i].Kind;

                if (kind == DomainEventKind.FamineStarted)
                {
                    started++;
                    turned = true;
                }
                else if (kind == DomainEventKind.FamineEnded)
                {
                    ended++;
                    turned = true;
                }
            }

            return turned;
        }
    }
}
