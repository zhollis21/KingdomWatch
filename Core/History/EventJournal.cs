using System;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Rng;

namespace KingdomWatch.Core.History
{
    /// <summary>
    /// Everything the <see cref="DomainEventBus"/> has published, in order.
    /// The chronicle, the feed and the determinism hash read from here.
    /// </summary>
    /// <remarks>
    /// The minimal journal: one array, appended to, never compacted. Section
    /// 17 wants old entries folded into era summaries and long-dead people
    /// with no descendants reduced to stubs; that is a design of its own and
    /// arrives with persistence, not here. Until then a 200-year run keeps
    /// every event, which at a few kilobytes per simulated year is fine for
    /// the headless harness.
    ///
    /// Growth allocates, doubling like <c>EventQueue</c>. Size the capacity
    /// for the run: the allocation-checked tick loop (section 18) is measured
    /// after warm-up, and a journal that has to grow inside the measured span
    /// is a regression the test will report.
    ///
    /// Time must not run backwards between entries. The bus stamps each event
    /// with the clock, and the clock only moves forward, so a violation here
    /// is a bug in one of them - and cheaper to catch at the journal than to
    /// find in a chronicle that reads out of order.
    ///
    /// **The digest is the journal's side of the world hash (#104).** Every
    /// event is folded into <see cref="Digest"/> as it is recorded, so the
    /// hash reads one number however long the history has grown, rather
    /// than walking every entry each time it is taken. The fold is
    /// <see cref="SplitMix64"/> over each field in a fixed order, the same
    /// idiom as the hash itself, and it is part of what a save has to carry:
    /// it cannot be rebuilt from a journal that has been compacted (#74).
    /// </remarks>
    public sealed class EventJournal : IDomainEventSubscriber
    {
        private DomainEvent[] _events;
        private int _count;
        private ulong _digest;

        /// <param name="capacity">
        /// Entries to preallocate. Growth past it works but allocates.
        /// </param>
        public EventJournal(int capacity)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(capacity), capacity, "A journal needs room for at least one event.");
            }

            _events = new DomainEvent[capacity];
        }

        public int Count => _count;

        /// <summary>
        /// Every event recorded so far, folded in order into one number. Two
        /// journals with different digests recorded different histories; the
        /// same digest is strong evidence of the same history, not proof - it
        /// detects divergence, as the world hash does.
        /// </summary>
        public ulong Digest => _digest;

        public DomainEvent this[int index]
        {
            get
            {
                if (index < 0 || index >= _count)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(index), index, "The journal holds " + _count + " events.");
                }

                return _events[index];
            }
        }

        /// <summary>Every recorded event, oldest first.</summary>
        public ReadOnlySpan<DomainEvent> AsSpan() => new ReadOnlySpan<DomainEvent>(_events, 0, _count);

        public void On(in DomainEvent published)
        {
            // A default(DomainEvent) skips the constructor that requires an
            // id. Recording one would put Event#None in the chronicle and the
            // hash, and nothing downstream could ever refer back to it.
            if (published.Id.IsNone)
            {
                throw new ArgumentException(
                    "Cannot record an event with no id: it was never published through the bus.",
                    nameof(published));
            }

            if (_count > 0 && published.Time < _events[_count - 1].Time)
            {
                throw new InvalidOperationException(
                    "Cannot record " + published + " after " + _events[_count - 1]
                    + ": the journal is in time order, and the world does not run backwards.");
            }

            if (_count == _events.Length)
            {
                Array.Resize(ref _events, _events.Length * 2);
            }

            _events[_count] = published;
            _count++;
            Fold(published);
        }

        // Longhand and in a fixed order, for the reason WorldHash folds its
        // records that way: a reflective walk would reorder itself the first
        // time someone adds a field.
        private void Fold(in DomainEvent published)
        {
            Mix(published.Id.Value);
            Mix(published.Time.Ticks);
            Mix((long)published.Kind);
            Mix(published.PrimaryEntity);
            Mix(published.SecondaryEntity);

            var reasons = published.Reasons;
            Mix(reasons.Count);

            for (var i = 0; i < reasons.Count; i++)
            {
                Mix((long)reasons[i]);
            }
        }

        private void Mix(ulong value) => _digest = SplitMix64.Mix(_digest ^ value);

        private void Mix(long value) => Mix(unchecked((ulong)value));

        private void Mix(EntityId id)
        {
            Mix((long)id.Kind);
            Mix(id.Value);
        }
    }
}
