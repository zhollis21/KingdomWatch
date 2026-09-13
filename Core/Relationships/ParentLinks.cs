using System;
using KingdomWatch.Core.Data;

namespace KingdomWatch.Core.Relationships
{
    /// <summary>
    /// Who a person's parents are. Either may be <see cref="EntityId.None"/>:
    /// founders arrive with no recorded ancestry, and a parent can be unknown
    /// to the record without being unknown to the world.
    /// </summary>
    /// <remarks>
    /// Two explicit fields rather than an array of up to two. The roles are
    /// real - the demographic model (#11) needs to know which parent carried
    /// the pregnancy - and two fields cost nothing to read, print or hash.
    /// </remarks>
    public readonly struct ParentLinks : IEquatable<ParentLinks>
    {
        /// <summary>Nobody recorded on either side. Equal to <c>default</c>.</summary>
        public static readonly ParentLinks None = default;

        public ParentLinks(EntityId mother, EntityId father)
        {
            Mother = mother;
            Father = father;
        }

        public EntityId Mother { get; }

        public EntityId Father { get; }

        /// <summary>True when <paramref name="person"/> is either parent.</summary>
        public bool Includes(EntityId person) =>
            !person.IsNone && (person == Mother || person == Father);

        public bool Equals(ParentLinks other) => Mother == other.Mother && Father == other.Father;

        public override bool Equals(object? obj) => obj is ParentLinks other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Mother, Father);

        public override string ToString() => "mother " + Mother + ", father " + Father;

        public static bool operator ==(ParentLinks left, ParentLinks right) => left.Equals(right);

        public static bool operator !=(ParentLinks left, ParentLinks right) => !left.Equals(right);
    }
}
