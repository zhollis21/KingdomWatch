using System;
using System.Collections.Generic;
using KingdomWatch.Core.Data;

namespace KingdomWatch.Core.Relationships
{
    /// <summary>
    /// Who descends from whom. The one relationship kind that is never
    /// forgotten: it is small, it is structural, and history, inheritance and
    /// the kinship ban all read from it long after the people in it are dead.
    /// See docs/design/kingdom-watch-plan-v7.1.md section 6.
    /// </summary>
    /// <remarks>
    /// Parent links are the source of truth and children are an index kept
    /// alongside them, so the graph has exactly one place a fact is written.
    /// Siblings are not stored at all; they are derived from shared parents.
    ///
    /// A person is recorded exactly once, at birth or at world generation,
    /// and their parents must already be recorded. That rule is what makes
    /// cycles impossible rather than merely checked for: a person's parents
    /// exist before they do, so nobody can be named as a parent of someone
    /// who is already their ancestor. The validator's "kinship graph has no
    /// cycles" invariant (#13) holds by construction here.
    ///
    /// Death changes nothing in this store. A dead parent is still a parent,
    /// and the ancestry of the living has to stay walkable through them.
    ///
    /// Keyed by durable id, not handle: genealogy outlives the people in it.
    /// The dictionaries are looked up, never enumerated, by the simulation -
    /// callers iterate people in <see cref="PersonStore"/> slot order and ask
    /// about each, which keeps the order of every walk a function of the
    /// population and not of hashing.
    /// </remarks>
    public sealed class Genealogy
    {
        private readonly Dictionary<EntityId, ParentLinks> _parents = new Dictionary<EntityId, ParentLinks>();
        private readonly Dictionary<EntityId, SpanList<EntityId>> _children = new Dictionary<EntityId, SpanList<EntityId>>();

        /// <summary>How many people are recorded.</summary>
        public int Count => _parents.Count;

        public bool IsRecorded(EntityId person)
        {
            RelationshipGuard.RequirePerson(person, nameof(person));
            return _parents.ContainsKey(person);
        }

        /// <summary>
        /// Records a person and their parents. Once per person; parents first.
        /// Either parent may be <see cref="EntityId.None"/> - founders have no
        /// recorded ancestry - but a named parent must already be recorded.
        /// </summary>
        public void Record(EntityId child, EntityId mother, EntityId father)
        {
            RelationshipGuard.RequirePerson(child, nameof(child));

            // ParentLinks checks kinds and distinctness; what is left is what
            // only a genealogy can know.
            var parents = new ParentLinks(mother, father);
            RequireRecordedParent(parents.Mother, nameof(mother), child);
            RequireRecordedParent(parents.Father, nameof(father), child);

            if (_parents.ContainsKey(child))
            {
                throw new InvalidOperationException(
                    child + " is already recorded, and ancestry is written once.");
            }

            _parents.Add(child, parents);
            AddChild(parents.Mother, child);
            AddChild(parents.Father, child);
        }

        /// <summary>The recorded parents. Throws for an unrecorded person.</summary>
        public ParentLinks Parents(EntityId person)
        {
            RelationshipGuard.RequirePerson(person, nameof(person));
            RequireRecorded(person, nameof(person));
            return _parents[person];
        }

        /// <summary>
        /// The recorded children, in the order they were recorded. Throws for
        /// an unrecorded person.
        /// </summary>
        public ReadOnlySpan<EntityId> Children(EntityId person)
        {
            RelationshipGuard.RequirePerson(person, nameof(person));
            RequireRecorded(person, nameof(person));

            return _children.TryGetValue(person, out var children)
                ? children.AsSpan()
                : ReadOnlySpan<EntityId>.Empty;
        }

        /// <summary>
        /// Whether two different people share at least one recorded parent.
        /// Half-siblings count.
        /// </summary>
        public bool AreSiblings(EntityId a, EntityId b)
        {
            RelationshipGuard.RequirePerson(a, nameof(a));
            RelationshipGuard.RequirePerson(b, nameof(b));
            RequireRecorded(a, nameof(a));
            RequireRecorded(b, nameof(b));

            return a != b && ShareAParent(a, b);
        }

        /// <summary>
        /// The nearest relation between two people within two generations.
        /// </summary>
        public KinshipDegree Kinship(EntityId a, EntityId b)
        {
            RelationshipGuard.RequirePerson(a, nameof(a));
            RelationshipGuard.RequirePerson(b, nameof(b));
            RequireRecorded(a, nameof(a));
            RequireRecorded(b, nameof(b));

            if (a == b)
            {
                return KinshipDegree.Self;
            }

            if (IsParentOf(a, b) || IsParentOf(b, a))
            {
                return KinshipDegree.ParentChild;
            }

            if (ShareAParent(a, b))
            {
                return KinshipDegree.Sibling;
            }

            if (IsGrandparentOf(a, b) || IsGrandparentOf(b, a))
            {
                return KinshipDegree.Grandparent;
            }

            if (IsSiblingOfAParentOf(a, b) || IsSiblingOfAParentOf(b, a))
            {
                return KinshipDegree.AuntUncle;
            }

            if (AreFirstCousins(a, b))
            {
                return KinshipDegree.FirstCousin;
            }

            return KinshipDegree.None;
        }

        private void RequireRecordedParent(EntityId parent, string paramName, EntityId child)
        {
            if (parent.IsNone)
            {
                return;
            }

            if (parent == child)
            {
                throw new ArgumentException(child + " cannot be their own parent.", paramName);
            }

            if (!_parents.ContainsKey(parent))
            {
                throw new ArgumentException(
                    parent + " is not recorded, and parents are recorded before their children.",
                    paramName);
            }
        }

        private void RequireRecorded(EntityId person, string paramName)
        {
            if (!_parents.ContainsKey(person))
            {
                throw new ArgumentException(
                    person + " is not recorded in the genealogy.", paramName);
            }
        }

        private void AddChild(EntityId parent, EntityId child)
        {
            if (parent.IsNone)
            {
                return;
            }

            if (!_children.TryGetValue(parent, out var children))
            {
                children = new SpanList<EntityId>(2);
                _children.Add(parent, children);
            }

            children.Add(child);
        }

        // The walkers below take ids the public surface has already checked
        // are recorded, plus whatever those records name as parents - which
        // may be None. A None parent has no record and so no parents of its
        // own, which is exactly right: a walk through a founder finds nobody.
        private ParentLinks ParentsOf(EntityId person) =>
            _parents.TryGetValue(person, out var links) ? links : ParentLinks.None;

        private bool IsParentOf(EntityId parent, EntityId child) => ParentsOf(child).Includes(parent);

        private bool ShareAParent(EntityId a, EntityId b)
        {
            var ofA = ParentsOf(a);
            var ofB = ParentsOf(b);

            return ofB.Includes(ofA.Mother) || ofB.Includes(ofA.Father);
        }

        private bool IsGrandparentOf(EntityId grandparent, EntityId person)
        {
            var parents = ParentsOf(person);

            return IsParentOf(grandparent, parents.Mother) || IsParentOf(grandparent, parents.Father);
        }

        private bool IsSiblingOfAParentOf(EntityId person, EntityId niece)
        {
            var parents = ParentsOf(niece);

            return IsSiblingOf(person, parents.Mother) || IsSiblingOf(person, parents.Father);
        }

        private bool AreFirstCousins(EntityId a, EntityId b)
        {
            var ofA = ParentsOf(a);

            return IsSiblingOfAParentOf(ofA.Mother, b) || IsSiblingOfAParentOf(ofA.Father, b);
        }

        private bool IsSiblingOf(EntityId a, EntityId b) =>
            !a.IsNone && !b.IsNone && a != b && ShareAParent(a, b);
    }
}
