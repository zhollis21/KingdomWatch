using System;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Work;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Work
{
    [TestFixture]
    public sealed class WorkTaskTests
    {
        private static readonly PersonHandle Worker = new PersonHandle(3, 1);
        private static readonly EntityId Band = new EntityId(EntityKind.MobileGroup, 7UL);
        private static readonly EventId Completion = new EventId(11UL);
        private static readonly SimulationTime Start = SimulationTime.FromHours(6L);
        private static readonly WorldPosition Home = new WorldPosition(1, 1);
        private static readonly WorldPosition Forest = new WorldPosition(5, 1);

        private static WorkTask Sample(long travel = 500L, long work = 1000L, long back = 500L) =>
            new WorkTask(Worker, Band, JobKind.Woodcutter, Start, travel, work, back, Home, Forest, Completion);

        [Test]
        public void Default_is_no_task()
        {
            Assert.Multiple(() =>
            {
                Assert.That(WorkTask.None.IsNone, Is.True);
                Assert.That(default(WorkTask).IsNone, Is.True);
                Assert.That(Sample().IsNone, Is.False);
            });
        }

        [Test]
        public void Construction_refuses_what_cannot_be_a_task()
        {
            Assert.Multiple(() =>
            {
                Assert.That(() => new WorkTask(PersonHandle.None, Band, JobKind.Forager, Start, 1L, 1L, 1L, Home, Forest, Completion), Throws.ArgumentException);
                Assert.That(() => new WorkTask(Worker, EntityId.None, JobKind.Forager, Start, 1L, 1L, 1L, Home, Forest, Completion), Throws.ArgumentException);
                Assert.That(() => new WorkTask(Worker, new EntityId(EntityKind.Person, 7UL), JobKind.Forager, Start, 1L, 1L, 1L, Home, Forest, Completion), Throws.ArgumentException, "a holder is a band, not a person");
                Assert.That(() => new WorkTask(Worker, Band, JobKind.None, Start, 1L, 1L, 1L, Home, Forest, Completion), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => new WorkTask(Worker, Band, (JobKind)200, Start, 1L, 1L, 1L, Home, Forest, Completion), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => new WorkTask(Worker, Band, JobKind.Forager, Start, -1L, 1L, 1L, Home, Forest, Completion), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => new WorkTask(Worker, Band, JobKind.Forager, Start, 1L, 0L, 1L, Home, Forest, Completion), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => new WorkTask(Worker, Band, JobKind.Forager, Start, 1L, 1L, -1L, Home, Forest, Completion), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => new WorkTask(Worker, Band, JobKind.Forager, Start, 1L, 1L, 1L, Home, Forest, EventId.None), Throws.ArgumentException);
                // Legs of no length are fine: the site can be underfoot.
                Assert.That(() => new WorkTask(Worker, Band, JobKind.Forager, Start, 0L, 1L, 0L, Home, Forest, Completion), Throws.Nothing);
                // Legs that together wrap the clock are not.
                Assert.That(() => new WorkTask(Worker, Band, JobKind.Forager, Start, long.MaxValue, 1L, 0L, Home, Forest, Completion), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => new WorkTask(Worker, Band, JobKind.Forager, Start, 1L, 1L, long.MaxValue - 1L, Home, Forest, Completion), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => new WorkTask(Worker, Band, JobKind.Forager, SimulationTime.Zero, 1L, 1L, long.MaxValue - 2L, Home, Forest, Completion), Throws.Nothing, "the largest task the clock can hold");
                // Legs that fit the clock but not the clock after the start are not either.
                Assert.That(() => new WorkTask(Worker, Band, JobKind.Forager, new SimulationTime(1L), 1L, 1L, long.MaxValue - 2L, Home, Forest, Completion), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => new WorkTask(Worker, Band, JobKind.Forager, new SimulationTime(long.MaxValue - 1L), 0L, 1L, 0L, Home, Forest, Completion), Throws.Nothing, "ending on the last instant");
                Assert.That(() => new WorkTask(Worker, Band, JobKind.Forager, new SimulationTime(long.MaxValue), 0L, 1L, 0L, Home, Forest, Completion), Throws.TypeOf<ArgumentOutOfRangeException>(), "ending one past it");
            });
        }

        [Test]
        public void The_largest_task_the_clock_can_hold_still_has_phases()
        {
            var task = new WorkTask(Worker, Band, JobKind.Forager, SimulationTime.Zero, 1L, 1L, long.MaxValue - 2L, Home, Forest, Completion);

            Assert.Multiple(() =>
            {
                Assert.That(task.End, Is.EqualTo(new SimulationTime(long.MaxValue)));
                Assert.That(task.PhaseAt(new SimulationTime(long.MaxValue)), Is.EqualTo(TaskPhase.Returning));
                Assert.That(task.PhaseAt(new SimulationTime(2L)), Is.EqualTo(TaskPhase.Returning));
            });
        }

        [Test]
        public void End_is_the_start_plus_the_three_legs()
        {
            Assert.That(Sample().End, Is.EqualTo(Start.Plus(2000L)));
        }

        [Test]
        public void Phase_follows_the_legs_with_the_end_still_returning()
        {
            var task = Sample();

            Assert.Multiple(() =>
            {
                Assert.That(task.PhaseAt(Start.Plus(-1L)), Is.EqualTo(TaskPhase.None), "before setting out");
                Assert.That(task.PhaseAt(Start), Is.EqualTo(TaskPhase.Outbound), "the first tick");
                Assert.That(task.PhaseAt(Start.Plus(499L)), Is.EqualTo(TaskPhase.Outbound), "the last tick of the walk");
                Assert.That(task.PhaseAt(Start.Plus(500L)), Is.EqualTo(TaskPhase.Working), "arrival");
                Assert.That(task.PhaseAt(Start.Plus(1499L)), Is.EqualTo(TaskPhase.Working), "the last tick of work");
                Assert.That(task.PhaseAt(Start.Plus(1500L)), Is.EqualTo(TaskPhase.Returning), "setting off home");
                Assert.That(task.PhaseAt(Start.Plus(2000L)), Is.EqualTo(TaskPhase.Returning), "home: the completion finds them here");
                Assert.That(task.PhaseAt(Start.Plus(2001L)), Is.EqualTo(TaskPhase.None), "after");
            });
        }

        [Test]
        public void A_task_with_no_walk_is_working_from_the_start()
        {
            var task = Sample(travel: 0L, back: 0L);

            Assert.Multiple(() =>
            {
                Assert.That(task.PhaseAt(Start), Is.EqualTo(TaskPhase.Working));
                Assert.That(task.PhaseAt(Start.Plus(999L)), Is.EqualTo(TaskPhase.Working));
                Assert.That(task.PhaseAt(Start.Plus(1000L)), Is.EqualTo(TaskPhase.Returning), "the end, though there is nowhere to return from");
            });
        }
    }
}
