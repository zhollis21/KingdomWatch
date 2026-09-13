using System;

namespace KingdomWatch.Core.Clock
{
    /// <summary>
    /// The one <see cref="IScheduledEventHandler"/> the clock is driven with:
    /// hands each dispatched event to the system that owns its
    /// <see cref="ScheduledEventKind"/>.
    /// </summary>
    /// <remarks>
    /// The clock takes a single handler, and every system wants to be it. This
    /// is the seam between them: needs registers for its hunger crossings, the
    /// demographic model for birth checks, and so on, and the clock never
    /// learns any of their names.
    ///
    /// Exactly one handler per kind. Two systems claiming one kind is the
    /// reuse <see cref="ScheduledEventKind"/> warns against - the kind would
    /// no longer say which occurrence it is - so the second registration is
    /// refused rather than chained. A dispatched kind with no handler is
    /// refused too: a wake-up nobody answers is an unhandled hunger crossing
    /// or a birth that never happens, and silently dropping it would turn a
    /// wiring mistake into a world that quietly stops working.
    /// </remarks>
    public sealed class ScheduledEventRouter : IScheduledEventHandler
    {
        private static readonly bool[] DefinedKinds = EnumGuard.BuildMask(typeof(ScheduledEventKind));

        private readonly IScheduledEventHandler?[] _byKind = new IScheduledEventHandler?[DefinedKinds.Length];

        /// <summary>Makes <paramref name="handler"/> the owner of <paramref name="kind"/>.</summary>
        public void Register(ScheduledEventKind kind, IScheduledEventHandler handler)
        {
            if (handler is null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            // The router is itself a handler, so this compiles - and a kind
            // routed back to the router dispatches into itself until the
            // stack runs out.
            if (ReferenceEquals(handler, this))
            {
                throw new ArgumentException(
                    "A router cannot own a kind: dispatching " + kind + " would route straight back here.",
                    nameof(handler));
            }

            var slot = SlotFor(kind);

            if (_byKind[slot] != null)
            {
                throw new InvalidOperationException(
                    kind + " already has a handler. Each kind is owned by exactly one system; a second "
                    + "occurrence needs its own ScheduledEventKind value.");
            }

            _byKind[slot] = handler;
        }

        public void Handle(ScheduledEvent scheduled, SimulationClock clock)
        {
            // The event's constructor rejected undefined kinds, so the slot is
            // in range. A default(ScheduledEvent) bypasses that constructor
            // with Kind None, and lands on slot 0 - which Register refuses to
            // fill, so it is reported below like any other unowned kind.
            var handler = _byKind[(int)scheduled.Kind];

            if (handler is null)
            {
                throw new InvalidOperationException(
                    "No handler is registered for " + scheduled.Kind + ", but " + scheduled
                    + " came due. A scheduled wake-up nobody answers is a wiring bug, not a no-op.");
            }

            handler.Handle(scheduled, clock);
        }

        private static int SlotFor(ScheduledEventKind kind)
        {
            if (!EnumGuard.IsDefined(DefinedKinds, (int)kind))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(kind), kind, "Not a defined ScheduledEventKind.");
            }

            if (kind == ScheduledEventKind.None)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(kind),
                    kind,
                    "ScheduledEventKind.None is the defaulted-field guard, not something that happens.");
            }

            return (int)kind;
        }
    }
}
