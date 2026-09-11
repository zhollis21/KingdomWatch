using System;
using System.Collections.Generic;
using KingdomWatch.Core.Data;

namespace KingdomWatch.Core.Clock
{
    /// <summary>
    /// The scheduler's pending events, ordered by
    /// <see cref="ScheduledEvent.CompareTo"/>. A binary min-heap over one
    /// array, with cancellation.
    /// </summary>
    /// <remarks>
    /// Internal on purpose. <see cref="SimulationClock"/> is the API; a second
    /// public type with overlapping Schedule and Cancel methods would only
    /// invite callers to bypass the clock's ordering guarantees. Everything
    /// here is exercised through the clock.
    ///
    /// A heap rather than a sorted timeline of per-instant buckets: the bucket
    /// version allocates a node and a list per distinct instant, which is a
    /// steady drip of garbage in the tick loop the design commits to keeping
    /// allocation-free (section 18), and the contents of each bucket still need
    /// ordering by the rest of the key. One array does the whole job.
    /// netstandard2.1 has no PriorityQueue, so it is hand-rolled either way.
    ///
    /// **Cancellation is lazy.** Cancelling drops the id from
    /// <see cref="_live"/> and leaves the entry in the array to be discarded
    /// when it surfaces. Removing from the middle of a heap means finding the
    /// entry first, and the alternative - a second index from id to array slot,
    /// maintained across every sift - is more machinery than the problem
    /// deserves. Cancellation is not rare: every meal moves a hunger crossing,
    /// so re-prediction is the normal case rather than the exception.
    ///
    /// That leaves dead entries occupying the array, so the queue compacts once
    /// they outnumber the live ones. Compaction rebuilds the heap, which
    /// changes its internal layout - and that is precisely why
    /// <see cref="ScheduledEvent"/>'s ordering has to be total. With a total
    /// order the dispatch sequence is a property of the events; without one it
    /// would be a property of this array, and compaction would silently
    /// reorder the world.
    /// </remarks>
    internal sealed class EventQueue
    {
        private const int InitialCapacity = 16;

        // Below this there is nothing worth compacting, and the array is small
        // enough that dead entries cost nothing.
        private const int CompactionFloor = 32;

        private ScheduledEvent[] _heap = Array.Empty<ScheduledEvent>();
        private int _count;

        // Every id currently in the array that has NOT been cancelled. Doubles
        // as the live count, so Count stays exact without scanning.
        private readonly HashSet<EventId> _live = new HashSet<EventId>();

        /// <summary>Events still due. Cancelled entries are not counted.</summary>
        internal int Count => _live.Count;

        internal void Enqueue(ScheduledEvent scheduled)
        {
            if (_count == _heap.Length)
            {
                Grow();
            }

            _heap[_count] = scheduled;
            SiftUp(_count);
            _count++;
            _live.Add(scheduled.Id);
        }

        /// <summary>
        /// Drops an event. Returns false when the id was never queued or has
        /// already been dispatched or cancelled, so a caller re-predicting a
        /// threshold can tell whether it beat the crossing.
        /// </summary>
        internal bool Cancel(EventId id) => _live.Remove(id);

        /// <summary>
        /// The next event due, without removing it. False when nothing is
        /// pending.
        /// </summary>
        internal bool TryPeek(out ScheduledEvent next)
        {
            DiscardCancelledRoot();

            if (_count == 0)
            {
                next = default;
                return false;
            }

            next = _heap[0];
            return true;
        }

        /// <summary>
        /// Removes and returns the next event due on or before
        /// <paramref name="target"/>. False when nothing is pending, or when
        /// the next one is still ahead of the target.
        /// </summary>
        /// <remarks>
        /// This is section 4's min(queue head, target), answered where the
        /// queue can answer it in one pass. Splitting it into a peek and a
        /// separate dequeue made the caller purge cancelled entries off the
        /// front twice per event, and left the dequeue with an
        /// empty-queue branch that no caller could ever reach.
        /// </remarks>
        internal bool TryDequeueDueBy(SimulationTime target, out ScheduledEvent due)
        {
            if (!TryPeek(out var next) || next.Time > target)
            {
                due = default;
                return false;
            }

            due = next;
            _live.Remove(due.Id);
            RemoveRoot();
            Compact();
            return true;
        }

        // Cancelled entries are only discovered when they reach the root, so
        // peeking and dequeuing both start by clearing any off the front.
        private void DiscardCancelledRoot()
        {
            while (_count > 0 && !_live.Contains(_heap[0].Id))
            {
                RemoveRoot();
            }

            Compact();
        }

        private void RemoveRoot()
        {
            _count--;
            _heap[0] = _heap[_count];
            _heap[_count] = default;

            if (_count > 1)
            {
                SiftDown(0);
            }
        }

        // Rebuild without the cancelled entries once they are the majority.
        // Bounds the array at roughly twice the live count however heavily
        // thresholds are re-predicted.
        private void Compact()
        {
            if (_count < CompactionFloor || _live.Count * 2 > _count)
            {
                return;
            }

            var kept = 0;

            for (var i = 0; i < _count; i++)
            {
                if (_live.Contains(_heap[i].Id))
                {
                    _heap[kept] = _heap[i];
                    kept++;
                }
            }

            for (var i = kept; i < _count; i++)
            {
                _heap[i] = default;
            }

            _count = kept;

            // Filtering in place leaves the array in no particular order, so
            // rebuild the heap property bottom-up.
            for (var i = (_count / 2) - 1; i >= 0; i--)
            {
                SiftDown(i);
            }
        }

        private void Grow()
        {
            var capacity = _heap.Length == 0 ? InitialCapacity : _heap.Length * 2;
            Array.Resize(ref _heap, capacity);
        }

        private void SiftUp(int index)
        {
            var moving = _heap[index];

            while (index > 0)
            {
                var parent = (index - 1) / 2;

                if (_heap[parent].CompareTo(moving) <= 0)
                {
                    break;
                }

                _heap[index] = _heap[parent];
                index = parent;
            }

            _heap[index] = moving;
        }

        private void SiftDown(int index)
        {
            var moving = _heap[index];
            var half = _count / 2;

            while (index < half)
            {
                var child = (index * 2) + 1;
                var right = child + 1;

                if (right < _count && _heap[right].CompareTo(_heap[child]) < 0)
                {
                    child = right;
                }

                if (_heap[child].CompareTo(moving) >= 0)
                {
                    break;
                }

                _heap[index] = _heap[child];
                index = child;
            }

            _heap[index] = moving;
        }
    }
}
