using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;

namespace KingdomWatch.Harness
{
    /// <summary>
    /// The world as text (section 19's M1 question): one line per journal
    /// entry that says something happened to a community or a family, and
    /// births and deaths folded into a line per year with each homeland's
    /// population, since two centuries of them one per line is unreadable.
    /// </summary>
    /// <remarks>
    /// People are named by id. Names arrive with the event feed (#73).
    /// </remarks>
    public static class Chronicle
    {
        public static void Write(WorldRun run, TextWriter output)
        {
            if (run is null)
            {
                throw new ArgumentNullException(nameof(run));
            }

            if (output is null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            var journal = run.World.Journal;
            var next = 0;

            // A band pitches camp every few weeks for as long as it wanders;
            // only the first is news.
            var camped = new HashSet<EntityId>();

            output.WriteLine(
                "Year 0: west " + run.FoundingWest + ", east " + run.FoundingEast + " (the founding bands)");

            for (var y = 0; y < run.Years.Count; y++)
            {
                var year = run.Years[y];

                // Every entry up to and including the boundary this year
                // ended on: AdvanceTo runs what is due on or before its
                // target, so an event at the first tick of the next year was
                // dispatched by this year's advance and counted in its tally.
                var boundary = year.Year * SimulationTime.TicksPerYear;

                for (; next < journal.Count && journal[next].Time.Ticks <= boundary; next++)
                {
                    var entry = journal[next];

                    if (entry.Kind == DomainEventKind.CampPitched && !camped.Add(entry.PrimaryEntity))
                    {
                        continue;
                    }

                    var line = Describe(entry);

                    if (line != null)
                    {
                        output.WriteLine("  " + Stamp(entry.Time) + line);
                    }
                }

                output.WriteLine(
                    "Year " + year.Year + ": west " + year.West + ", east " + year.East
                    + " (" + Tally(year.Tally) + ")");
            }

            if (run.WestDiedOut is long west)
            {
                output.WriteLine("West died out in year " + west + ".");
            }

            if (run.EastDiedOut is long east)
            {
                output.WriteLine("East died out in year " + east + ".");
            }

            if (run.Failure != null)
            {
                output.WriteLine("Stopped: " + run.Failure);
            }
        }

        private static string Tally(YearTally tally)
        {
            var line = tally.Births + " born, " + tally.Deaths + " died";

            if (tally.Deaths == 0)
            {
                return line;
            }

            var causes = new List<string>(5);
            Cause(causes, tally.OldAge, "old age");
            Cause(causes, tally.Illness, "illness");
            Cause(causes, tally.Starved, "starved");
            Cause(causes, tally.Froze, "froze");
            Cause(causes, tally.OtherDeaths, "other");
            return line + ": " + string.Join(", ", causes);
        }

        private static void Cause(List<string> causes, int count, string name)
        {
            if (count > 0)
            {
                causes.Add(count + " " + name);
            }
        }

        private static string Stamp(SimulationTime time) =>
            "y" + time.YearNumber.ToString(CultureInfo.InvariantCulture)
            + " d" + time.DayOfYear.ToString(CultureInfo.InvariantCulture) + " ";

        private static string? Describe(DomainEvent entry)
        {
            switch (entry.Kind)
            {
                case DomainEventKind.PersonBorn:
                case DomainEventKind.PersonDied:
                    return null;
                case DomainEventKind.CampPitched:
                    return entry.PrimaryEntity + " pitched its first camp";
                case DomainEventKind.SettlementFounded:
                    return entry.PrimaryEntity + " was founded" + From(entry);
                case DomainEventKind.SettlementAbandoned:
                    return entry.PrimaryEntity + " was abandoned" + Because(entry);
                case DomainEventKind.FamineStarted:
                    return "famine in " + entry.PrimaryEntity + Because(entry);
                case DomainEventKind.FamineEnded:
                    return "the famine in " + entry.PrimaryEntity + " ended";
                case DomainEventKind.MarriageFormed:
                    return entry.PrimaryEntity + " and " + entry.SecondaryEntity + " married";
                case DomainEventKind.HouseholdFormed:
                    return entry.PrimaryEntity + " was formed" + From(entry);
                case DomainEventKind.HouseholdDissolved:
                    return entry.PrimaryEntity + " dissolved" + Because(entry);
                default:
                    return entry.Kind + " " + entry.PrimaryEntity + From(entry);
            }
        }

        private static string From(DomainEvent entry) =>
            entry.SecondaryEntity.IsNone ? string.Empty : " (" + entry.SecondaryEntity + ")";

        private static string Because(DomainEvent entry) =>
            entry.Reasons.Count == 0 ? string.Empty : " because " + entry.Reasons;
    }
}
