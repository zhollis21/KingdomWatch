using System;
using KingdomWatch.Core.Data;

namespace KingdomWatch.Core.Relationships
{
    /// <summary>
    /// The argument checks every relationship store makes. Relationships are
    /// keyed by durable id, so a <see cref="EntityId.None"/> or an id of the
    /// wrong kind would be stored, saved and resolved to nothing forever.
    /// </summary>
    internal static class RelationshipGuard
    {
        internal static void RequirePerson(EntityId id, string paramName)
        {
            // Checking the kind covers None as well: None is the only id with
            // no kind.
            if (id.Kind != EntityKind.Person)
            {
                throw new ArgumentException(
                    "Expected the id of a person, not " + id + ".", paramName);
            }
        }

        internal static void RequireSome(EntityId id, string paramName)
        {
            if (id.IsNone)
            {
                throw new ArgumentException("EntityId.None names nobody.", paramName);
            }
        }

        internal static void RequireEvent(EventId id, string paramName)
        {
            if (id.IsNone)
            {
                throw new ArgumentException(
                    "EventId.None is not an event that happened; relationships need real provenance.",
                    paramName);
            }
        }

        internal static void RequireDistinct(EntityId a, EntityId b, string paramName)
        {
            if (a == b)
            {
                throw new ArgumentException(
                    a + " cannot be in a relationship with themselves.", paramName);
            }
        }
    }
}
