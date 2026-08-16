import type { ComplexityExpression } from './expression';
import type { ComplexityResult } from './types';

export function formatBigO(expression: ComplexityExpression): string {
  return `O(${format(expression)})`;
}

export function format(expression: ComplexityExpression): string {
  switch (expression.kind) {
    case 'const':
      return String(expression.value);
    case 'var':
      return expression.name;
    case 'log':
      return `log ${formatOperand(expression.inner)}`;
    case 'pow':
      return formatPower(expression.base, expression.exponent);
    case 'factorial':
      return `${formatOperand(expression.inner)}!`;
    case 'binomial':
      return `C(${format(expression.n)}, ${format(expression.k)})`;
    case 'mul':
      return formatProduct(expression.factors);
    case 'add':
      return expression.terms.map(format).join(' + ');
    case 'call':
      return `C(${expression.functionName})`;
    case 'unknown':
      return 'unknown';
  }
}

export function formatHeadline(result: ComplexityResult): string {
  return `Time ${formatBigO(result.time)} · Space ${formatBigO(result.space)} `
    + `· ${result.confidence} confidence`;
}

function formatProduct(
  factors: readonly ComplexityExpression[],
): string {
  const ordered = [...factors].sort((a, b) => {
    const rank = productRank(a) - productRank(b);
    if (rank !== 0) return rank;
    return format(a) < format(b) ? -1 : format(a) > format(b) ? 1 : 0;
  });
  return ordered.map(formatOperand).join(' ');
}

function productRank(expression: ComplexityExpression): number {
  switch (expression.kind) {
    case 'var':
      return 0;
    case 'pow':
      return 1;
    case 'log':
      return 2;
    case 'call':
      return 3;
    default:
      return 4;
  }
}

function formatPower(
  base: ComplexityExpression,
  exponent: ComplexityExpression,
): string {
  if (exponent.kind === 'const' && exponent.value === 2) {
    return `${formatOperand(base)}²`;
  }
  if (exponent.kind === 'const' && exponent.value === 3) {
    return `${formatOperand(base)}³`;
  }
  return `${formatOperand(base)}^${formatOperand(exponent)}`;
}

function formatOperand(expression: ComplexityExpression): string {
  if (expression.kind === 'add' || expression.kind === 'mul') {
    return `(${format(expression)})`;
  }
  return format(expression);
}
