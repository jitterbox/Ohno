using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace ComplexityAnalyzer.CSharp;

/// <summary>
/// Finds regexes whose engine gives a linear-time guarantee.
/// </summary>
/// <remarks>
/// The default .NET engine backtracks, so its cost depends on the
/// pattern and the input together and can blow up — Ohno reports that
/// as opaque, which is the honest answer.
/// <para>
/// <c>RegexOptions.NonBacktracking</c> (.NET 7+) is different: it
/// simulates the automaton and never revisits a character, so a match
/// is linear in the input length regardless of the pattern. That is a
/// documented property of the engine, not an inference about the
/// pattern, which is why it is the one case where naming the option
/// earns a real bound instead of an unknown.
/// </para>
/// <para>
/// The option has to be provable at the construction site. A
/// <c>Regex</c> arriving as a parameter or an unresolved field keeps
/// the opaque treatment: its options are not visible here.
/// </para>
/// </remarks>
internal static class RegexFacts
{
    private const string RegexType =
        "System.Text.RegularExpressions.Regex";

    private const string OptionsType =
        "System.Text.RegularExpressions.RegexOptions";

    private const string GeneratedAttribute =
        "System.Text.RegularExpressions.GeneratedRegexAttribute";

    /// <summary>
    /// Records every regex in this body whose options provably include
    /// <c>NonBacktracking</c>. Runs with the other preparation passes,
    /// before costing, so a declaration is seen no matter where the
    /// use appears.
    /// </summary>
    public static void Detect(IOperation body, AnalysisState state)
    {
        foreach (var op in OperationTree.SelfAndDescendants(body))
        {
            if (op is not IObjectCreationOperation create) continue;
            if (SymbolKeys.TypeName(create.Type) != RegexType) continue;
            if (!MentionsNonBacktracking(create.Arguments)) continue;

            var symbol = AssignedSymbol(create);
            if (symbol is not null)
                state.LinearRegexes.Add(symbol);
        }
    }

    /// <summary>
    /// True when this call runs a regex that is provably linear time.
    /// </summary>
    public static bool IsLinear(
        IInvocationOperation call, AnalysisState state)
    {
        if (SymbolKeys.TypeName(call.TargetMethod.ContainingType)
            != RegexType)
        {
            return false;
        }

        // Static form: the options travel with the call.
        if (call.Instance is null)
            return MentionsNonBacktracking(call.Arguments);

        // Instance form: the receiver has to resolve to a construction
        // we saw, or to a [GeneratedRegex] method declaring the option.
        if (IsGeneratedNonBacktracking(call.Instance)) return true;
        var symbol = SizeResolver.TargetSymbol(call.Instance);
        return symbol is not null && state.LinearRegexes.Contains(symbol);
    }

    /// <summary>
    /// The subject the match scans, so the bound is stated in the input
    /// rather than in a guessed dimension.
    /// </summary>
    public static IOperation? Subject(IInvocationOperation call) =>
        call.Arguments.Length == 0 ? null : call.Arguments[0].Value;

    /// <summary>
    /// Whether the call materializes its results (matches, a rewritten
    /// string, the split pieces) rather than answering yes or no.
    /// </summary>
    public static bool Materializes(string member) =>
        member is "Matches" or "Replace" or "Split";

    private static bool MentionsNonBacktracking(
        IEnumerable<IArgumentOperation> arguments)
    {
        foreach (var argument in arguments)
        {
            foreach (var op in
                OperationTree.SelfAndDescendants(argument.Value))
            {
                if (IsNonBacktrackingFlag(op)) return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The enum member, wherever it sits in an <c>|</c> chain.
    /// </summary>
    private static bool IsNonBacktrackingFlag(IOperation operation) =>
        operation is IFieldReferenceOperation
        {
            Field.Name: "NonBacktracking",
        } field
        && SymbolKeys.TypeName(field.Field.ContainingType) == OptionsType;

    private static bool IsGeneratedNonBacktracking(IOperation instance)
    {
        if (SizeResolver.Unwrap(instance) is not IInvocationOperation call)
            return false;

        foreach (var attribute in
            call.TargetMethod.GetAttributes())
        {
            if (SymbolKeys.TypeName(attribute.AttributeClass)
                != GeneratedAttribute)
            {
                continue;
            }

            foreach (var argument in attribute.ConstructorArguments)
            {
                if (HasNonBacktrackingBit(argument)) return true;
            }
        }

        return false;
    }

    /// <summary>
    /// <c>RegexOptions.NonBacktracking</c> is 0x400. In attribute
    /// metadata the options arrive already folded into one constant,
    /// so the bit is what there is to test.
    /// </summary>
    private static bool HasNonBacktrackingBit(TypedConstant argument)
    {
        const int nonBacktracking = 0x400;
        if (argument.Kind != TypedConstantKind.Enum) return false;
        return argument.Value is int value
            && (value & nonBacktracking) == nonBacktracking;
    }

    private static ISymbol? AssignedSymbol(IObjectCreationOperation create)
    {
        var parent = create.Parent;
        if (parent is IVariableInitializerOperation init
            && init.Parent is IVariableDeclaratorOperation declarator)
        {
            return declarator.Symbol;
        }

        if (parent is IFieldInitializerOperation field)
            return field.InitializedFields.FirstOrDefault();
        if (parent is ISimpleAssignmentOperation assign)
            return SizeResolver.TargetSymbol(assign.Target);
        return null;
    }
}
