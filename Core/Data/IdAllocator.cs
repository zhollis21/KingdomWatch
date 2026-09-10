using System;

namespace KingdomWatch.Core.Data
{
    /// <summary>
    /// Hands out durable <see cref="EntityId"/> and <see cref="EventId"/>
    /// values. Counters only ever move forward, which is what makes "never
    /// reused" true rather than aspirational.
    /// </summary>
    /// <remarks>
    /// Reuse is the failure this exists to prevent: a recycled id silently
    /// repoints an old history entry, grievance or bookmark at a different
    /// entity, and the corruption stays invisible until someone reads the
    /// chronicle centuries later. See
    /// docs/design/kingdom-watch-plan-v7.1.md section 5.
    ///
    /// Counters start at 1 so value 0 is never handed out and stays
    /// unambiguously "none". Each kind counts independently, which keeps ids
    /// small and readable in logs and tests.
    ///
    /// Saving and loading is issue #15. This type exposes its state through
    /// Peek/Resume; it does not serialize anything itself.
    /// </remarks>
    public sealed class IdAllocator
    {
        private static readonly int SlotCount = ComputeSlotCount();

        private readonly ulong[] _nextByKind;
        private ulong _nextEventId = 1UL;

        public IdAllocator()
        {
            _nextByKind = new ulong[SlotCount];

            for (var i = 0; i < _nextByKind.Length; i++)
            {
                _nextByKind[i] = 1UL;
            }
        }

        /// <summary>Allocates the next durable id for a kind.</summary>
        public EntityId Next(EntityKind kind)
        {
            var slot = SlotFor(kind);
            var value = _nextByKind[slot];

            if (value == ulong.MaxValue)
            {
                throw new InvalidOperationException(
                    "Ran out of " + kind + " ids. Durable ids are never reused, so this world cannot continue.");
            }

            _nextByKind[slot] = value + 1UL;
            return new EntityId(kind, value);
        }

        /// <summary>Allocates the next durable event id.</summary>
        public EventId NextEvent()
        {
            var value = _nextEventId;

            if (value == ulong.MaxValue)
            {
                throw new InvalidOperationException(
                    "Ran out of event ids. Durable ids are never reused, so this world cannot continue.");
            }

            _nextEventId = value + 1UL;
            return new EventId(value);
        }

        /// <summary>The value the next <see cref="Next"/> call will hand out.</summary>
        public ulong PeekNext(EntityKind kind) => _nextByKind[SlotFor(kind)];

        /// <summary>The value the next <see cref="NextEvent"/> call will hand out.</summary>
        public ulong PeekNextEvent() => _nextEventId;

        /// <summary>
        /// Restores a kind's counter when loading a save. Refuses to move
        /// backwards, since that is precisely how a load would start reissuing
        /// ids that history already refers to.
        /// </summary>
        public void ResumeFrom(EntityKind kind, ulong next)
        {
            var slot = SlotFor(kind);

            if (next < _nextByKind[slot])
            {
                throw new ArgumentOutOfRangeException(
                    nameof(next),
                    next,
                    "Resuming " + kind + " ids at " + next + " would reissue ids already allocated (next is "
                    + _nextByKind[slot] + "). Durable ids are never reused.");
            }

            _nextByKind[slot] = next;
        }

        /// <summary>Restores the event counter when loading a save.</summary>
        public void ResumeEventsFrom(ulong next)
        {
            if (next < _nextEventId)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(next),
                    next,
                    "Resuming event ids at " + next + " would reissue ids already allocated (next is "
                    + _nextEventId + "). Durable ids are never reused.");
            }

            _nextEventId = next;
        }

        private static int ComputeSlotCount()
        {
            var kinds = (EntityKind[])Enum.GetValues(typeof(EntityKind));
            var max = 0;

            foreach (var kind in kinds)
            {
                if ((int)kind > max)
                {
                    max = (int)kind;
                }
            }

            return max + 1;
        }

        private static int SlotFor(EntityKind kind)
        {
            if (kind == EntityKind.None)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(kind), kind, "EntityKind.None is not allocatable.");
            }

            var slot = (int)kind;

            if (slot < 0 || slot >= SlotCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(kind), kind, "Not a defined EntityKind.");
            }

            return slot;
        }
    }
}
