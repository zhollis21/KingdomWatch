using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace KingdomWatch.Core.Data
{
    /// <summary>
    /// A transformation of resources: what goes in, what comes out, and how
    /// long it takes. Data, not code - section 9's recipe graph is what gates
    /// capability, since there is no research system.
    /// </summary>
    /// <remarks>
    /// Deliberately the minimal shape. Section 9 also sketches
    /// <c>building</c>, <c>substitutes</c> and <c>degrades_to</c>; those
    /// arrive with the issues that give them meaning (buildings at M3, the
    /// substitution and degradation paths with #37) rather than as empty
    /// fields nothing reads.
    ///
    /// A recipe with no inputs is a gathering recipe - foraging, felling,
    /// quarrying - and its outputs come from the world rather than from
    /// stock. <see cref="IsGathering"/> is derived from the input list rather
    /// than stored, so the two cannot disagree. The distinction matters to the
    /// conservation audit: <see cref="ResourceLedger.CompleteRecipe"/> tallies
    /// gathered outputs separately from produced ones.
    ///
    /// Who runs a recipe, and the task state that lets the stepped simulation
    /// show one in progress, is #52's. This type only says what one is.
    /// </remarks>
    public sealed class Recipe
    {
        // Wrapped once here rather than per access, for the same reasons as
        // MobileGroup.Members: a live list could be downcast and mutated past
        // the constructor's checks, and a per-call wrapper would allocate in
        // whatever tick-loop path reads it.
        private readonly ReadOnlyCollection<ResourceQuantity> _inputs;
        private readonly ReadOnlyCollection<ResourceQuantity> _outputs;

        public Recipe(
            string name,
            IReadOnlyList<ResourceQuantity> inputs,
            IReadOnlyList<ResourceQuantity> outputs,
            long duration)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A recipe needs a name.", nameof(name));
            }

            if (inputs is null)
            {
                throw new ArgumentNullException(nameof(inputs));
            }

            if (outputs is null)
            {
                throw new ArgumentNullException(nameof(outputs));
            }

            if (outputs.Count == 0)
            {
                throw new ArgumentException(
                    "A recipe that makes nothing is not a recipe.", nameof(outputs));
            }

            if (duration <= 0L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(duration), duration, "A recipe takes a positive number of ticks.");
            }

            Name = name;
            Duration = duration;
            _inputs = Copy(inputs, nameof(inputs));
            _outputs = Copy(outputs, nameof(outputs));
        }

        public string Name { get; }

        /// <summary>What is consumed, in a stable order. Empty for gathering.</summary>
        public IReadOnlyList<ResourceQuantity> Inputs => _inputs;

        /// <summary>What is made, in a stable order. Never empty.</summary>
        public IReadOnlyList<ResourceQuantity> Outputs => _outputs;

        /// <summary>How long one run takes, in <see cref="Clock.SimulationTime"/> ticks.</summary>
        public long Duration { get; }

        /// <summary>
        /// True when the outputs come from the world rather than from stock.
        /// </summary>
        public bool IsGathering => _inputs.Count == 0;

        public override string ToString() => Name;

        // Copies so the caller's list cannot change the recipe afterwards, and
        // checks that each kind appears at most once. Two lines for the same
        // kind would be either a typo or an attempt at a merged line; refusing
        // is cheaper than guessing which, and it lets the ledger treat each
        // line independently.
        private static ReadOnlyCollection<ResourceQuantity> Copy(
            IReadOnlyList<ResourceQuantity> lines, string paramName)
        {
            var copy = new List<ResourceQuantity>(lines.Count);

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];

                // A defaulted struct never went through ResourceQuantity's
                // constructor, so its kind is None and its quantity is zero.
                ResourceQuantity.ThrowIfNotAResource(line.Kind, paramName);

                for (var j = 0; j < copy.Count; j++)
                {
                    if (copy[j].Kind == line.Kind)
                    {
                        throw new ArgumentException(
                            line.Kind + " appears more than once.", paramName);
                    }
                }

                copy.Add(line);
            }

            return copy.AsReadOnly();
        }
    }
}
