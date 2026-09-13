using System;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;

namespace KingdomWatch.Core.Relationships
{
    /// <summary>
    /// What one person feels toward another. Directed: Aldric's tie toward
    /// Mira and hers toward him are two records that can disagree.
    /// </summary>
    /// <remarks>
    /// Three bounded integers rather than a float, because these are read in
    /// branching code and section 5 requires integer arithmetic there.
    /// Liking is signed - dislike is negative liking. Resentment and
    /// familiarity have no meaningful negative, so they run from zero.
    ///
    /// <see cref="LastTouched"/> is what makes decay a pure function of the
    /// clock: <see cref="SocialTies.Decay"/> applies however many steps have
    /// elapsed since it and moves it forward, so calling decay twice for the
    /// same instant decays nothing twice.
    /// </remarks>
    public readonly struct SocialTie : IEquatable<SocialTie>
    {
        public const sbyte MaxMagnitude = 100;

        internal SocialTie(
            EntityId toward,
            sbyte liking,
            sbyte resentment,
            sbyte familiarity,
            SimulationTime lastTouched)
        {
            Toward = toward;
            Liking = liking;
            Resentment = resentment;
            Familiarity = familiarity;
            LastTouched = lastTouched;
        }

        public EntityId Toward { get; }

        /// <summary>-<see cref="MaxMagnitude"/> to <see cref="MaxMagnitude"/>.</summary>
        public sbyte Liking { get; }

        /// <summary>0 to <see cref="MaxMagnitude"/>.</summary>
        public sbyte Resentment { get; }

        /// <summary>0 to <see cref="MaxMagnitude"/>.</summary>
        public sbyte Familiarity { get; }

        public SimulationTime LastTouched { get; }

        /// <summary>
        /// How much of a relationship this is, for deciding which tie to drop
        /// when a person is at their cap. Zero means nothing is left to keep.
        /// </summary>
        public int Weight => Math.Abs(Liking) + Resentment + Familiarity;

        public bool Equals(SocialTie other) =>
            Toward == other.Toward
            && Liking == other.Liking
            && Resentment == other.Resentment
            && Familiarity == other.Familiarity
            && LastTouched == other.LastTouched;

        public override bool Equals(object? obj) => obj is SocialTie other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Toward, Liking, Resentment, Familiarity);

        public override string ToString() =>
            "toward " + Toward + ": liking " + Liking + ", resentment " + Resentment
            + ", familiarity " + Familiarity;

        public static bool operator ==(SocialTie left, SocialTie right) => left.Equals(right);

        public static bool operator !=(SocialTie left, SocialTie right) => !left.Equals(right);
    }
}
