using ComplexityAnalyzer.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace ComplexityAnalyzer.CSharp;

internal static class SizeResolver
{
    public static ComplexityExpression Resolve(
        IOperation? operation, AnalysisState state)
    {
        return Unwrap(operation) switch
        {
            IParameterReferenceOperation p => state.SizeOf(p.Parameter),
            ILocalReferenceOperation l => state.SizeOf(l.Local),
            IFieldReferenceOperation f => state.SizeOf(f.Field),
            IPropertyReferenceOperation prop => FromProperty(prop, state),
            IArrayCreationOperation a => FromArrayCreation(a, state),
            IConversionOperation c => Resolve(c.Operand, state),
            IInvocationOperation inv => FromInvocation(inv, state),
            IObjectCreationOperation => Cx.One,
            _ => Cx.Var("n"),
        };
    }

    public static ISymbol? TargetSymbol(IOperation? operation)
    {
        return Unwrap(operation) switch
        {
            IParameterReferenceOperation p => p.Parameter,
            ILocalReferenceOperation l => l.Local,
            IFieldReferenceOperation f => f.Field,
            IPropertyReferenceOperation prop => TargetSymbol(prop.Instance),
            IConversionOperation c => TargetSymbol(c.Operand),
            _ => null,
        };
    }

    public static IOperation? Unwrap(IOperation? operation)
    {
        while (operation is IConversionOperation conversion)
            operation = conversion.Operand;
        return operation;
    }

    private static ComplexityExpression FromProperty(
        IPropertyReferenceOperation prop, AnalysisState state)
    {
        if (prop.Property.Name is "Count" or "Length" or "UnorderedItems")
            return Resolve(prop.Instance, state);
        return state.SizeOf(prop.Property);
    }

    private static ComplexityExpression FromInvocation(
        IInvocationOperation invocation, AnalysisState state)
    {
        if (invocation.Instance is not null)
            return Resolve(invocation.Instance, state);
        if (invocation.Arguments.Length > 0)
            return Resolve(invocation.Arguments[0].Value, state);
        return Cx.Var("n");
    }

    private static ComplexityExpression FromArrayCreation(
        IArrayCreationOperation creation, AnalysisState state)
    {
        if (creation.DimensionSizes.Length == 1)
            return Resolve(creation.DimensionSizes[0], state);
        return Cx.Var("n");
    }
}
