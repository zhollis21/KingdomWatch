using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Relationships;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Performance
{
    /// <summary>
    /// Relationship reads run under <c>AdvanceTo</c> - a marriage check walks
    /// the genealogy, a social decision reads ties - so the stores get the
    /// same zero-allocation guard as the scheduler (#59). Births and marriages
    /// allocate, and are rare; everything else here must not.
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    public sealed class RelationshipAllocationTests
    {
        [Test]
        public void Genealogy_reads_at_steady_state_allocate_nothing()
        {
            var ids = new IdAllocator();
            var tree = new Genealogy();
            var grandma = Person(ids, tree);
            var grandpa = Person(ids, tree);
            var parent = Person(ids, tree, grandma, grandpa);
            var uncle = Person(ids, tree, grandma, grandpa);
            var me = Person(ids, tree, Person(ids, tree), parent);
            var cousin = Person(ids, tree, Person(ids, tree), uncle);
            var stranger = Person(ids, tree);

            Workload();

            Assert.That(Allocations.Measure(Workload), Is.Zero);

            void Workload()
            {
                for (var i = 0; i < 1000; i++)
                {
                    _ = tree.Parents(me);
                    _ = tree.Children(grandma).Length;
                    _ = tree.AreSiblings(me, cousin);
                    _ = tree.Kinship(me, cousin);
                    _ = tree.Kinship(me, grandpa);
                    _ = tree.Kinship(me, stranger);
                    _ = tree.IsRecorded(me);
                }
            }
        }

        [Test]
        public void Partnership_reads_and_endings_at_steady_state_allocate_nothing()
        {
            var ids = new IdAllocator();
            var store = new Partnerships();
            var mira = ids.Next(EntityKind.Person);
            var aldric = ids.Next(EntityKind.Person);
            var bram = ids.Next(EntityKind.Person);

            // Each pass forms and ends one partnership, so the history lists
            // grow on every pass. Warm up past a doubling (64 -> 128) so the
            // measured span lands inside spare capacity rather than on the
            // next resize.
            Workload(100);

            Assert.That(Allocations.Measure(() => Workload(8)), Is.Zero);

            void Workload(int passes)
            {
                for (var i = 0; i < passes; i++)
                {
                    store.Form(mira, aldric, ids.NextEvent(), SimulationTime.Zero);
                    _ = store.ActivePartnerOf(mira);
                    _ = store.History(aldric).Length;
                    _ = store.History(bram).Length;
                    store.End(aldric, mira, ids.NextEvent(), SimulationTime.Zero);
                }
            }
        }

        [Test]
        public void Social_tie_adjust_decay_and_reads_at_steady_state_allocate_nothing()
        {
            var ids = new IdAllocator();
            var ties = new SocialTies(new SocialTieSettings(8, SimulationTime.TicksPerDay));
            var aldric = ids.Next(EntityKind.Person);
            var others = new EntityId[12];

            for (var i = 0; i < others.Length; i++)
            {
                others[i] = ids.Next(EntityKind.Person);
            }

            var day = 0L;
            Workload();

            Assert.That(Allocations.Measure(Workload), Is.Zero);

            void Workload()
            {
                for (var i = 0; i < 1000; i++)
                {
                    var now = SimulationTime.FromDays(day++);

                    // Cycling through more people than the cap keeps eviction
                    // on the measured path.
                    ties.Adjust(aldric, others[i % others.Length], (i % 7) - 3, i % 3, 2, now);
                    ties.Decay(aldric, now);
                    _ = ties.Ties(aldric).Length;
                    _ = ties.TryGet(aldric, others[0], out _);
                }
            }
        }

        [Test]
        public void Memory_teach_death_compact_and_reads_at_steady_state_allocate_nothing()
        {
            var ids = new IdAllocator();
            var memories = new Memories(new MemorySettings(8, SimulationTime.TicksPerDay, SimulationTime.TicksPerDay * 2, 8));
            var oakshire = ids.Next(EntityKind.Settlement);
            var raid = ids.NextEvent();
            var witness = ids.Next(EntityKind.Person);
            memories.Record(oakshire, raid, EntityId.None, -10, new[] { witness }, SimulationTime.Zero);

            Workload();

            Assert.That(Allocations.Measure(Workload), Is.Zero);

            void Workload()
            {
                for (var i = 0; i < 1000; i++)
                {
                    var learner = ids.Next(EntityKind.Person);
                    _ = memories.Teach(oakshire, raid, learner);
                    memories.WitnessDied(learner);
                    memories.Compact(oakshire, SimulationTime.Zero);
                    _ = memories.Held(oakshire).Length;
                    _ = memories.Witnesses(oakshire, raid).Length;
                    _ = memories.TryGet(oakshire, raid, out _);
                }
            }
        }

        private static EntityId Person(IdAllocator ids, Genealogy tree) =>
            Person(ids, tree, EntityId.None, EntityId.None);

        private static EntityId Person(IdAllocator ids, Genealogy tree, EntityId mother, EntityId father)
        {
            var person = ids.Next(EntityKind.Person);
            tree.Record(person, mother, father);
            return person;
        }
    }
}
