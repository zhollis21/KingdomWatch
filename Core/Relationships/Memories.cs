using System;
using System.Collections.Generic;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;

namespace KingdomWatch.Core.Relationships
{
    /// <summary>
    /// What people and settlements remember about specific events, and who
    /// witnessed each. Tiered like other knowledge: recent memories are held
    /// individually, old ones are forgotten once nobody living saw them, and
    /// important ones are promoted and kept forever. See
    /// docs/design/kingdom-watch-plan-v7.1.md sections 10 and 11.
    /// </summary>
    /// <remarks>
    /// A witness list is a capped set. A witness already on the list is not
    /// added twice, and one past <see cref="MemorySettings.MaxWitnesses"/>
    /// is not added at all - the same rule whether they arrive with
    /// <see cref="Record"/> or later through <see cref="Teach"/>. Both report
    /// whether the witness was taken, and neither throws for one who was
    /// not, because "the whole village saw it" is a normal call and the cap
    /// is the store's bound to enforce, not the caller's to pre-apply.
    ///
    /// Promotion happens the moment a witness count reaches the threshold,
    /// so a raid the whole village saw is promoted as it is recorded. Aging
    /// and forgetting happen in <see cref="Compact"/>, which the owning
    /// system calls on its own cadence - this store never touches the clock.
    ///
    /// The death cascade (#9) calls <see cref="WitnessDied"/>. That walk
    /// visits every holder, which is the one place this store enumerates a
    /// dictionary: the outcome is the same in any order, because each
    /// memory's list is edited independently, so hashing order cannot leak
    /// into state. If M2 profiling minds the walk, #65 records the options;
    /// the likely fix is a witness-to-memory index, not a change to what it
    /// does.
    /// </remarks>
    public sealed class Memories
    {
        private readonly Dictionary<EntityId, SpanList<Memory>> _byHolder =
            new Dictionary<EntityId, SpanList<Memory>>();

        private readonly MemorySettings _settings;

        public Memories(MemorySettings settings)
        {
            // The settings constructor validates every field, so a zero
            // witness cap can only mean default(MemorySettings).
            if (settings.MaxWitnesses == 0)
            {
                throw new ArgumentException(
                    "Default settings are not settings; construct MemorySettings explicitly.",
                    nameof(settings));
            }

            _settings = settings;
        }

        public MemorySettings Settings => _settings;

        /// <summary>
        /// How many holders currently remember at least one thing. A holder
        /// with no memories has no entry.
        /// </summary>
        public int HolderCount => _byHolder.Count;

        /// <summary>
        /// Records a new memory. One per holder per event. Returns how many of
        /// the offered witnesses were taken.
        /// </summary>
        public int Record(
            EntityId holder,
            EventId originEvent,
            EntityId subject,
            sbyte valence,
            ReadOnlySpan<EntityId> witnesses,
            SimulationTime now)
        {
            RelationshipGuard.RequireSome(holder, nameof(holder));
            RelationshipGuard.RequireEvent(originEvent, nameof(originEvent));

            foreach (var witness in witnesses)
            {
                RelationshipGuard.RequirePerson(witness, nameof(witnesses));
            }

            if (_byHolder.TryGetValue(holder, out var already) && IndexOf(already, originEvent) >= 0)
            {
                throw new InvalidOperationException(
                    holder + " already remembers " + originEvent + "; a memory is recorded once.");
            }

            // Sized to the cap up front, so Teach never has to grow it.
            var list = new SpanList<EntityId>(_settings.MaxWitnesses);
            var taken = 0;

            foreach (var witness in witnesses)
            {
                if (AddWitness(list, witness))
                {
                    taken++;
                }
            }

            ListFor(holder).Add(new Memory(originEvent, holder, subject, valence, TierFor(list), now, list));
            return taken;
        }

        /// <summary>
        /// Adds a person - a descendant, typically - to a memory's witnesses,
        /// so that a grievance can be inherited. Returns false if they were
        /// already there or the list is full.
        /// </summary>
        public bool Teach(EntityId holder, EventId originEvent, EntityId learner)
        {
            RelationshipGuard.RequireSome(holder, nameof(holder));
            RelationshipGuard.RequireEvent(originEvent, nameof(originEvent));
            RelationshipGuard.RequirePerson(learner, nameof(learner));

            ref var memory = ref Find(holder, originEvent);

            if (!AddWitness(memory.WitnessList, learner))
            {
                return false;
            }

            if (memory.Tier != MemoryTier.Promoted)
            {
                memory = memory.WithTier(TierFor(memory.WitnessList, memory.Tier));
            }

            return true;
        }

        /// <summary>
        /// Removes a person from every witness list they are on. The death
        /// cascade's hook: strength is living witnesses.
        /// </summary>
        public void WitnessDied(EntityId person)
        {
            RelationshipGuard.RequirePerson(person, nameof(person));

            foreach (var held in _byHolder.Values)
            {
                for (var i = 0; i < held.Count; i++)
                {
                    var witnesses = held[i].WitnessList;
                    var index = IndexOf(witnesses, person);

                    if (index >= 0)
                    {
                        witnesses.RemoveAt(index);
                    }
                }
            }
        }

        /// <summary>
        /// Ages the holder's memories: Recent becomes Old past
        /// <see cref="MemorySettings.OldAfterTicks"/>, and Old with no
        /// witnesses left is forgotten past
        /// <see cref="MemorySettings.ForgetAfterTicks"/>. Promoted memories
        /// are untouched.
        /// </summary>
        public void Compact(EntityId holder, SimulationTime now)
        {
            RelationshipGuard.RequireSome(holder, nameof(holder));

            if (!_byHolder.TryGetValue(holder, out var held))
            {
                return;
            }

            // Checked up front so that a refused call leaves every memory as
            // it was, rather than some aged or forgotten before the bad one
            // was reached.
            for (var i = 0; i < held.Count; i++)
            {
                if (now < held[i].FormedAt)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(now), now, held[i].OriginEvent + " was remembered at " + held[i].FormedAt
                        + "; time does not run backwards.");
                }
            }

            for (var i = held.Count - 1; i >= 0; i--)
            {
                ref var memory = ref held[i];
                var age = now.Ticks - memory.FormedAt.Ticks;

                // Promoted needs no special case: it is neither Recent nor
                // Old, so neither step below can touch it.

                if (memory.Tier == MemoryTier.Recent && age >= _settings.OldAfterTicks)
                {
                    memory = memory.WithTier(MemoryTier.Old);
                }

                if (memory.Tier == MemoryTier.Old
                    && memory.WitnessCount == 0
                    && age >= _settings.ForgetAfterTicks)
                {
                    held.RemoveAt(i);
                }
            }

            // A holder that has forgotten everything has no entry: entries
            // are keyed by durable id and would outlive the holder, and
            // WitnessDied walks every one of them.
            if (held.Count == 0)
            {
                _byHolder.Remove(holder);
            }
        }

        /// <summary>
        /// Everything the holder remembers, oldest-recorded first. Empty for a
        /// holder with no memories.
        /// </summary>
        public ReadOnlySpan<Memory> Held(EntityId holder)
        {
            RelationshipGuard.RequireSome(holder, nameof(holder));

            return _byHolder.TryGetValue(holder, out var held)
                ? held.AsSpan()
                : ReadOnlySpan<Memory>.Empty;
        }

        public bool TryGet(EntityId holder, EventId originEvent, out Memory memory)
        {
            RelationshipGuard.RequireSome(holder, nameof(holder));
            RelationshipGuard.RequireEvent(originEvent, nameof(originEvent));

            if (_byHolder.TryGetValue(holder, out var held))
            {
                var index = IndexOf(held, originEvent);

                if (index >= 0)
                {
                    memory = held[index];
                    return true;
                }
            }

            memory = default;
            return false;
        }

        /// <summary>
        /// The witnesses of one memory, in the order they were added. Throws
        /// when the holder has no such memory.
        /// </summary>
        public ReadOnlySpan<EntityId> Witnesses(EntityId holder, EventId originEvent)
        {
            RelationshipGuard.RequireSome(holder, nameof(holder));
            RelationshipGuard.RequireEvent(originEvent, nameof(originEvent));

            return Find(holder, originEvent).WitnessList.AsSpan();
        }

        private ref Memory Find(EntityId holder, EventId originEvent)
        {
            if (_byHolder.TryGetValue(holder, out var held))
            {
                var index = IndexOf(held, originEvent);

                if (index >= 0)
                {
                    return ref held[index];
                }
            }

            throw new InvalidOperationException(holder + " has no memory of " + originEvent + ".");
        }

        private bool AddWitness(SpanList<EntityId> witnesses, EntityId witness)
        {
            if (witnesses.Count == _settings.MaxWitnesses || IndexOf(witnesses, witness) >= 0)
            {
                return false;
            }

            witnesses.Add(witness);
            return true;
        }

        private MemoryTier TierFor(SpanList<EntityId> witnesses, MemoryTier otherwise = MemoryTier.Recent) =>
            witnesses.Count >= _settings.PromoteWhenWitnessesAtLeast ? MemoryTier.Promoted : otherwise;

        private static int IndexOf(SpanList<Memory> held, EventId originEvent)
        {
            for (var i = 0; i < held.Count; i++)
            {
                if (held[i].OriginEvent == originEvent)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int IndexOf(SpanList<EntityId> people, EntityId person)
        {
            for (var i = 0; i < people.Count; i++)
            {
                if (people[i] == person)
                {
                    return i;
                }
            }

            return -1;
        }

        private SpanList<Memory> ListFor(EntityId holder)
        {
            if (!_byHolder.TryGetValue(holder, out var held))
            {
                held = new SpanList<Memory>(4);
                _byHolder.Add(holder, held);
            }

            return held;
        }
    }
}
