using System;

namespace KingdomWatch.Core.Events
{
    /// <summary>
    /// The top few <see cref="ReasonCode"/>s behind a decision, in the order
    /// the decision site ranked them. Rides on every <see cref="DomainEvent"/>.
    /// </summary>
    /// <remarks>
    /// Four inline fields and a count rather than an array: section 5 caps
    /// provenance at two to four reasons, and an array per event would be an
    /// allocation on every publish in a tick loop the design commits to keeping
    /// allocation-free (section 18). Four overloaded constructors instead of
    /// <c>params</c> for the same reason.
    ///
    /// Order is meaningful and preserved - first is the strongest contributor.
    /// A code may appear once: listing the same reason twice is a bug at the
    /// decision site, not a stronger reason.
    ///
    /// Most events carry no reasons at all. A birth or a natural death is not
    /// a decision, and <see cref="None"/> is the normal value for those.
    /// </remarks>
    public readonly struct Reasons : IEquatable<Reasons>
    {
        /// <summary>The most reasons one decision records.</summary>
        public const int MaxCount = 4;

        /// <summary>No reasons. Equal to <c>default</c>.</summary>
        public static readonly Reasons None = default;

        private static readonly bool[] DefinedCodes = EnumGuard.BuildMask(typeof(ReasonCode));

        private readonly ReasonCode _first;
        private readonly ReasonCode _second;
        private readonly ReasonCode _third;
        private readonly ReasonCode _fourth;

        public Reasons(ReasonCode first)
            : this(1, first, ReasonCode.None, ReasonCode.None, ReasonCode.None)
        {
        }

        public Reasons(ReasonCode first, ReasonCode second)
            : this(2, first, second, ReasonCode.None, ReasonCode.None)
        {
        }

        public Reasons(ReasonCode first, ReasonCode second, ReasonCode third)
            : this(3, first, second, third, ReasonCode.None)
        {
        }

        public Reasons(ReasonCode first, ReasonCode second, ReasonCode third, ReasonCode fourth)
            : this(4, first, second, third, fourth)
        {
        }

        private Reasons(int count, ReasonCode first, ReasonCode second, ReasonCode third, ReasonCode fourth)
        {
            Guard(first, nameof(first));

            if (count > 1)
            {
                Guard(second, nameof(second));
                RejectRepeat(second, nameof(second), first);
            }

            if (count > 2)
            {
                Guard(third, nameof(third));
                RejectRepeat(third, nameof(third), first, second);
            }

            if (count > 3)
            {
                Guard(fourth, nameof(fourth));
                RejectRepeat(fourth, nameof(fourth), first, second, third);
            }

            Count = count;
            _first = first;
            _second = second;
            _third = third;
            _fourth = fourth;
        }

        /// <summary>How many reasons were recorded, 0 to <see cref="MaxCount"/>.</summary>
        public int Count { get; }

        /// <summary>The reason at a rank, 0 being the strongest contributor.</summary>
        public ReasonCode this[int index]
        {
            get
            {
                if (index < 0 || index >= Count)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(index), index, "These reasons have " + Count + " entries.");
                }

                switch (index)
                {
                    case 0: return _first;
                    case 1: return _second;
                    case 2: return _third;
                    default: return _fourth;
                }
            }
        }

        /// <summary>
        /// True when <paramref name="code"/> was recorded. Asking about
        /// <see cref="ReasonCode.None"/> is answered false rather than
        /// refused - it is a defined member, and "no" is the honest answer -
        /// but an undefined value is refused like everywhere else in Core.
        /// </summary>
        public bool Contains(ReasonCode code)
        {
            if (!EnumGuard.IsDefined(DefinedCodes, (int)code))
            {
                throw new ArgumentOutOfRangeException(nameof(code), code, "Not a defined ReasonCode.");
            }

            return code != ReasonCode.None
                && (_first == code || _second == code || _third == code || _fourth == code);
        }

        public bool Equals(Reasons other) =>
            Count == other.Count
            && _first == other._first
            && _second == other._second
            && _third == other._third
            && _fourth == other._fourth;

        public override bool Equals(object? obj) => obj is Reasons other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Count, _first, _second, _third, _fourth);

        public override string ToString()
        {
            switch (Count)
            {
                case 0: return "[]";
                case 1: return "[" + _first + "]";
                case 2: return "[" + _first + ", " + _second + "]";
                case 3: return "[" + _first + ", " + _second + ", " + _third + "]";
                default: return "[" + _first + ", " + _second + ", " + _third + ", " + _fourth + "]";
            }
        }

        public static bool operator ==(Reasons left, Reasons right) => left.Equals(right);

        public static bool operator !=(Reasons left, Reasons right) => !left.Equals(right);

        private static void Guard(ReasonCode code, string parameter)
        {
            if (!EnumGuard.IsDefined(DefinedCodes, (int)code))
            {
                throw new ArgumentOutOfRangeException(parameter, code, "Not a defined ReasonCode.");
            }

            if (code == ReasonCode.None)
            {
                throw new ArgumentOutOfRangeException(
                    parameter,
                    code,
                    "ReasonCode.None is the defaulted-field guard, not a reason. Leave it out instead.");
            }
        }

        private static void RejectRepeat(ReasonCode code, string parameter, ReasonCode earlier)
        {
            if (code == earlier)
            {
                throw new ArgumentException(
                    code + " is listed twice. A reason contributes once; listing it again is not a stronger reason.",
                    parameter);
            }
        }

        private static void RejectRepeat(ReasonCode code, string parameter, ReasonCode a, ReasonCode b)
        {
            RejectRepeat(code, parameter, a);
            RejectRepeat(code, parameter, b);
        }

        private static void RejectRepeat(ReasonCode code, string parameter, ReasonCode a, ReasonCode b, ReasonCode c)
        {
            RejectRepeat(code, parameter, a, b);
            RejectRepeat(code, parameter, c);
        }
    }
}
