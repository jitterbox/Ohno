import type { ComplexityExpression } from './expression';
import { simplify } from './simplifier';
import type { RecognizedPattern } from './types';

export function formatExplanation(
  time: ComplexityExpression,
  patterns: readonly RecognizedPattern[],
): string {
  if (time.kind === 'unknown') {
    const unknown = patterns.find((p) => p.effect === 'unknown');
    const reason = unknown?.reason
      ?? time.reason
      ?? 'it depends on information that is not in this method';
    return unknownText(reason);
  }
  const range = patterns.find(
    (p) => p.effect === 'range' && (p.rangeExplanation?.length ?? 0) > 0,
  );
  if (range?.rangeExplanation) return range.rangeExplanation;
  if (containsCall(time)) return '';
  return phrase(simplify(time));
}

export function unknownText(reason: string): string {
  return 'Unknown: The complexity cannot be easily determined because '
    + `${reason}.`;
}

function phrase(time: ComplexityExpression): string {
  if (time.kind === 'const') return 'Constant time';
  if (time.kind === 'var') return 'Linear time';
  if (time.kind === 'log') return 'Logarithmic time';
  if (time.kind === 'pow' && time.exponent.kind === 'const') {
    if (time.exponent.value === 2) return 'Quadratic time';
    if (time.exponent.value === 3) return 'Cubic time';
  }
  if (time.kind === 'mul' && isLinearithmic(time.factors)) {
    return 'Linearithmic time';
  }
  if (time.kind === 'mul' && isMultilinear(time.factors)) {
    return time.factors.length === 2
      ? 'Bilinear time'
      : 'Multilinear time';
  }
  if (time.kind === 'mul' && hasExponential(time.factors)) {
    return 'Exponential time';
  }
  if (time.kind === 'mul' && hasFactorial(time.factors)) {
    return 'Factorial time';
  }
  if (time.kind === 'mul' && hasBinomial(time.factors)) {
    return 'Combinatorial time';
  }
  if (time.kind === 'add'
    && time.terms.every((t) => t.kind === 'var' || t.kind === 'const')) {
    return 'Linear time';
  }
  if (time.kind === 'pow' && time.base.kind === 'const'
    && time.exponent.kind === 'var') {
    return 'Exponential time';
  }
  if (time.kind === 'factorial') return 'Factorial time';
  if (time.kind === 'binomial') return 'Combinatorial time';
  return '';
}

function isLinearithmic(
  factors: readonly ComplexityExpression[],
): boolean {
  return factors.some((f) => f.kind === 'log')
    && factors.some((f) => f.kind === 'var');
}

function isMultilinear(
  factors: readonly ComplexityExpression[],
): boolean {
  return factors.length >= 2 && factors.every((f) => f.kind === 'var');
}

function hasExponential(
  factors: readonly ComplexityExpression[],
): boolean {
  return factors.some((f) =>
    f.kind === 'pow' && f.base.kind === 'const' && f.exponent.kind === 'var');
}

function hasFactorial(
  factors: readonly ComplexityExpression[],
): boolean {
  return factors.some((f) => f.kind === 'factorial');
}

function hasBinomial(
  factors: readonly ComplexityExpression[],
): boolean {
  return factors.some((f) => f.kind === 'binomial');
}

function containsCall(expression: ComplexityExpression): boolean {
  if (expression.kind === 'call') return true;
  return childrenOf(expression).some(containsCall);
}

function childrenOf(
  expression: ComplexityExpression,
): ComplexityExpression[] {
  switch (expression.kind) {
    case 'add':
      return [...expression.terms];
    case 'mul':
      return [...expression.factors];
    case 'log':
    case 'factorial':
      return [expression.inner];
    case 'pow':
      return [expression.base, expression.exponent];
    case 'binomial':
      return [expression.n, expression.k];
    default:
      return [];
  }
}
