using System;
using KingdomWatch.Core.Events;

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
    /// </remarks>
    public sealed class EventJournal : IDomainEventSubscriber
    {
        private DomainEvent[] _events;
        private int _count;

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
        }
    }
}
