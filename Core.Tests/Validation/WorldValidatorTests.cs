using System.Collections.Generic;
using System;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
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
                Assert.That(() => validator.CheckHouseholds(null!, people, clock), Throws.ArgumentNullException);
                Assert.That(
                    () => validator.CheckHouseholds(world.Households, null!, clock), Throws.ArgumentNullException);
                Assert.That(() => validator.CheckGenealogy(null!, people, clock), Throws.ArgumentNullException);
                Assert.That(() => validator.CheckSchedule(null!, people), Throws.ArgumentNullException);
                Assert.That(() => validator.CheckSchedule(clock, null!), Throws.ArgumentNullException);
                Assert.That(() => validator.CheckJobs(null!, people, clock), Throws.ArgumentNullException);
                Assert.That(() => validator.CheckCommunities(null!, people, clock), Throws.ArgumentNullException);
                Assert.That(() => validator.CheckTracked(null!, people, clock), Throws.ArgumentNullException);
                Assert.That(
                    () => validator.CheckSupplies(null!, EntityId.None, clock), Throws.ArgumentNullException);
            });
        }

        [Test]
        public void The_ancestry_rules_are_held_up_by_Genealogy_rather_than_by_this()
        {
            // KinshipCycle and ParentInvalid cannot be provoked today, and it
            // is worth saying why rather than leaving two rules that have
            // never been seen to fire. Genealogy.Record refuses a parent that
            // is not a person (ParentLinks checks the kinds), refuses a parent
            // it has not already recorded, and refuses to record anyone twice
            // - so a cycle cannot be built forwards and cannot be introduced
            // by rewriting an existing link.
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

        private static WorldValidator Check(DemographicWorld world) =>
            new WorldValidator()
                .CheckPeople(world.People, world.Clock, world.Settings)
                .CheckHouseholds(world.Households, world.People, world.Clock)
                .CheckGenealogy(world.Genealogy, world.People, world.Clock)
                .CheckSchedule(world.Clock, world.People);

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
