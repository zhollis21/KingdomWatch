using System;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;

namespace KingdomWatch.Core.Lifecycle
{
    /// <summary>
    /// The numbers of the demographic model: when the stages begin, who may
    /// conceive and how often, and the life table. Section 6: the mechanism
    /// is architecture, the numbers are tuning - so the mechanism lives in
    /// <see cref="Aging"/>, <see cref="Mortality"/> and <see cref="Fertility"/>
    /// and every number lives here.
    /// </summary>
    /// <remarks>
    /// One instance describes one race. Section 10 wants a race difference
    /// to be a number in a table, not a new system; lifespan and fertility
    /// are both named there as the cheap kind. Only one race exists until
    /// #34, so <see cref="Default"/> is the human table, and the elf table
    /// is a second instance handed to the same three systems.
    ///
    /// Integer throughout: chances are per mille and the sim branches on
    /// them, which section 5 makes a rule rather than a preference. Every
    /// number is a placeholder in the <see cref="PrimitiveTier"/> sense -
    /// plausible against a 120-day year, not tuned, and nothing to read a
    /// balance decision into. Section 6's warning stands: winter is not the
    /// regulator, age at household formation is.
    ///
    /// Age in years is whole years lived, so a boundary "at 16" means the
    /// sixteenth birthday. Stages are contiguous and ordered, which is what
    /// lets <see cref="StageAt"/> be a function of age alone and
    /// <see cref="Aging"/> book one wake-up per boundary.
    /// </remarks>
    public sealed class DemographicSettings
    {
        /// <summary>
        /// The largest year any field may hold. A million years of ticks is
        /// still a thousandth of the clock's range, so a boundary converted to
        /// ticks cannot overflow, and the only overflow left - a birthday past
        /// the last representable instant - is the one the systems guard for.
        /// </summary>
        public const long MaxYears = 1_000_000L;

        /// <summary>The human table. See the type's remarks.</summary>
        public static readonly DemographicSettings Default = new DemographicSettings();

        /// <summary>Birthday at which an infant becomes a child.</summary>
        public long ChildAtYears { get; init; } = 3L;

        /// <summary>Birthday at which a child becomes an adolescent.</summary>
        public long AdolescentAtYears { get; init; } = 12L;

        /// <summary>Birthday at which an adolescent becomes an adult.</summary>
        public long AdultAtYears { get; init; } = 16L;

        /// <summary>Birthday at which an adult becomes an elder.</summary>
        public long ElderAtYears { get; init; } = 55L;

        /// <summary>First birthday at which a woman may conceive.</summary>
        public long FertileFromYears { get; init; } = 16L;

        /// <summary>Birthday from which a woman no longer conceives.</summary>
        public long FertileUntilYears { get; init; } = 45L;

        /// <summary>Ticks between one household's conception checks.</summary>
        public long BirthCheckTicks { get; init; } = 10L * SimulationTime.TicksPerDay;

        /// <summary>
        /// Chance per check that an eligible couple conceives. Twenty-five
        /// per mille every ten days is a conception about every 400 days
        /// and, with gestation, a birth roughly every four years - six or
        /// seven across a fertile life, before infant mortality takes its
        /// share. Forty was tried first and grew a band of twelve to seven
        /// hundred in a century; section 15 expects six-fold in three
        /// hundred years.
        /// </summary>
        public int ConceptionPerMille { get; init; } = 25;

        /// <summary>Conception to birth. Three quarters of a year.</summary>
        public long GestationTicks { get; init; } = 90L * SimulationTime.TicksPerDay;

        /// <summary>
        /// How long after giving birth a woman does not conceive. A year.
        /// Without it, a check that shares an instant with a delivery - every
        /// delivery, when gestation is a multiple of the check interval -
        /// would conceive the day the child was born.
        /// </summary>
        public long PostpartumTicks { get; init; } = SimulationTime.TicksPerYear;

        /// <summary>
        /// Health a child is born with. Positive: health at or below zero is
        /// certain death at the next wake-up, and a child born there would
        /// be a child born dead.
        /// </summary>
        public short NewbornHealth { get; init; } = 100;

        /// <summary>
        /// Health below which a person is frail: they do not conceive, and
        /// their yearly death chance is multiplied by
        /// <see cref="FrailtyMultiplier"/>. At least one, so that "at or
        /// above the floor" always means alive: health at or below zero is
        /// certain death, and no floor may admit it. One is the table with
        /// no frailty gate.
        /// </summary>
        public short HealthFloor { get; init; } = 50;

        /// <summary>Yearly death chance, per mille, for an infant.</summary>
        public int InfantMortalityPerMille { get; init; } = 60;

        /// <summary>Yearly death chance, per mille, for a child.</summary>
        public int ChildMortalityPerMille { get; init; } = 10;

        /// <summary>Yearly death chance, per mille, for an adolescent.</summary>
        public int AdolescentMortalityPerMille { get; init; } = 3;

        /// <summary>Yearly death chance, per mille, for an adult.</summary>
        public int AdultMortalityPerMille { get; init; } = 10;

        /// <summary>
        /// Yearly death chance, per mille, for an elder below the soft
        /// lifespan. From there it climbs to certainty at the maximum.
        /// </summary>
        public int ElderMortalityPerMille { get; init; } = 50;

        /// <summary>
        /// Birthday from which the yearly chance ramps toward certainty, and
        /// past which a death to the table reads as old age rather than
        /// illness.
        /// </summary>
        public long SoftLifespanYears { get; init; } = 70L;

        /// <summary>Birthday nobody survives.</summary>
        public long MaxLifespanYears { get; init; } = 100L;

        /// <summary>
        /// What the yearly chance is multiplied by for someone below
        /// <see cref="HealthFloor"/>. Section 6's health modifier.
        /// </summary>
        public int FrailtyMultiplier { get; init; } = 3;

        /// <summary>
        /// What the yearly chance is multiplied by for someone unfed past
        /// <see cref="Needs.Hunger.StarvationGrace"/>. Section 6's nutrition
        /// modifier; starving to death outright is
        /// <see cref="ScheduledEventKind.StarvationCritical"/>.
        /// </summary>
        public int HungerMultiplier { get; init; } = 3;

        /// <summary>
        /// Throws if the table is inconsistent: boundaries out of order or
        /// past <see cref="MaxYears"/>, a chance outside per mille, a
        /// multiplier that could overflow, a lifespan before the last stage. Called
        /// by each system that reads the table, once, at construction.
        /// </summary>
        public void Validate()
        {
            RequireAscending(0L, ChildAtYears, nameof(ChildAtYears));
            RequireAscending(ChildAtYears, AdolescentAtYears, nameof(AdolescentAtYears));
            RequireAscending(AdolescentAtYears, AdultAtYears, nameof(AdultAtYears));
            RequireAscending(AdultAtYears, ElderAtYears, nameof(ElderAtYears));
            RequireAscending(ElderAtYears, SoftLifespanYears, nameof(SoftLifespanYears));
            RequireAscending(SoftLifespanYears, MaxLifespanYears, nameof(MaxLifespanYears));
            RequireAscending(MaxLifespanYears, MaxYears + 1L, nameof(MaxLifespanYears));
            RequireAscending(0L, FertileFromYears, nameof(FertileFromYears));
            RequireAscending(FertileFromYears, FertileUntilYears, nameof(FertileUntilYears));
            RequireAscending(FertileUntilYears, MaxYears + 1L, nameof(FertileUntilYears));

            RequirePositive(BirthCheckTicks, nameof(BirthCheckTicks));
            RequirePositive(GestationTicks, nameof(GestationTicks));
            RequireNonNegative(PostpartumTicks, nameof(PostpartumTicks));
            RequirePositive(NewbornHealth, nameof(NewbornHealth));
            RequirePositive(HealthFloor, nameof(HealthFloor));
            RequireMultiplier(FrailtyMultiplier, nameof(FrailtyMultiplier));
            RequireMultiplier(HungerMultiplier, nameof(HungerMultiplier));

            RequirePerMille(ConceptionPerMille, nameof(ConceptionPerMille));
            RequirePerMille(InfantMortalityPerMille, nameof(InfantMortalityPerMille));
            RequirePerMille(ChildMortalityPerMille, nameof(ChildMortalityPerMille));
            RequirePerMille(AdolescentMortalityPerMille, nameof(AdolescentMortalityPerMille));
            RequirePerMille(AdultMortalityPerMille, nameof(AdultMortalityPerMille));
            RequirePerMille(ElderMortalityPerMille, nameof(ElderMortalityPerMille));
        }

        /// <summary>The stage someone of this age is in.</summary>
        public AgeStage StageAt(long ageYears)
        {
            if (ageYears < ChildAtYears)
            {
                return AgeStage.Infant;
            }

            if (ageYears < AdolescentAtYears)
            {
                return AgeStage.Child;
            }

            if (ageYears < AdultAtYears)
            {
                return AgeStage.Adolescent;
            }

            return ageYears < ElderAtYears ? AgeStage.Adult : AgeStage.Elder;
        }

        /// <summary>
        /// The birthday at which someone of this age next changes stage, or
        /// false once they are an elder, which is the last stage.
        /// </summary>
        public bool TryNextBoundary(long ageYears, out long boundaryYears)
        {
            if (ageYears < ChildAtYears)
            {
                boundaryYears = ChildAtYears;
                return true;
            }

            if (ageYears < AdolescentAtYears)
            {
                boundaryYears = AdolescentAtYears;
                return true;
            }

            if (ageYears < AdultAtYears)
            {
                boundaryYears = AdultAtYears;
                return true;
            }

            if (ageYears < ElderAtYears)
            {
                boundaryYears = ElderAtYears;
                return true;
            }

            boundaryYears = 0L;
            return false;
        }

        /// <summary>Whether a woman of this age may conceive.</summary>
        public bool IsFertileAge(long ageYears) =>
            ageYears >= FertileFromYears && ageYears < FertileUntilYears;

        /// <summary>
        /// The chance per mille that someone of this age dies in the coming
        /// year, before health and nutrition are weighed. The stage's rate
        /// until the soft lifespan; from there a straight line to certainty
        /// at the maximum, and certainty past it.
        /// </summary>
        public int BaseMortalityPerMille(long ageYears)
        {
            if (ageYears >= MaxLifespanYears)
            {
                return 1000;
            }

            var stageRate = StageRate(StageAt(ageYears));

            if (ageYears < SoftLifespanYears)
            {
                return stageRate;
            }

            var ramp = MaxLifespanYears - SoftLifespanYears;
            var into = ageYears - SoftLifespanYears;

            // Integer interpolation, rounding down: the year before the
            // maximum is very likely fatal and the maximum itself is certain.
            return stageRate + (int)((1000L - stageRate) * into / ramp);
        }

        private int StageRate(AgeStage stage)
        {
            switch (stage)
            {
                case AgeStage.Infant:
                    return InfantMortalityPerMille;
                case AgeStage.Child:
                    return ChildMortalityPerMille;
                case AgeStage.Adolescent:
                    return AdolescentMortalityPerMille;
                case AgeStage.Adult:
                    return AdultMortalityPerMille;
                default:
                    return ElderMortalityPerMille;
            }
        }

        private static void RequireAscending(long previous, long value, string name)
        {
            if (value <= previous)
            {
                throw new ArgumentOutOfRangeException(
                    name, value, name + " must come after " + previous + ".");
            }
        }

        private static void RequirePositive(long value, string name)
        {
            if (value <= 0L)
            {
                throw new ArgumentOutOfRangeException(name, value, name + " must be positive.");
            }
        }

        // One to a thousand: a multiplier of a thousand takes any base rate
        // to certainty, so nothing larger means anything, and bounding it is
        // what keeps the product of two of them inside a long.
        private static void RequireMultiplier(int value, string name)
        {
            if (value < 1 || value > 1000)
            {
                throw new ArgumentOutOfRangeException(name, value, name + " is a multiplier from 1 to 1000.");
            }
        }

        private static void RequireNonNegative(long value, string name)
        {
            if (value < 0L)
            {
                throw new ArgumentOutOfRangeException(name, value, name + " cannot be negative.");
            }
        }

        private static void RequirePerMille(int value, string name)
        {
            if (value < 0 || value > 1000)
            {
                throw new ArgumentOutOfRangeException(name, value, name + " is a chance per mille.");
            }
        }
    }
}
