using System;
using System.Collections.Generic;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;

namespace KingdomWatch.Core.Relationships
{
    /// <summary>
    /// Liking, resentment and familiarity between people. The one
    /// relationship kind that is deleted: ordinary acquaintance decays to
    /// nothing and is compacted away, because a 300-year-old elf cannot keep
    /// a record for everyone they ever met. See
    /// docs/design/kingdom-watch-plan-v7.1.md section 6.
    /// </summary>
    /// <remarks>
    /// Bounded twice over. Each value is clamped to
    /// <see cref="SocialTie.MaxMagnitude"/>, and each person holds at most
    /// <see cref="SocialTieSettings.MaxTiesPerPerson"/> ties - past that the
    /// weakest is evicted, ties broken by the lower id so that the choice is
    /// a function of the state and never of hashing. A tie with nothing left
    /// in it is not held at all, whether it got there by adjustment or by
    /// decay.
    ///
    /// Decay is caller-driven. <see cref="Decay"/> applies whatever time has
    /// elapsed since each tie was last touched, so the system that owns a
    /// person's social life calls it on its own cadence - the
    /// SocialDecision scheduled event is the natural one - and this store
    /// never touches the clock. Until that system exists nothing decays,
    /// which is correct for a headless run where almost no ties form.
    ///
    /// There is no dead flag. A tie toward the dead simply decays out like
    /// any other; the grudge that outlives its object is a
    /// <see cref="Memory"/>, not a tie.
    /// </remarks>
    public sealed class SocialTies
    {
        private readonly Dictionary<EntityId, SpanList<SocialTie>> _byPerson =
            new Dictionary<EntityId, SpanList<SocialTie>>();

        private readonly SocialTieSettings _settings;

        public SocialTies(SocialTieSettings settings)
        {
            // The settings constructor validates every field, so a zero cap
            // can only mean default(SocialTieSettings) - which also carries a
            // zero decay interval to divide by.
            if (settings.MaxTiesPerPerson == 0)
            {
                throw new ArgumentException(
                    "Default settings are not settings; construct SocialTieSettings explicitly.",
                    nameof(settings));
            }

            _settings = settings;
        }

        public SocialTieSettings Settings => _settings;

        /// <summary>
        /// How many people currently hold at least one tie. A person with no
        /// ties has no entry - entries are keyed by durable id and would
        /// otherwise outlive the person.
        /// </summary>
        public int PersonCount => _byPerson.Count;

        /// <summary>
        /// Moves what <paramref name="from"/> feels toward
        /// <paramref name="toward"/> by the given amounts, creating the tie if
        /// there is none. Results are clamped; the tie is stamped with
        /// <paramref name="now"/>.
        /// </summary>
        /// <remarks>
        /// The store never holds a tie with nothing in it. An existing tie
        /// adjusted to zero weight is removed, and a new one that would start
        /// at zero is not added - and evicts nobody to make room for nothing.
        /// A caller whose computed adjustment nets to zero is not wrong, so
        /// this is a rule rather than a refusal.
        /// </remarks>
        public void Adjust(
            EntityId from,
            EntityId toward,
            int liking,
            int resentment,
            int familiarity,
            SimulationTime now)
        {
            RelationshipGuard.RequirePerson(from, nameof(from));
            RelationshipGuard.RequirePerson(toward, nameof(toward));
            RelationshipGuard.RequireDistinct(from, toward, nameof(toward));

            if (_byPerson.TryGetValue(from, out var existing))
            {
                var index = IndexOf(existing, toward);

                if (index >= 0)
                {
                    ref var tie = ref existing[index];
                    RequireNotBefore(now, tie.LastTouched);

                    var adjusted = new SocialTie(
                        toward,
                        ClampSigned((long)tie.Liking + liking),
                        ClampUnsigned((long)tie.Resentment + resentment),
                        ClampUnsigned((long)tie.Familiarity + familiarity),
                        now);

                    if (adjusted.Weight == 0)
                    {
                        existing.RemoveAt(index);
                        DropIfEmpty(from, existing);
                    }
                    else
                    {
                        tie = adjusted;
                    }

                    return;
                }
            }

            var fresh = new SocialTie(
                toward,
                ClampSigned(liking),
                ClampUnsigned(resentment),
                ClampUnsigned(familiarity),
                now);

            if (fresh.Weight == 0)
            {
                return;
            }

            // Only now is there something to hold, so only now does the
            // person get an entry.
            var ties = ListFor(from);

            if (ties.Count == _settings.MaxTiesPerPerson)
            {
                ties.RemoveAt(WeakestIndex(ties));
            }

            ties.Add(fresh);
        }

        /// <summary>
        /// Applies the decay owed on every tie the person holds, and drops
        /// those with nothing left.
        /// </summary>
        public void Decay(EntityId person, SimulationTime now)
        {
            RelationshipGuard.RequirePerson(person, nameof(person));

            if (!_byPerson.TryGetValue(person, out var ties))
            {
                return;
            }

            // Checked up front so that a refused call leaves every tie as it
            // was, rather than half of them decayed before the bad one was
            // reached.
            for (var i = 0; i < ties.Count; i++)
            {
                RequireNotBefore(now, ties[i].LastTouched);
            }

            for (var i = ties.Count - 1; i >= 0; i--)
            {
                ref var tie = ref ties[i];
                var steps = (now.Ticks - tie.LastTouched.Ticks) / _settings.TicksPerDecayStep;

                if (steps == 0L)
                {
                    continue;
                }

                // Steps are counted, not the remainder: the stamp moves by
                // whole steps so the fraction of a step left over is still
                // owed next time.
                tie = new SocialTie(
                    tie.Toward,
                    TowardZero(tie.Liking, steps),
                    TowardZero(tie.Resentment, steps),
                    TowardZero(tie.Familiarity, steps),
                    tie.LastTouched.Plus(steps * _settings.TicksPerDecayStep));

                if (tie.Weight == 0)
                {
                    ties.RemoveAt(i);
                }
            }

            DropIfEmpty(person, ties);
        }

        /// <summary>
        /// Every tie the person holds, oldest-formed first. Empty for someone
        /// with none.
        /// </summary>
        public ReadOnlySpan<SocialTie> Ties(EntityId person)
        {
            RelationshipGuard.RequirePerson(person, nameof(person));

            return _byPerson.TryGetValue(person, out var ties)
                ? ties.AsSpan()
                : ReadOnlySpan<SocialTie>.Empty;
        }

        /// <summary>
        /// The tie from one person toward another, if there is one. Asking
        /// about oneself is refused like writing to oneself: nothing has cause
        /// to, so a call that does is a bug worth hearing about.
        /// </summary>
        public bool TryGet(EntityId from, EntityId toward, out SocialTie tie)
        {
            RelationshipGuard.RequirePerson(from, nameof(from));
            RelationshipGuard.RequirePerson(toward, nameof(toward));
            RelationshipGuard.RequireDistinct(from, toward, nameof(toward));

            if (_byPerson.TryGetValue(from, out var ties))
            {
                var index = IndexOf(ties, toward);

                if (index >= 0)
                {
                    tie = ties[index];
                    return true;
                }
            }

            tie = default;
            return false;
        }

        private static void RequireNotBefore(SimulationTime now, SimulationTime lastTouched)
        {
            if (now < lastTouched)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(now), now, "Tie last touched at " + lastTouched + "; time does not run backwards.");
            }
        }

        private static int IndexOf(SpanList<SocialTie> ties, EntityId toward)
        {
            for (var i = 0; i < ties.Count; i++)
            {
                if (ties[i].Toward == toward)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int WeakestIndex(SpanList<SocialTie> ties)
        {
            var weakest = 0;

            for (var i = 1; i < ties.Count; i++)
            {
                var candidate = ties[i].Weight.CompareTo(ties[weakest].Weight);

                if (candidate < 0 || (candidate == 0 && ties[i].Toward < ties[weakest].Toward))
                {
                    weakest = i;
                }
            }

            return weakest;
        }

        // Long arithmetic: a stored value plus an int delta can exceed int,
        // and wrapping would turn "likes them a great deal more" into
        // "loathes them".
        private static sbyte ClampSigned(long value) =>
            (sbyte)Math.Max(-SocialTie.MaxMagnitude, Math.Min(SocialTie.MaxMagnitude, value));

        private static sbyte ClampUnsigned(long value) =>
            (sbyte)Math.Max(0L, Math.Min(SocialTie.MaxMagnitude, value));

        private static sbyte TowardZero(sbyte value, long steps)
        {
            if (steps >= Math.Abs(value))
            {
                return 0;
            }

            return (sbyte)(value < 0 ? value + steps : value - steps);
        }

        // A person with no ties has no entry; see PersonCount.
        private void DropIfEmpty(EntityId person, SpanList<SocialTie> ties)
        {
            if (ties.Count == 0)
            {
                _byPerson.Remove(person);
            }
        }

        private SpanList<SocialTie> ListFor(EntityId person)
        {
            if (!_byPerson.TryGetValue(person, out var ties))
            {
                ties = new SpanList<SocialTie>(Math.Min(_settings.MaxTiesPerPerson, 8));
                _byPerson.Add(person, ties);
            }

            return ties;
        }
    }
}
