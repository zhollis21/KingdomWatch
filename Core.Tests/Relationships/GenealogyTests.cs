using KingdomWatch.Core.Data;
using KingdomWatch.Core.Relationships;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Relationships
{
    [TestFixture]
    public sealed class GenealogyTests
    {
        [Test]
        public void Recording_a_child_links_both_directions()
        {
            var ids = new IdAllocator();
            var tree = new Genealogy();
            var mother = Founder(tree, ids);
            var father = Founder(tree, ids);
            var child = ids.Next(EntityKind.Person);

            tree.Record(child, mother, father);

            Assert.Multiple(() =>
            {
                Assert.That(tree.Parents(child), Is.EqualTo(new ParentLinks(mother, father)));
                Assert.That(tree.Children(mother).ToArray(), Is.EqualTo(new[] { child }));
                Assert.That(tree.Children(father).ToArray(), Is.EqualTo(new[] { child }));
                Assert.That(tree.Children(child).Length, Is.Zero);
                Assert.That(tree.Count, Is.EqualTo(3));
            });
        }

        [Test]
        public void Founders_have_no_parents_and_a_single_known_parent_is_allowed()
        {
            var ids = new IdAllocator();
            var tree = new Genealogy();
            var founder = Founder(tree, ids);
            var child = ids.Next(EntityKind.Person);

            tree.Record(child, founder, EntityId.None);

            Assert.Multiple(() =>
            {
                Assert.That(tree.Parents(founder), Is.EqualTo(ParentLinks.None));
                Assert.That(tree.Parents(child).Mother, Is.EqualTo(founder));
                Assert.That(tree.Parents(child).Father, Is.EqualTo(EntityId.None));
                Assert.That(tree.Children(founder).ToArray(), Is.EqualTo(new[] { child }));
            });
        }

        [Test]
        public void Children_come_back_in_the_order_they_were_recorded()
        {
            var ids = new IdAllocator();
            var tree = new Genealogy();
            var mother = Founder(tree, ids);
            var first = Child(tree, ids, mother, EntityId.None);
            var second = Child(tree, ids, mother, EntityId.None);
            var third = Child(tree, ids, mother, EntityId.None);

            Assert.That(tree.Children(mother).ToArray(), Is.EqualTo(new[] { first, second, third }));
        }

        [Test]
        public void A_person_is_recorded_once()
        {
            var ids = new IdAllocator();
            var tree = new Genealogy();
            var founder = Founder(tree, ids);

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => tree.Record(founder, EntityId.None, EntityId.None),
                    Throws.InvalidOperationException);
                Assert.That(tree.Count, Is.EqualTo(1));
            });
        }

        [Test]
        public void Parents_are_recorded_before_their_children()
        {
            // The rule that makes cycles impossible rather than checked for:
            // nobody can name as a parent someone who does not yet exist,
            // and nobody who exists can later acquire parents.
            var ids = new IdAllocator();
            var tree = new Genealogy();
            var unrecorded = ids.Next(EntityKind.Person);
            var child = ids.Next(EntityKind.Person);

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => tree.Record(child, unrecorded, EntityId.None),
                    Throws.ArgumentException.With.Message.Contains("not recorded"));
                Assert.That(
                    () => tree.Record(child, EntityId.None, unrecorded),
                    Throws.ArgumentException.With.Message.Contains("not recorded"));
                Assert.That(tree.IsRecorded(child), Is.False, "a refused Record must not record anything");
            });
        }

        [Test]
        public void Refused_inputs()
        {
            var ids = new IdAllocator();
            var tree = new Genealogy();
            var founder = Founder(tree, ids);
            var other = Founder(tree, ids);
            var child = ids.Next(EntityKind.Person);
            var settlement = ids.Next(EntityKind.Settlement);

            Assert.Multiple(() =>
            {
                Assert.That(() => tree.Record(EntityId.None, founder, other), Throws.ArgumentException);
                Assert.That(() => tree.Record(settlement, founder, other), Throws.ArgumentException);
                Assert.That(() => tree.Record(child, settlement, other), Throws.ArgumentException);
                Assert.That(() => tree.Record(child, founder, settlement), Throws.ArgumentException);
                Assert.That(() => tree.Record(child, child, other), Throws.ArgumentException, "own mother");
                Assert.That(() => tree.Record(child, founder, child), Throws.ArgumentException, "own father");
                Assert.That(() => tree.Record(child, founder, founder), Throws.ArgumentException, "same parent twice");
                Assert.That(() => tree.Parents(child), Throws.ArgumentException, "unrecorded");
                Assert.That(() => tree.Children(child), Throws.ArgumentException, "unrecorded");
                Assert.That(() => tree.AreSiblings(founder, child), Throws.ArgumentException, "unrecorded");
                Assert.That(() => tree.Kinship(child, founder), Throws.ArgumentException, "unrecorded");
                Assert.That(() => tree.Parents(EntityId.None), Throws.ArgumentException);
                Assert.That(() => tree.Parents(settlement), Throws.ArgumentException);
                Assert.That(() => tree.Kinship(founder, EntityId.None), Throws.ArgumentException);
                Assert.That(() => tree.Children(EntityId.None), Throws.ArgumentException);
                Assert.That(() => tree.Children(settlement), Throws.ArgumentException);
                Assert.That(() => tree.AreSiblings(settlement, founder), Throws.ArgumentException);
                Assert.That(() => tree.AreSiblings(founder, EntityId.None), Throws.ArgumentException);
                Assert.That(() => tree.IsRecorded(EntityId.None), Throws.ArgumentException);
                Assert.That(() => tree.IsRecorded(settlement), Throws.ArgumentException);
                Assert.That(tree.IsRecorded(child), Is.False);
            });
        }

        [Test]
        public void ParentLinks_cannot_name_a_non_person_or_the_same_person_twice()
        {
            var ids = new IdAllocator();
            var person = ids.Next(EntityKind.Person);
            var other = ids.Next(EntityKind.Person);
            var settlement = ids.Next(EntityKind.Settlement);

            Assert.Multiple(() =>
            {
                Assert.That(() => new ParentLinks(settlement, other), Throws.ArgumentException);
                Assert.That(() => new ParentLinks(person, settlement), Throws.ArgumentException);
                Assert.That(() => new ParentLinks(person, person), Throws.ArgumentException);
                Assert.That(() => new ParentLinks(EntityId.None, EntityId.None), Throws.Nothing, "founders");
                Assert.That(() => new ParentLinks(person, EntityId.None), Throws.Nothing, "one known parent");
            });
        }

        [Test]
        public void Half_siblings_are_siblings()
        {
            var ids = new IdAllocator();
            var tree = new Genealogy();
            var mother = Founder(tree, ids);
            var firstFather = Founder(tree, ids);
            var secondFather = Founder(tree, ids);
            var a = Child(tree, ids, mother, firstFather);
            var b = Child(tree, ids, mother, secondFather);
            var unrelated = Child(tree, ids, Founder(tree, ids), secondFather);

            Assert.Multiple(() =>
            {
                Assert.That(tree.AreSiblings(a, b), Is.True);
                Assert.That(tree.AreSiblings(b, a), Is.True);
                Assert.That(tree.AreSiblings(b, unrelated), Is.True, "share a father");
                Assert.That(tree.AreSiblings(a, unrelated), Is.False);
                Assert.That(tree.AreSiblings(a, a), Is.False, "nobody is their own sibling");
                Assert.That(tree.AreSiblings(mother, firstFather), Is.False, "founders share nothing");
            });
        }

        [Test]
        public void Kinship_names_every_degree_in_both_directions()
        {
            // Two founding couples; their children marry; the grandchildren
            // are first cousins of the other couple's grandchildren.
            var ids = new IdAllocator();
            var tree = new Genealogy();
            var grandma = Founder(tree, ids);
            var grandpa = Founder(tree, ids);
            var parent = Child(tree, ids, grandma, grandpa);
            var uncle = Child(tree, ids, grandma, grandpa);
            var inLaw = Founder(tree, ids);
            var aunt = Founder(tree, ids);
            var me = Child(tree, ids, inLaw, parent);
            var sister = Child(tree, ids, inLaw, parent);
            var cousin = Child(tree, ids, aunt, uncle);
            var stranger = Founder(tree, ids);
            var strangersChild = Child(tree, ids, stranger, EntityId.None);

            Assert.Multiple(() =>
            {
                Assert.That(tree.Kinship(me, me), Is.EqualTo(KinshipDegree.Self));
                Assert.That(tree.Kinship(me, parent), Is.EqualTo(KinshipDegree.ParentChild));
                Assert.That(tree.Kinship(parent, me), Is.EqualTo(KinshipDegree.ParentChild));
                Assert.That(tree.Kinship(me, inLaw), Is.EqualTo(KinshipDegree.ParentChild));
                Assert.That(tree.Kinship(me, sister), Is.EqualTo(KinshipDegree.Sibling));
                Assert.That(tree.Kinship(parent, uncle), Is.EqualTo(KinshipDegree.Sibling));
                Assert.That(tree.Kinship(me, grandma), Is.EqualTo(KinshipDegree.Grandparent));
                Assert.That(tree.Kinship(grandpa, me), Is.EqualTo(KinshipDegree.Grandparent));
                Assert.That(tree.Kinship(me, uncle), Is.EqualTo(KinshipDegree.AuntUncle));
                Assert.That(tree.Kinship(uncle, sister), Is.EqualTo(KinshipDegree.AuntUncle));
                Assert.That(tree.Kinship(me, cousin), Is.EqualTo(KinshipDegree.FirstCousin));
                Assert.That(tree.Kinship(cousin, sister), Is.EqualTo(KinshipDegree.FirstCousin));
                Assert.That(tree.Kinship(me, aunt), Is.EqualTo(KinshipDegree.None), "aunt by marriage is not blood");
                Assert.That(tree.Kinship(inLaw, grandma), Is.EqualTo(KinshipDegree.None), "in-laws");
                Assert.That(tree.Kinship(me, stranger), Is.EqualTo(KinshipDegree.None));
                Assert.That(tree.Kinship(me, strangersChild), Is.EqualTo(KinshipDegree.None));
                Assert.That(tree.Kinship(grandma, grandpa), Is.EqualTo(KinshipDegree.None), "a founding couple");
            });
        }

        [Test]
        public void Kinship_reports_the_nearest_relation_when_several_hold()
        {
            // grandpa + grandma -> daughter; grandpa + daughter -> child. The
            // child's father is also their grandfather, and the nearer of the
            // two is what the ban needs to hear.
            var ids = new IdAllocator();
            var tree = new Genealogy();
            var grandpa = Founder(tree, ids);
            var grandma = Founder(tree, ids);
            var daughter = Child(tree, ids, grandma, grandpa);
            var child = Child(tree, ids, daughter, grandpa);

            Assert.Multiple(() =>
            {
                Assert.That(tree.Kinship(child, grandpa), Is.EqualTo(KinshipDegree.ParentChild));
                Assert.That(tree.Kinship(child, grandma), Is.EqualTo(KinshipDegree.Grandparent));
                Assert.That(tree.Kinship(child, daughter), Is.EqualTo(KinshipDegree.ParentChild));
            });
        }

        [Test]
        public void Walks_through_founders_find_nobody()
        {
            // Every walker has to cope with a None parent slot. Two children
            // of a single known parent each, with nothing above them.
            var ids = new IdAllocator();
            var tree = new Genealogy();
            var a = Child(tree, ids, Founder(tree, ids), EntityId.None);
            var b = Child(tree, ids, EntityId.None, Founder(tree, ids));
            var grandchild = Child(tree, ids, a, EntityId.None);

            Assert.Multiple(() =>
            {
                Assert.That(tree.Kinship(a, b), Is.EqualTo(KinshipDegree.None));
                Assert.That(tree.Kinship(grandchild, b), Is.EqualTo(KinshipDegree.None));
                Assert.That(tree.AreSiblings(a, b), Is.False);
            });
        }

        private static EntityId Founder(Genealogy tree, IdAllocator ids) =>
            Child(tree, ids, EntityId.None, EntityId.None);

        private static EntityId Child(Genealogy tree, IdAllocator ids, EntityId mother, EntityId father)
        {
            var child = ids.Next(EntityKind.Person);
            tree.Record(child, mother, father);
            return child;
        }
    }
}
