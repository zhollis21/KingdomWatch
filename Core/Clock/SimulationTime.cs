using System;
using System.Globalization;

namespace KingdomWatch.Core.Clock
{
    /// <summary>
    /// A point on the world clock, counted in integer ticks from the start of
    /// the world. One tick is one simulated second.
    /// </summary>
    /// <remarks>
    /// Integer, never floating point, and never derived from frame time.
    /// Determinism is the foundation the whole debugging strategy rests on, and
    /// a clock that accumulates rounding error takes every system down with it.
    /// See docs/design/kingdom-watch-plan-v7.1.md section 4.
    ///
    /// One second is the granularity section 4 actually asks for: it schedules
    /// a hunger crossing at 17:42 and walks Aldric from home at 10:00 to the
    /// forest at 10:12. It also leaves room under the stepped detail that
    /// arrives at M3, where visible agents need finer positions than a minute.
    /// Two hundred years is about 6.3e9 ticks, so a signed 64-bit count is not
    /// remotely close to a limit.
    ///
    /// A struct rather than a bare long because the unit is the thing that gets
    /// confused - ticks, seconds, days and years all read as "a number" at a
    /// call site. This is the same reason EntityId is not a bare ulong.
    ///
    /// Time never runs backwards, so this cannot be negative. Durations stay
    /// plain longs: they are signed, they are not points on the clock, and
    /// giving them their own type would buy nothing today.
    ///
    /// Days per year is deliberately absent. The calendar belongs to seasons
    /// (issue #53), which is where a year first means something.
    /// </remarks>
    public readonly struct SimulationTime : IEquatable<SimulationTime>, IComparable<SimulationTime>
    {
        public const long TicksPerSecond = 1L;
        public const long TicksPerMinute = 60L * TicksPerSecond;
        public const long TicksPerHour = 60L * TicksPerMinute;
        public const long TicksPerDay = 24L * TicksPerHour;

        /// <summary>The start of the world. Equal to <c>default</c>.</summary>
        public static readonly SimulationTime Zero = default;

        public SimulationTime(long ticks)
        {
            if (ticks < 0L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ticks), ticks, "Simulation time counts forward from zero; it is never negative.");
            }

            Ticks = ticks;
        }

        /// <summary>Simulated seconds since the start of the world.</summary>
        public long Ticks { get; }

        /// <summary>Whole days elapsed. Day 0 is the first day.</summary>
        public long DayNumber => Ticks / TicksPerDay;

        /// <summary>Ticks elapsed within <see cref="DayNumber"/>.</summary>
        public long TickOfDay => Ticks % TicksPerDay;

        public static SimulationTime FromSeconds(long seconds) =>
            new SimulationTime(Scale(seconds, TicksPerSecond, nameof(seconds)));

        public static SimulationTime FromMinutes(long minutes) =>
            new SimulationTime(Scale(minutes, TicksPerMinute, nameof(minutes)));

        public static SimulationTime FromHours(long hours) =>
            new SimulationTime(Scale(hours, TicksPerHour, nameof(hours)));

        public static SimulationTime FromDays(long days) =>
            new SimulationTime(Scale(days, TicksPerDay, nameof(days)));

        /// <summary>
        /// This time plus a duration in ticks. Negative durations are allowed
        /// as long as the result stays on or after <see cref="Zero"/>.
        /// </summary>
        public SimulationTime Plus(long ticks)
        {
            if (ticks > 0L && Ticks > long.MaxValue - ticks)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ticks), ticks, "Adding that many ticks would overflow the world clock.");
            }

            var sum = Ticks + ticks;

            if (sum < 0L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ticks), ticks, "That duration would put the clock before the start of the world.");
            }

            return new SimulationTime(sum);
        }

        /// <summary>
        /// Ticks from this time to <paramref name="later"/>. Negative when
        /// <paramref name="later"/> is in the past.
        /// </summary>
        public long TicksUntil(SimulationTime later) => later.Ticks - Ticks;

        public bool Equals(SimulationTime other) => Ticks == other.Ticks;

        public override bool Equals(object? obj) => obj is SimulationTime other && Equals(other);

        public override int GetHashCode() => Ticks.GetHashCode();

        public int CompareTo(SimulationTime other) => Ticks.CompareTo(other.Ticks);

        /// <summary>
        /// Day and wall clock, with the raw tick alongside it. The tick is what
        /// makes a bug report reproducible - section 5 wants "seed 38471928,
        /// tick 183729" - and the wall clock is what makes it readable.
        /// </summary>
        public override string ToString()
        {
            var tickOfDay = TickOfDay;

            return "day " + DayNumber.ToString(CultureInfo.InvariantCulture)
                + " " + Pad(tickOfDay / TicksPerHour)
                + ":" + Pad((tickOfDay / TicksPerMinute) % 60L)
                + ":" + Pad(tickOfDay % TicksPerMinute)
                + " (tick " + Ticks.ToString(CultureInfo.InvariantCulture) + ")";
        }

        public static bool operator ==(SimulationTime left, SimulationTime right) => left.Equals(right);

        public static bool operator !=(SimulationTime left, SimulationTime right) => !left.Equals(right);

        public static bool operator <(SimulationTime left, SimulationTime right) => left.Ticks < right.Ticks;

        public static bool operator >(SimulationTime left, SimulationTime right) => left.Ticks > right.Ticks;

        public static bool operator <=(SimulationTime left, SimulationTime right) => left.Ticks <= right.Ticks;

        public static bool operator >=(SimulationTime left, SimulationTime right) => left.Ticks >= right.Ticks;

        private static string Pad(long value) =>
            value < 10L
                ? "0" + value.ToString(CultureInfo.InvariantCulture)
                : value.ToString(CultureInfo.InvariantCulture);

        private static long Scale(long value, long ticksPerUnit, string parameterName)
        {
            if (value < 0L)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName, value, "Simulation time counts forward from zero; it is never negative.");
            }

            if (value > long.MaxValue / ticksPerUnit)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName, value, "That many would overflow the world clock.");
            }

            return value * ticksPerUnit;
        }
    }
}
