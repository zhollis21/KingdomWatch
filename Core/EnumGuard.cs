using System;
using System.Globalization;

namespace KingdomWatch.Core
{
    /// <summary>
    /// Cheap "is this actually a declared member?" checks for enums.
    /// </summary>
    /// <remarks>
    /// An enum parameter looks like the type system pins it to the declared
    /// members, but an enum is an int with names and any int casts in. Public
    /// API in Core that takes an enum has to check, because several of these
    /// values are persisted - an unrecognised one reaches history and saves and
    /// can never be resolved again.
    ///
    /// Enum.IsDefined would do this in a line, but it is reflection-based and
    /// boxes its argument, and DeterministicRng.Key runs on every random draw.
    /// The mask is built once per enum and indexed from then on.
    ///
    /// A mask rather than a range check because these enums are documented as
    /// append-only: a range would silently widen as members are added, and
    /// would accept a gap if a value were ever reserved.
    ///
    /// All of Core's enums number from 0 upward. A negative member would be
    /// reported undefined and rejected at the call site - loud, if less precise
    /// than it could be.
    /// </remarks>
    internal static class EnumGuard
    {
        /// <summary>
        /// Builds a lookup marking every declared value of an enum. Call once
        /// into a static readonly field, never per call.
        /// </summary>
        internal static bool[] BuildMask(Type enumType)
        {
            var values = Enum.GetValues(enumType);
            var max = 0;

            foreach (var value in values)
            {
                var slot = Convert.ToInt32(value, CultureInfo.InvariantCulture);

                if (slot > max)
                {
                    max = slot;
                }
            }

            var mask = new bool[max + 1];

            foreach (var value in values)
            {
                mask[Convert.ToInt32(value, CultureInfo.InvariantCulture)] = true;
            }

            return mask;
        }

        /// <summary>True when the integer names a declared member.</summary>
        internal static bool IsDefined(bool[] mask, int value) =>
            value >= 0 && value < mask.Length && mask[value];
    }
}
