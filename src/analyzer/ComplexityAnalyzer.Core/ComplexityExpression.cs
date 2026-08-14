using System.Collections.Immutable;

namespace ComplexityAnalyzer.Core;

/// <summary>
/// Base type for symbolic complexity expressions. Expressions are immutable
/// and never modeled as strings; formatting is a presentation concern.
/// </summary>
public abstract record ComplexityExpression;

/// <summary>An integer constant. <c>Constant(1)</c> is the multiplicative identity.</summary>
public sealed record ConstantExpression(int Value) : ComplexityExpression;

/// <summary>A symbolic input dimension, e.g. n, m, k.</summary>
public sealed record VariableExpression(string Name) : ComplexityExpression;

/// <summary>Logarithm of an expression, e.g. log(n).</summary>
public sealed record LogExpression(ComplexityExpression Inner) : ComplexityExpression;

/// <summary>Base raised to an exponent, e.g. n^2 or 2^n.</summary>
public sealed record PowerExpression(ComplexityExpression Base, ComplexityExpression Exponent) : ComplexityExpression;

/// <summary>Factorial of an expression, e.g. n!.</summary>
public sealed record FactorialExpression(ComplexityExpression Inner) : ComplexityExpression;

/// <summary>Product of factors, e.g. n * log(k).</summary>
public sealed record ProductExpression(IImmutableList<ComplexityExpression> Factors) : ComplexityExpression;

/// <summary>Sum of terms, e.g. n + m.</summary>
public sealed record SumExpression(IImmutableList<ComplexityExpression> Terms) : ComplexityExpression;

/// <summary>
/// A cost that cannot be inferred at all. Must remain visible in expressions
/// rather than being silently dropped.
/// </summary>
public sealed record UnknownExpression(string Reason) : ComplexityExpression;

/// <summary>
/// The opaque cost of a resolvable-by-name but unanalyzable call, C(foo).
/// </summary>
public sealed record FunctionCostExpression(string FunctionName) : ComplexityExpression;
