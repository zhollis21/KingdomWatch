using System.Collections.Generic;
using System;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Lifecycle;
using KingdomWatch.Core.Tests.Lifecycle;
using KingdomWatch.Core.Tests.Work;
using KingdomWatch.Harness;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Validation
{
    /// <summary>
    /// Every rule the validator claims, shown breaking. A rule nobody has
    /// watched fail is a rule that may not fire at all, and an invariant
    /// checker that silently passes is worse than none - it reads as coverage.
    /// </summary>
    /// <remarks>
    /// Corruption is applied through <see cref="PersonStore.RecordSpan"/>,
    /// which is not a contrivance: section 5 requires the bulk path to hand
    /// out records by reference so tight loops stay allocation-free, the
    /// store's own remarks forbid writing identity fields through it, and
    /// documentation is the only thing enforcing that. This is the hole the
    /// validator exists to cover.
    /// </remarks>
    [TestFixture]
    public sealed class WorldValidatorTests
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
        public void A_sound_world_reports_nothing()
        {
            var world = Populated();

            Assert.That(Check(world).IsClean, Is.True, Check(world).Report(0UL));
        }

        [Test]
        public void A_slot_emptied_behind_the_stores_back_breaks_the_count()
        {
            var world = Populated();

            First(world).Id = EntityId.None;

            Assert.That(Rules(world), Does.Contain(ValidationRule.PersonCount));
        }

        [Test]
        public void A_handle_that_names_another_slot_is_caught()
        {
            var world = Populated();
            ref var record = ref First(world);

            record.Handle = new PersonHandle(record.Handle.Index + 1, record.Handle.Generation);

            Assert.That(Rules(world), Does.Contain(ValidationRule.PersonHandleSlot));
        }

        [Test]
        public void An_id_the_store_cannot_look_up_is_caught()
        {
            var world = Populated();

            First(world).Id = new EntityId(EntityKind.Person, 90_001UL);

            Assert.That(Rules(world), Does.Contain(ValidationRule.PersonIdRoundTrip));
        }

        [Test]
        public void Two_people_sharing_an_id_are_caught()
        {
            var world = Populated();
            var records = world.People.RecordSpan();
            var first = IndexOfFirst(world);
            var second = IndexOfNext(world, first);

            records[second].Id = records[first].Id;

            Assert.That(Rules(world), Does.Contain(ValidationRule.DuplicateEntityId));
        }

        [Test]
        public void A_value_outside_an_enum_is_caught()
        {
            // An enum parameter looks pinned by the type system and is not:
            // an enum is an int with names, and the bulk path writes ints.
            var world = Populated();

            First(world).AgeStage = (AgeStage)99;

            Assert.That(Rules(world), Does.Contain(ValidationRule.PersonFieldUndefined));
        }

        [Test]
        public void A_stage_that_disagrees_with_the_age_is_caught()
        {
            // Aging refreshes the stage at boundaries only, so a worldgen that
            // seeded the wrong one stays wrong until the next birthday (#11).
            var world = Populated();

            First(world).AgeStage = AgeStage.Infant;

            Assert.That(Rules(world), Does.Contain(ValidationRule.AgeStageStale));
        }

        [Test]
        public void Somebody_born_after_now_is_caught()
        {
            var world = Populated();

            First(world).BornTick = world.Clock.Now.Ticks + 1L;

            Assert.That(Rules(world), Does.Contain(ValidationRule.BornInFuture));
        }

        [Test]
        public void A_person_naming_a_household_that_does_not_list_them_is_caught()
        {
            var world = Populated();

            First(world).Household = new EntityId(EntityKind.Household, 40_001UL);

            Assert.That(Rules(world), Does.Contain(ValidationRule.HouseholdMembership));
        }

        [Test]
        public void A_household_listing_somebody_the_store_lost_is_caught()
        {
            var world = Populated();
            world.NewCouple(out var wife, out _);
            var records = world.People.RecordSpan();

            records[wife.Index].Id = EntityId.None;

            Assert.That(Rules(world), Does.Contain(ValidationRule.HouseholdMemberMissing));
        }

        [Test]
        public void A_living_person_with_no_ancestry_record_is_caught()
        {
            // FamilyFormation and Deaths both assume the record is there, and
            // a missing one only shows when someone marries or dies (#70).
            var world = Populated();

            world.People.Add(
                world.Base.Ids.Next(EntityKind.Person),
                new WorldPosition(1, 1),
                100,
                AgeStage.Adult,
                Sex.Female,
                0,
                0,
                world.Clock.Now,
                world.Clock.Now.Ticks - 30L * SimulationTime.TicksPerYear);

            Assert.That(Rules(world), Does.Contain(ValidationRule.GenealogyMissing));
        }

        [Test]
        public void An_event_still_due_for_somebody_gone_is_caught()
        {
            var world = Populated();

            world.Clock.Schedule(
                world.Clock.Now.Plus(SimulationTime.TicksPerDay),
                Mortality.Phase,
                ScheduledEventKind.MortalityCheck,
                new EntityId(EntityKind.Person, 70_001UL),
                EntityId.None);

            Assert.That(Rules(world), Does.Contain(ValidationRule.ScheduledTargetMissing));
        }

        [Test]
        public void An_event_still_due_for_a_household_that_is_gone_is_caught()
        {
            // Seven of the thirteen scheduled kinds in use are owned by a
            // household or a community rather than by a person - work days,
            // meals, evening fires, courtship rounds, councils, arrivals,
            // birth checks - and the rule skipped
            // every one of them until review said so.
            var world = Populated();

            world.Clock.Schedule(
                world.Clock.Now.Plus(SimulationTime.TicksPerDay),
                Fertility.Phase,
                ScheduledEventKind.BirthCheck,
                new EntityId(EntityKind.Household, 71_001UL),
                EntityId.None);

            Assert.That(Rules(world), Does.Contain(ValidationRule.ScheduledTargetMissing));
        }

        [Test]
        public void An_event_still_due_for_a_community_that_is_gone_is_caught()
        {
            var world = Populated();

            world.Clock.Schedule(
                world.Clock.Now.Plus(SimulationTime.TicksPerDay),
                KingdomWatch.Core.Work.Jobs.Phase,
                ScheduledEventKind.WorkDayDue,
                new EntityId(EntityKind.MobileGroup, 71_002UL),
                EntityId.None);

            Assert.That(Rules(world), Does.Contain(ValidationRule.ScheduledTargetMissing));
        }

        [Test]
        public void A_kind_that_nothing_schedules_against_is_left_alone()
        {
            // Polities, dynasties and named animals are in EntityKind and in
            // nothing that books an event (#39, #45). Reporting them would be
            // a rule that fires on a world nobody can build, which is the
            // opposite of the point.
            var world = Populated();

            world.Clock.Schedule(
                world.Clock.Now.Plus(SimulationTime.TicksPerDay),
                Fertility.Phase,
                ScheduledEventKind.BirthCheck,
                new EntityId(EntityKind.Polity, 71_003UL),
                EntityId.None);

            Assert.That(Rules(world), Does.Not.Contain(ValidationRule.ScheduledTargetMissing));
        }

        [Test]
        public void A_record_naming_an_event_the_queue_lost_is_caught()
        {
            // The #80 failure, from the other side: the record and the queue
            // are free to disagree and nothing else notices.
            var world = Populated();

            First(world).PregnancyDue = new EventId(60_001UL);

            Assert.That(Rules(world), Does.Contain(ValidationRule.PendingEventMissing));
        }

        [Test]
        public void A_job_recorded_with_no_task_behind_it_is_caught()
        {
            var world = new WorkWorld(3UL, WorkWorld.DefaultMap());
            var band = world.NewBand(WorkWorld.Camp, WorkWorld.PlentifulFood(4));
            var worker = world.JoinAdults(band, 4)[0];

            world.People.RecordSpan()[worker.Index].Job = JobKind.Forager;

            var validator = new WorldValidator()
                .CheckJobs(world.Jobs, world.People, world.Clock);

            Assert.That(RulesOf(validator), Does.Contain(ValidationRule.JobMirrorStale));
        }

        [Test]
        public void A_task_that_outlived_its_worker_is_caught()
        {
            // Not answerable from PersonStore: the death cascade removes the
            // record, so a task left behind belongs to somebody who can no
            // longer be enumerated.
            var world = new WorkWorld(3UL, WorkWorld.DefaultMap());
            var band = world.NewBand(WorkWorld.Camp, 0);
            var worker = world.Join(band, 30L);

            world.AdvanceToDawn();

            Assert.That(world.Jobs.HasTask(worker), Is.True, "the worker never got a task to outlive them");

            world.People.RecordSpan()[worker.Index].Id = EntityId.None;

            var validator = new WorldValidator()
                .CheckJobs(world.Jobs, world.People, world.Clock);

            Assert.That(RulesOf(validator), Does.Contain(ValidationRule.TaskOutlivedWorker));
        }

        [Test]
        public void A_community_listing_somebody_the_store_lost_is_caught()
        {
            var world = Populated();
            var band = world.NewBand();
            var person = world.NewPerson(30L, Sex.Male);

            band.AddMember(person);
            world.People.RecordSpan()[person.Index].Id = EntityId.None;

            var validator = new WorldValidator()
                .CheckCommunities(new[] { (ICommunity)band }, world.People, world.Clock);

            Assert.That(RulesOf(validator), Does.Contain(ValidationRule.CommunityMemberMissing));
        }

        [Test]
        public void One_person_in_two_communities_is_caught()
        {
            var world = Populated();
            var here = world.NewBand();
            var there = world.NewBand();
            var person = world.NewPerson(30L, Sex.Male);

            here.AddMember(person);
            there.AddMember(person);

            var validator = new WorldValidator()
                .CheckCommunities(new[] { (ICommunity)here, there }, world.People, world.Clock);

            Assert.That(RulesOf(validator), Does.Contain(ValidationRule.DoubleMembership));
        }

        [Test]
        public void A_stage_boundary_the_queue_lost_is_caught()
        {
            // The third of PersonRecord's booked-event ids. It was added by
            // the same change that added this validator and went unchecked
            // until review caught it - the rule covered two of the three.
            var world = Populated();

            First(world).PendingAgeStage = new EventId(60_002UL);

            Assert.That(Rules(world), Does.Contain(ValidationRule.PendingEventMissing));
        }

        [Test]
        public void A_system_booking_the_queue_lost_is_caught()
        {
            // The same rule for the record a system keeps rather than the one
            // a person carries. A booking the queue has forgotten is a stream
            // that has silently stopped: the owner waits for a wake-up that
            // never comes, and nothing else says so.
            var world = new WorkWorld(3UL, WorkWorld.DefaultMap());
            var band = world.NewBand(WorkWorld.Camp, WorkWorld.PlentifulFood(4));

            world.JoinAdults(band, 4);
            world.AdvanceToDawn();

            var bookings = new List<PendingBooking>();
            world.Jobs.CopyBookingsTo(bookings);

            Assert.That(bookings, Is.Not.Empty, "the band never booked a work day to lose");

            // Cancelling behind the system's back is what a lost booking is.
            world.Clock.Cancel(bookings[0].Booked);

            var validator = new WorldValidator().CheckBookings(bookings, world.Clock);

            Assert.That(RulesOf(validator), Does.Contain(ValidationRule.PendingEventMissing));
        }

        [Test]
        public void An_ancestry_walk_follows_fathers_as_well_as_mothers()
        {
            // The walk used to climb mothers only, which cannot see a cycle
            // that uses a father edge - if a's father is b and b's mother is
            // a, neither walk ever closes the loop.
            //
            // A cycle cannot be built through Genealogy at all (see below), so
            // the discriminating case is the walk's own bound: a chain of
            // fathers longer than it will report only if fathers are
            // followed. Climbing mothers alone stops at the first person,
            // whose mother is None, and reports nothing.
            var world = Populated();
            var ancestor = world.IdOf(world.NewPerson(30L, Sex.Male));

            for (var i = 0; i < 600; i++)
            {
                ancestor = world.IdOf(
                    world.NewPersonBornAt(
                        world.Clock.Now.Ticks - 30L * SimulationTime.TicksPerYear,
                        Sex.Male,
                        AgeStage.Adult,
                        EntityId.None,
                        ancestor));
            }

            var validator = new WorldValidator()
                .CheckGenealogy(world.Genealogy, world.People, world.Clock);

            Assert.That(RulesOf(validator), Does.Contain(ValidationRule.KinshipCycle));
        }

        [Test]
        public void A_line_of_mothers_too_deep_to_be_real_is_reported()
        {
            // The depth bound counts a mother edge as a generation too; the
            // fathers test above cannot see one that does not.
            var world = Populated();
            var ancestor = world.IdOf(world.NewPerson(30L, Sex.Female));

            for (var i = 0; i < 600; i++)
            {
                ancestor = world.IdOf(
                    world.NewPersonBornAt(
                        world.Clock.Now.Ticks - 30L * SimulationTime.TicksPerYear,
                        Sex.Female,
                        AgeStage.Adult,
                        ancestor,
                        EntityId.None));
            }

            var validator = new WorldValidator()
                .CheckGenealogy(world.Genealogy, world.People, world.Clock);

            Assert.That(RulesOf(validator), Does.Contain(ValidationRule.KinshipCycle));
        }

        [Test]
        public void A_deep_line_is_reported_even_when_a_shallow_line_reaches_the_same_ancestor()
        {
            // The #103 review: a walk that visits each ancestor once, at the
            // depth it first reaches them, hides a deep line behind a shallow
            // one. X has 400 generations above her; P reaches X through her
            // father in two generations and through her mother in 300, so
            // P's deepest line is 700 generations.
            var world = Populated();
            var born = world.Clock.Now.Ticks - 30L * SimulationTime.TicksPerYear;
            PersonHandle Born(Sex sex, EntityId mother, EntityId father) =>
                world.NewPersonBornAt(born, sex, AgeStage.Adult, mother, father);

            var above = world.IdOf(world.NewPerson(30L, Sex.Male));

            for (var i = 1; i < 400; i++)
            {
                above = world.IdOf(Born(Sex.Male, EntityId.None, above));
            }

            var x = world.IdOf(Born(Sex.Female, EntityId.None, above));
            var motherLine = x;

            for (var i = 0; i < 299; i++)
            {
                motherLine = world.IdOf(Born(Sex.Female, motherLine, EntityId.None));
            }

            var father = world.IdOf(Born(Sex.Male, x, EntityId.None));
            var p = world.IdOf(Born(Sex.Female, motherLine, father));

            var validator = new WorldValidator()
                .CheckGenealogy(world.Genealogy, world.People, world.Clock);

            var reported = false;

            foreach (var finding in validator.Findings)
            {
                reported |= finding.Rule == ValidationRule.KinshipCycle && finding.Subject == p;
            }

            Assert.That(reported, Is.True, "P's 700-generation line went unreported");
        }

        [Test]
        public void Deep_lines_are_climbed_through_the_dead_on_both_sides()
        {
            // Only the youngest of each line is alive, so the check cannot
            // borrow a depth it worked out for a living ancestor: it has to
            // climb dead fathers and dead mothers itself. A rebuilt world
            // (#42) is mostly dead ancestors.
            var world = Populated();
            var born = world.Clock.Now.Ticks - 30L * SimulationTime.TicksPerYear;

            PersonHandle Line(Sex sex, bool throughMothers)
            {
                var ancestors = new List<PersonHandle> { world.NewPerson(30L, sex) };

                for (var i = 0; i < 600; i++)
                {
                    var parent = world.IdOf(ancestors[ancestors.Count - 1]);
                    ancestors.Add(world.NewPersonBornAt(
                        born, sex, AgeStage.Adult,
                        throughMothers ? parent : EntityId.None,
                        throughMothers ? EntityId.None : parent));
                }

                for (var i = 0; i < ancestors.Count - 1; i++)
                {
                    world.Deaths.Die(ancestors[i], new Reasons(ReasonCode.OldAge));
                }

                return ancestors[ancestors.Count - 1];
            }

            var fatherLine = world.IdOf(Line(Sex.Male, throughMothers: false));
            var motherLine = world.IdOf(Line(Sex.Female, throughMothers: true));

            var validator = new WorldValidator()
                .CheckGenealogy(world.Genealogy, world.People, world.Clock);
            var reported = new List<EntityId>();

            foreach (var finding in validator.Findings)
            {
                if (finding.Rule == ValidationRule.KinshipCycle)
                {
                    reported.Add(finding.Subject);
                }
            }

            Assert.That(reported, Is.EquivalentTo(new[] { fatherLine, motherLine }));
        }

        [Test]
        public void A_wide_but_shallow_ancestry_is_not_a_cycle()
        {
            // #17's seed 1 stopped at year 267 on "more than 512 recorded
            // ancestors": in a population of fourteen thousand, eleven
            // generations of distinct forebears is ordinary. Ten generations
            // of a full tree is 1,022 ancestors and only ten deep.
            var world = Populated();
            var born = world.Clock.Now.Ticks - 30L * SimulationTime.TicksPerYear;
            var generation = new List<EntityId>();

            for (var i = 0; i < 1024; i++)
            {
                generation.Add(world.IdOf(world.NewPersonBornAt(born, i % 2 == 0 ? Sex.Female : Sex.Male, AgeStage.Adult)));
            }

            while (generation.Count > 1)
            {
                var next = new List<EntityId>();

                for (var i = 0; i < generation.Count; i += 2)
                {
                    next.Add(world.IdOf(world.NewPersonBornAt(
                        born, next.Count % 2 == 0 ? Sex.Female : Sex.Male, AgeStage.Adult, generation[i], generation[i + 1])));
                }

                generation = next;
            }

            var validator = new WorldValidator()
                .CheckGenealogy(world.Genealogy, world.People, world.Clock);

            Assert.That(RulesOf(validator), Does.Not.Contain(ValidationRule.KinshipCycle), validator.Report(11UL));
        }

        [Test]
        public void A_tracked_community_listing_somebody_the_store_lost_is_caught()
        {
            // The tracked set is what each system has events booked against,
            // which is not the same question as where people stand: a band is
            // legitimately tracked by four systems at once, so this rule is
            // only about the members resolving.
            var world = Populated();
            var band = world.NewBand();
            var person = world.NewPerson(30L, Sex.Male);

            band.AddMember(person);
            world.People.RecordSpan()[person.Index].Id = EntityId.None;

            var tracked = new List<ICommunity>();
            world.Hunger.CopyTrackedTo(tracked);

            var validator = new WorldValidator().CheckTracked(tracked, world.People, world.Clock);

            Assert.That(RulesOf(validator), Does.Contain(ValidationRule.CommunityMemberMissing));
        }

        [Test]
        public void The_supply_rules_are_held_up_by_ResourceLedger_rather_than_by_this()
        {
            // NegativeResource and ConservationBroken are the other two rules
            // that cannot be provoked, and the reason is the same shape as the
            // ancestry ones: the ledger owns its own arithmetic and refuses to
            // go negative rather than recording that it did.
            //
            // They stay for the same reason too. The ledger is not the only
            // thing that will ever write stock - #24 adds reservations with
            // owners, and #42 rebuilds a world from a save - and a conservation
            // rule that was never wired is found at exactly the wrong moment.
            var world = new WorkWorld();
            var band = world.NewBand(WorkWorld.Camp, 0);

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => band.SharedSupplies.Consume(ResourceKind.Food, 1),
                    Throws.Exception,
                    "spending stock that is not there is refused, not recorded");
                Assert.That(band.SharedSupplies.AuditBalances(), Is.True, "and the refusal left it balanced");
            });
        }

        [Test]
        public void A_report_names_the_seed_and_every_finding()
        {
            // Section 5 asks for "seed 39274 broke at year 347 because ...",
            // so the report has to carry the rule and the subject rather than
            // a count.
            var world = Populated();

            First(world).BornTick = world.Clock.Now.Ticks + 1L;

            var validator = Check(world);
            var report = validator.Report(39_274UL);

            Assert.Multiple(() =>
            {
                Assert.That(validator.IsClean, Is.False);
                Assert.That(report, Does.Contain("39274"));
                Assert.That(report, Does.Contain(nameof(ValidationRule.BornInFuture)));
                Assert.That(new WorldValidator().Report(1UL), Does.Contain("clean"));
            });
        }

        [Test]
        public void A_finding_names_a_rule_and_a_detail()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    () => new ValidationFinding(ValidationRule.None, default, EntityId.None, "x"),
                    Throws.InstanceOf<ArgumentOutOfRangeException>(),
                    "None is the absence of a rule, not one to report");
                Assert.That(
                    () => new ValidationFinding((ValidationRule)999, default, EntityId.None, "x"),
                    Throws.InstanceOf<ArgumentOutOfRangeException>(),
                    "a rule nobody declared would print as a number nobody could trace");
                Assert.That(
                    () => new ValidationFinding(ValidationRule.BornInFuture, default, EntityId.None, null!),
                    Throws.ArgumentNullException);
            });
        }

        [Test]
        public void Every_check_refuses_null()
        {
            var world = Populated();
            var people = world.People;
            var clock = world.Clock;
            var validator = new WorldValidator();

            Assert.Multiple(() =>
            {
                Assert.That(() => validator.CheckPeople(null!, clock, world.Settings), Throws.ArgumentNullException);
                Assert.That(() => validator.CheckPeople(people, null!, world.Settings), Throws.ArgumentNullException);
                Assert.That(
                    () => validator.CheckPeople(people, clock, null!),
                    Throws.ArgumentNullException,
                    "the stage rule is not silently skipped for want of settings");
                Assert.That(() => validator.CheckHouseholds(null!, people, clock), Throws.ArgumentNullException);
                Assert.That(
                    () => validator.CheckHouseholds(world.Households, null!, clock), Throws.ArgumentNullException);
                Assert.That(() => validator.CheckGenealogy(null!, people, clock), Throws.ArgumentNullException);
                Assert.That(
                    () => validator.CheckSchedule(null!, people, Communities(world), world.Households),
                    Throws.ArgumentNullException);
                Assert.That(
                    () => validator.CheckSchedule(clock, null!, Communities(world), world.Households),
                    Throws.ArgumentNullException);
                Assert.That(
                    () => validator.CheckSchedule(clock, people, null!, world.Households),
                    Throws.ArgumentNullException);
                Assert.That(
                    () => validator.CheckSchedule(clock, people, Communities(world), null!),
                    Throws.ArgumentNullException);
                Assert.That(() => validator.CheckJobs(null!, people, clock), Throws.ArgumentNullException);
                Assert.That(() => validator.CheckCommunities(null!, people, clock), Throws.ArgumentNullException);
                Assert.That(() => validator.CheckTracked(null!, people, clock), Throws.ArgumentNullException);
                Assert.That(
                    () => validator.CheckBookings(null!, clock), Throws.ArgumentNullException);
                Assert.That(
                    () => validator.CheckBookings(new List<PendingBooking>(), null!),
                    Throws.ArgumentNullException);
                Assert.That(
                    () => validator.CheckSupplies(null!, EntityId.None, clock), Throws.ArgumentNullException);
            });
        }

        [Test]
        public void A_booking_names_an_owner_a_kind_and_an_event()
        {
            // The type is what five systems hand their records out in, so a
            // half-filled one would be a silently unchecked stream rather
            // than a crash.
            var owner = new EntityId(EntityKind.MobileGroup, 4UL);

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => new PendingBooking(EntityId.None, ScheduledEventKind.MealDue, new EventId(1UL)),
                    Throws.ArgumentException);
                Assert.That(
                    () => new PendingBooking(owner, ScheduledEventKind.None, new EventId(1UL)),
                    Throws.InstanceOf<ArgumentOutOfRangeException>());

                // An enum parameter looks pinned by the type system and is
                // not. This one is worse than most: CompareTo sorts on the
                // numeric value, so a cast-in kind would order itself between
                // two real ones and move the sequence the world hash folds in.
                Assert.That(
                    () => new PendingBooking(owner, (ScheduledEventKind)999, new EventId(1UL)),
                    Throws.InstanceOf<ArgumentOutOfRangeException>(),
                    "a kind nobody declared is not a kind");
                Assert.That(
                    () => new PendingBooking(owner, ScheduledEventKind.MealDue, EventId.None),
                    Throws.ArgumentException,
                    "nothing booked is an absent entry, not one naming None");
            });
        }

        [Test]
        public void Bookings_order_by_owner_then_kind_then_event()
        {
            // AddBookings sorts on this, so the hash depends on it being a
            // total order rather than merely a consistent one.
            var first = new EntityId(EntityKind.MobileGroup, 1UL);
            var second = new EntityId(EntityKind.MobileGroup, 2UL);

            var byOwner = new PendingBooking(first, ScheduledEventKind.MealDue, new EventId(9UL));
            var byKind = new PendingBooking(second, ScheduledEventKind.MealDue, new EventId(9UL));
            var byEvent = new PendingBooking(second, ScheduledEventKind.WorkDayDue, new EventId(1UL));

            Assert.Multiple(() =>
            {
                Assert.That(byOwner.CompareTo(byKind), Is.LessThan(0), "owner first");
                Assert.That(byKind.CompareTo(byEvent), Is.LessThan(0), "then kind");
                Assert.That(byOwner.CompareTo(byOwner), Is.Zero, "and a booking equals itself");
                Assert.That(byOwner, Is.Not.EqualTo(byKind));
            });
        }

        [Test]
        public void An_ancestor_with_no_record_of_their_own_is_reported_rather_than_thrown_on()
        {
            // Unreachable today for the same reason the cycle rules are:
            // Genealogy.Record refuses a parent it has not already recorded.
            // It is here because Genealogy.Parents throws for somebody it has
            // no record of, and a validator that crashes on corrupt data
            // reports nothing about it - so the walk checks before it asks.
            // #42 rebuilds a world from a save, which is a door Record does
            // not stand in front of.
            var world = Populated();
            var child = world.IdOf(world.NewPerson(5L, Sex.Female));

            Assert.That(
                () => world.Genealogy.Record(child, new EntityId(EntityKind.Person, 88_888UL), EntityId.None),
                Throws.ArgumentException,
                "an unrecorded parent is refused at the door");
        }

        [Test]
        public void The_ancestry_rules_are_held_up_by_Genealogy_rather_than_by_this()
        {
            // ParentInvalid cannot be provoked today, and neither can a real
            // cycle: Genealogy.Record refuses a parent that is not a person
            // (ParentLinks checks the kinds), refuses a parent it has not
            // already recorded, refuses anyone as their own parent, and
            // refuses to record anyone twice - so a cycle cannot be built
            // forwards and cannot be introduced by rewriting an existing link.
            //
            // KinshipCycle's other arm is reachable and tested: the walk's own
            // bound fires on a line of descent too deep to be real, which is what
            // An_ancestry_walk_follows_fathers_as_well_as_mothers drives.
            //
            // The rules stay because that guard is not the only way ancestry
            // will ever be written: #42 rebuilds a world from a save, and
            // section 17's own warning is that rebuilding can shift history.
            // That is the moment these become live, and finding out then that
            // they were never wired is the expensive version.
            var world = Populated();
            var child = world.NewPerson(5L, Sex.Female);
            var childId = world.IdOf(child);

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => world.Genealogy.Record(childId, new EntityId(EntityKind.Household, 5UL), EntityId.None),
                    Throws.Exception,
                    "a non-person parent is refused at the door");
                Assert.That(
                    () => world.Genealogy.Record(childId, EntityId.None, EntityId.None),
                    Throws.InvalidOperationException,
                    "ancestry is written once, so a link cannot be repointed into a cycle");
            });
        }

        private static DemographicWorld Populated()
        {
            var world = new DemographicWorld(Quiet(), 11UL);
            var band = world.NewBand();

            foreach (var member in world.Generator.Generate(16, new WorldPosition(3, 3)).Members)
            {
                band.AddMember(member);
            }

            world.AdvanceYears(2L);
            return world;
        }

        // Every band the world has, which is what the schedule rule resolves
        // a community-owned event against.
        private static List<ICommunity> Communities(DemographicWorld world)
        {
            var communities = new List<ICommunity>();
            var scratch = new List<ICommunity>();

            world.Hunger.CopyTrackedTo(scratch);
            communities.AddRange(scratch);

            return communities;
        }

        private static WorldValidator Check(DemographicWorld world) =>
            new WorldValidator()
                .CheckPeople(world.People, world.Clock, world.Settings)
                .CheckHouseholds(world.Households, world.People, world.Clock)
                .CheckGenealogy(world.Genealogy, world.People, world.Clock)
                .CheckSchedule(world.Clock, world.People, Communities(world), world.Households);

        private static ValidationRule[] Rules(DemographicWorld world) => RulesOf(Check(world));

        private static ValidationRule[] RulesOf(WorldValidator validator)
        {
            var rules = new ValidationRule[validator.Findings.Count];

            for (var i = 0; i < rules.Length; i++)
            {
                rules[i] = validator.Findings[i].Rule;
            }

            return rules;
        }

        private static ref PersonRecord First(DemographicWorld world) =>
            ref world.People.RecordSpan()[IndexOfFirst(world)];

        private static int IndexOfFirst(DemographicWorld world) => IndexOfNext(world, -1);

        private static int IndexOfNext(DemographicWorld world, int after)
        {
            var records = world.People.RecordSpan();

            for (var i = after + 1; i < records.Length; i++)
            {
                if (!records[i].Id.IsNone)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
