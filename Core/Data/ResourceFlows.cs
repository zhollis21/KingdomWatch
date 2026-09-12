namespace KingdomWatch.Core.Data
{
    /// <summary>
    /// The cumulative flows of one resource through one
    /// <see cref="ResourceLedger"/> over its whole life: everything that ever
    /// entered, and everything that ever left. The two halves of section 12's
    /// conservation audit.
    /// </summary>
    /// <remarks>
    /// Each counter only ever grows. They are 64-bit because they accumulate
    /// over centuries where the live buckets only ever hold what exists now.
    /// Plain arithmetic throughout: see the overflow remarks on
    /// <see cref="ResourceLedger"/>.
    /// </remarks>
    public readonly struct ResourceFlows
    {
        internal ResourceFlows(
            long opening,
            long produced,
            long gathered,
            long imported,
            long consumed,
            long exported,
            long destroyed,
            long embodied)
        {
            Opening = opening;
            Produced = produced;
            Gathered = gathered;
            Imported = imported;
            Consumed = consumed;
            Exported = exported;
            Destroyed = destroyed;
            Embodied = embodied;
        }

        /// <summary>Stock the ledger started with, before any simulation ran.</summary>
        public long Opening { get; }

        /// <summary>Made by recipes from other stock.</summary>
        public long Produced { get; }

        /// <summary>Taken from the world by gathering recipes.</summary>
        public long Gathered { get; }

        /// <summary>Received from another ledger.</summary>
        public long Imported { get; }

        /// <summary>Used up - eaten, burnt, or transformed by a recipe.</summary>
        public long Consumed { get; }

        /// <summary>Handed to another ledger.</summary>
        public long Exported { get; }

        /// <summary>Lost - spoiled, raided, burned down.</summary>
        public long Destroyed { get; }

        /// <summary>Built into something that is no longer stock.</summary>
        public long Embodied { get; }

        /// <summary>Everything that ever entered.</summary>
        public long TotalIn => Opening + Produced + Gathered + Imported;

        /// <summary>Everything that ever left.</summary>
        public long TotalOut => Consumed + Exported + Destroyed + Embodied;
    }
}
