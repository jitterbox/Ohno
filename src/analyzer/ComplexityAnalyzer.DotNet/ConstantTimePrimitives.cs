namespace ComplexityAnalyzer.DotNet;

/// <summary>
/// The set of BCL members that are constant time <em>because we say so
/// here</em>, not because nothing else matched.
/// </summary>
/// <remarks>
/// Ohno's rule is that O(1) is never a fallback: an unresolved
/// executable operation costs <c>C(name)</c> at Low confidence. This
/// allowlist is the other half of that rule — the places where constant
/// time is positively known.
/// <para>
/// Membership is keyed by containing type, because the same member name
/// is not the same cost on every type: <c>int.GetHashCode()</c> is
/// Θ(1) and <c>string.GetHashCode()</c> is Θ(length). Name-only
/// matching is exactly the "not positively known" case this type
/// exists to prevent.
/// </para>
/// <para>
/// Anything with a size-dependent cost belongs in
/// <see cref="OperationCatalog"/> with a real template instead.
/// </para>
/// </remarks>
public static class ConstantTimePrimitives
{
    /// <summary>
    /// Types whose entire instance/static surface is fixed-size work:
    /// scalar math, intrinsics, and runtime services that do not touch
    /// a collection.
    /// </summary>
    private static readonly HashSet<string> ConstantTypes = new(
        StringComparer.Ordinal)
    {
        "System.Math",
        "System.MathF",
        "System.GC",
        "System.Environment",
        "System.BitConverter",
        "System.Numerics.BitOperations",
        "System.Runtime.CompilerServices.RuntimeHelpers",
        "System.Runtime.CompilerServices.Unsafe",
        "System.Diagnostics.Stopwatch",
        "System.Diagnostics.Debug",
        "System.Threading.Interlocked",
        "System.Threading.Volatile",
        "System.Threading.Monitor",
        "System.Object",
        "System.ValueType",
        "System.Nullable`1",
        "System.Collections.Generic.KeyValuePair`2",
        "System.Index",
        "System.Range",
        // Ohno makes no claim about I/O (README "What Ohno is not"),
        // so console writes are local formatting work, not a dimension.
        "System.Console",
        "System.DateTime",
        "System.DateTimeOffset",
        "System.TimeSpan",
        "System.DateOnly",
        "System.TimeOnly",
    };

    /// <summary>
    /// Fixed-width scalars. Their <c>Equals</c>, <c>GetHashCode</c>,
    /// <c>CompareTo</c>, <c>ToString</c>, and parse/format members are
    /// bounded by the width of the type, not by an input size.
    /// </summary>
    private static readonly HashSet<string> ScalarTypes = new(
        StringComparer.Ordinal)
    {
        "System.Boolean",
        "System.Byte",
        "System.SByte",
        "System.Char",
        "System.Int16",
        "System.UInt16",
        "System.Int32",
        "System.UInt32",
        "System.Int64",
        "System.UInt64",
        "System.Int128",
        "System.UInt128",
        "System.Single",
        "System.Double",
        "System.Decimal",
        "System.Half",
        "System.IntPtr",
        "System.UIntPtr",
        "System.Guid",
        "System.Enum",
        "System.DateTime",
        "System.DateTimeOffset",
        "System.TimeSpan",
    };

    /// <summary>
    /// Members that are constant time on a fixed-width scalar receiver
    /// and size-dependent on anything else.
    /// </summary>
    private static readonly HashSet<string> ScalarMembers = new(
        StringComparer.Ordinal)
    {
        "Equals",
        "GetHashCode",
        "CompareTo",
        "ToString",
        "Parse",
        "TryParse",
        "TryFormat",
        "GetTypeCode",
        "op_Equality",
        "op_Inequality",
    };

    /// <summary>
    /// Property getters that read a stored field or a fixed-size view.
    /// Keyed by member name and only consulted for BCL types. Indexers
    /// are not listed here — <c>get_Item</c> is O(1) on a list and
    /// O(n) on <c>ImmutableList</c>, so each type belongs in the
    /// catalog. A user type's <c>Count</c> walks its getter body.
    /// </summary>
    private static readonly HashSet<string> ConstantAccessors = new(
        StringComparer.Ordinal)
    {
        "get_Length",
        "get_Count",
        "get_LongLength",
        "get_Rank",
        "get_Capacity",
        "get_IsEmpty",
        "get_IsReadOnly",
        "get_Key",
        "get_Value",
        "get_HasValue",
        "get_Current",
        "get_Keys",
        "get_Values",
        "get_Comparer",
        "get_IsDefault",
        "get_IsDefaultOrEmpty",
        "get_Span",
        "get_Memory",
        "get_Chars",
        "get_UnorderedItems",
        // Cached singletons: reading one is a static field load.
        "get_Ordinal",
        "get_OrdinalIgnoreCase",
        "get_CurrentCulture",
        "get_CurrentCultureIgnoreCase",
        "get_InvariantCulture",
        "get_InvariantCultureIgnoreCase",
        "get_Default",
        "get_Empty",
        "get_Instance",
        "get_Shared",
        "get_MaxValue",
        "get_MinValue",
        "get_Now",
        "get_UtcNow",
        "get_Today",
        "get_Zero",
        "get_One",
        "get_Epsilon",
        "get_NaN",
    };

    /// <summary>
    /// Individual members that are fixed-size work on a type whose
    /// wider surface is not. Keyed <c>Type#Member</c>.
    /// </summary>
    private static readonly HashSet<string> ConstantMembers = new(
        StringComparer.Ordinal)
    {
        "System.Threading.Tasks.Task#Yield",
        "System.Threading.Tasks.Task#FromResult",
        "System.Threading.Tasks.Task#CompletedTask",
        "System.Threading.Tasks.Task#Delay",
        "System.Threading.CancellationToken#ThrowIfCancellationRequested",
        "System.ArgumentNullException#ThrowIfNull",
        "System.ArgumentException#ThrowIfNullOrEmpty",
        "System.ObjectDisposedException#ThrowIf",
        "System.Array#Empty",
        "System.Array#GetLength",
        "System.Array#GetUpperBound",
        "System.Array#GetLowerBound",
        "System.String#IsNullOrEmpty",
        "System.String#get_Length",
        "System.String#get_Chars",
        "System.Char#IsDigit",
        "System.Char#IsLetter",
        "System.Char#IsLetterOrDigit",
        "System.Char#IsWhiteSpace",
        "System.Char#IsUpper",
        "System.Char#IsLower",
        "System.Char#ToUpper",
        "System.Char#ToLower",
        "System.Char#ToUpperInvariant",
        "System.Char#ToLowerInvariant",
        "System.Char#GetNumericValue",
    };

    /// <summary>
    /// True when this member's cost is fixed regardless of any input
    /// size. A false answer is not a claim that the member is
    /// expensive — it means the cost is not established here, so the
    /// caller must fall back to <c>C(name)</c> rather than to O(1).
    /// </summary>
    public static bool IsConstant(string? containingType, string member)
    {
        if (containingType is null) return false;
        if (ConstantTypes.Contains(containingType)) return true;
        if (ConstantMembers.Contains($"{containingType}#{member}"))
            return true;
        if (ScalarTypes.Contains(containingType)
            && ScalarMembers.Contains(member))
        {
            return true;
        }

        return IsBclType(containingType)
            && ConstantAccessors.Contains(member);
    }

    /// <summary>
    /// True for a property getter that reads a stored field or a
    /// fixed-size view on a framework type.
    /// </summary>
    public static bool IsConstantAccessor(
        string? containingType, string member) =>
        containingType is not null
        && (ConstantTypes.Contains(containingType)
            || IsBclType(containingType))
        && ConstantAccessors.Contains(member);

    /// <summary>
    /// Constructors that allocate a fixed-size object. A ctor taking a
    /// size or a source collection is sized work and belongs in the
    /// catalog; this covers the parameterless and comparer-only forms.
    /// </summary>
    public static bool IsConstantConstruction(
        string? containingType, int arity) =>
        arity == 0 && IsBclType(containingType);

    private static bool IsBclType(string? containingType) =>
        containingType is not null
        && (containingType.StartsWith("System.", StringComparison.Ordinal)
            || containingType.StartsWith("Microsoft.", StringComparison.Ordinal));
}
