namespace KingdomWatch.Core.Clock
{
    /// <summary>
    /// The hours of a villager's day (#21): wake, work from dawn to dusk,
    /// home, supper, the fires lit, bed. Ticks of day, the same every day of
    /// the year.
    /// </summary>
    /// <remarks>
    /// One home for the hours, so that the systems that keep them
    /// (<see cref="Work.Jobs"/>, <see cref="Needs.Hunger"/>,
    /// <see cref="Needs.Warmth"/>) and the view that draws them agree on one
    /// clock. <see cref="Wake"/> and <see cref="Bed"/> move nothing in the
    /// simulation: they are when the view draws people up and about, and
    /// when indoors.
    ///
    /// Fixed all year on purpose: a winter day no shorter than a summer one
    /// leaves the work window, and with it the economy's balance, as it was.
    /// </remarks>
    public static class DailySchedule
    {
        /// <summary>Tick of day people get up.</summary>
        public const long Wake = 5L * SimulationTime.TicksPerHour;

        /// <summary>Tick of day the work day starts.</summary>
        public const long Dawn = 6L * SimulationTime.TicksPerHour;

        /// <summary>Tick of day the work day ends: everyone is walking home or there.</summary>
        public const long Dusk = 18L * SimulationTime.TicksPerHour;

        /// <summary>Tick of day the day's one meal is eaten, at home.</summary>
        public const long Supper = 19L * SimulationTime.TicksPerHour;

        /// <summary>Tick of day the winter fires are lit.</summary>
        public const long Nightfall = 20L * SimulationTime.TicksPerHour;

        /// <summary>Tick of day people go to bed.</summary>
        public const long Bed = 21L * SimulationTime.TicksPerHour;

        /// <summary>Whether it is bedtime: from <see cref="Bed"/> until <see cref="Wake"/>.</summary>
        public static bool IsBedtime(SimulationTime now)
        {
            var tickOfDay = now.TickOfDay;
            return tickOfDay >= Bed || tickOfDay < Wake;
        }

        /// <summary>
        /// Ticks from <paramref name="now"/> to the next time the clock reads
        /// <paramref name="tickOfDay"/>, strictly after now: at that hour
        /// itself, the answer is a day.
        /// </summary>
        public static long TicksUntilNext(SimulationTime now, long tickOfDay)
        {
            var current = now.TickOfDay;
            return current < tickOfDay
                ? tickOfDay - current
                : SimulationTime.TicksPerDay - current + tickOfDay;
        }
    }
}
