namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Compiler shim. <c>init</c> accessors and positional records emit a
    /// reference to this type, and netstandard2.1 does not ship it - so Core
    /// has to supply it or neither feature compiles.
    ///
    /// Internal on purpose: every assembly that needs it declares its own, and
    /// a public copy would collide with the BCL's once Core moves off
    /// netstandard2.1 (Unity 6.8 replaces Mono with CoreCLR on .NET 10).
    /// Deleting this file at that point is the whole migration for records.
    /// </summary>
    internal static class IsExternalInit
    {
    }
}
