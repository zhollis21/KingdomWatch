namespace KingdomWatch.Core.Construction
{
    /// <summary>
    /// What a <see cref="Building"/> is. The economy ladder's section 4 names
    /// many more; each is appended by the issue that gives it a reason to be
    /// built (#142-#145, #98, #99, #112, #45).
    /// </summary>
    /// <remarks>
    /// Stored on every building and written into saves and the world hash,
    /// so append-only: renumbering repoints every saved building.
    /// </remarks>
    public enum BuildingKind : byte
    {
        /// <summary>Not a building. Guards against a defaulted field.</summary>
        None = 0,

        /// <summary>A household's home (#69 makes the stock of them a limit).</summary>
        House = 1,

        /// <summary>A farmstead: the building fields are laid out around.</summary>
        Barn = 2,

        /// <summary>A sown field, tied to a barn, worked by Farmers for Grain.</summary>
        Field = 3,
    }
}
