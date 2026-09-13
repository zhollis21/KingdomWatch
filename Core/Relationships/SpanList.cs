using System;

namespace KingdomWatch.Core.Relationships
{
    /// <summary>
    /// A growable array that hands its contents out as a <see cref="Span{T}"/>.
    /// The per-person edge storage behind every relationship store.
    /// </summary>
    /// <remarks>
    /// <see cref="System.Collections.Generic.List{T}"/> would do everything
    /// here except the one thing that matters: netstandard2.1 has no way to
    /// view a List's backing array as a span, so reading through it means
    /// either an interface enumerator (which allocates - see AGENTS.md on the
    /// tick loop) or an indexer that copies each struct out. Relationship
    /// reads happen under <c>AdvanceTo</c> - a marriage check walks a
    /// person's kin - so they take the same allocation-free path
    /// <see cref="History.EventJournal"/> uses: an array plus a count.
    ///
    /// Removal shifts the tail down rather than swapping the last element in.
    /// Swap-remove is cheaper but reorders the survivors, and every consumer
    /// of these lists iterates them inside the simulation, where iteration
    /// order has to be a function of the operations that built the list and
    /// nothing else.
    /// </remarks>
    internal sealed class SpanList<T>
    {
        private T[] _items;
        private int _count;

        /// <param name="capacity">At least one; every store sizes for its first entry.</param>
        internal SpanList(int capacity)
        {
            _items = new T[capacity];
        }

        internal int Count => _count;

        internal ref T this[int index] => ref _items[index];

        internal void Add(T item)
        {
            if (_count == _items.Length)
            {
                Array.Resize(ref _items, _items.Length * 2);
            }

            _items[_count] = item;
            _count++;
        }

        internal void RemoveAt(int index)
        {
            _count--;

            if (index < _count)
            {
                Array.Copy(_items, index + 1, _items, index, _count - index);
            }

            Array.Clear(_items, _count, 1);
        }

        internal Span<T> AsSpan() => new Span<T>(_items, 0, _count);
    }
}
