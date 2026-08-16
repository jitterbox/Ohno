import { addAll, factorial, binomial, log, mul, mulAll, One, pow } from './cx';
import type { ComplexityExpression } from './expression';
import { format } from './formatter';
import { Monomial } from './monomial';

const MaxDistributedTerms = 64;

export function simplify(
  expression: ComplexityExpression,
): ComplexityExpression {
  return canonicalOrder(simplifyNode(expression));
}

function simplifyNode(
  expression: ComplexityExpression,
): ComplexityExpression {
  switch (expression.kind) {
    case 'const':
    case 'var':
    case 'unknown':
    case 'call':
      return expression;
    case 'factorial':
      return factorial(simplifyNode(expression.inner));
    case 'binomial':
      return binomial(
        simplifyNode(expression.n),
        simplifyNode(expression.k),
      );
    case 'log':
      return simplifyLog(simplifyNode(expression.inner));
    case 'pow':
      return pow(
        simplifyNode(expression.base),
        simplifyNode(expression.exponent),
      );
    case 'mul':
      return simplifyProduct(expression.factors.map(simplifyNode));
    case 'add':
      return simplifySum(expression.terms.map(simplifyNode));
  }
}

function simplifyLog(
  inner: ComplexityExpression,
): ComplexityExpression {
  if (inner.kind === 'const') return One;
  if (inner.kind === 'mul') {
    return simplifySum(inner.factors.map(simplifyLog));
  }
  if (inner.kind === 'pow' && inner.exponent.kind === 'const') {
    return log(inner.base);
  }
  return log(inner);
}

function simplifyProduct(
  factors: readonly ComplexityExpression[],
): ComplexityExpression {
  const list = [...factors];
  const sumIndex = list.findIndex((f) => f.kind === 'add');
  if (sumIndex >= 0 && list[sumIndex].kind === 'add') {
    const sum = list[sumIndex];
    if (sum.kind === 'add' && sum.terms.length <= MaxDistributedTerms) {
      const rest = list.filter((_, i) => i !== sumIndex);
      return simplifySum(sum.terms.map((term) => mul(...rest, term)));
    }
  }
  const nonConstants = list.filter((f) => f.kind !== 'const');
  return nonConstants.length === 0 ? One : mulAll(nonConstants);
}

function simplifySum(
  terms: Iterable<ComplexityExpression>,
): ComplexityExpression {
  const flat: ComplexityExpression[] = [];
  for (const term of terms) {
    const normalized = term.kind === 'mul'
      ? simplifyProduct(term.factors)
      : term;
    if (normalized.kind === 'add') {
      flat.push(...normalized.terms);
    } else if (!(normalized.kind === 'const' && normalized.value === 0)) {
      flat.push(normalized);
    }
  }

  const kept: {
    expr: ComplexityExpression;
    mono: Monomial | undefined;
  }[] = [];
  for (const term of flat) {
    const monomial = Monomial.tryFrom(term);
    let dominated = false;
    for (let i = kept.length - 1; i >= 0; i--) {
      const other = kept[i].mono;
      if (!monomial || !other) continue;
      if (other.dominates(monomial)) {
        dominated = true;
        break;
      }
      if (monomial.dominates(other)) kept.splice(i, 1);
    }
    if (!dominated) kept.push({ expr: term, mono: monomial });
  }
  return addAll(kept.map((k) => k.expr));
}

function canonicalOrder(
  expression: ComplexityExpression,
): ComplexityExpression {
  switch (expression.kind) {
    case 'add': {
      const terms = expression.terms
        .map(canonicalOrder)
        .sort(byFormat);
      return terms.length === 1 ? terms[0] : { kind: 'add', terms };
    }
    case 'mul': {
      const factors = expression.factors
        .map(canonicalOrder)
        .sort((a, b) => productRank(a) - productRank(b) || byFormat(a, b));
      return factors.length === 1
        ? factors[0]
        : { kind: 'mul', factors };
    }
    case 'log':
      return { kind: 'log', inner: canonicalOrder(expression.inner) };
    case 'pow':
      return {
        kind: 'pow',
        base: canonicalOrder(expression.base),
        exponent: canonicalOrder(expression.exponent),
      };
    case 'factorial':
      return {
        kind: 'factorial',
        inner: canonicalOrder(expression.inner),
      };
    case 'binomial':
      return {
        kind: 'binomial',
        n: canonicalOrder(expression.n),
        k: canonicalOrder(expression.k),
      };
    default:
      return expression;
  }
}

function byFormat(a: ComplexityExpression, b: ComplexityExpression): number {
  const left = format(a);
  const right = format(b);
  return left < right ? -1 : left > right ? 1 : 0;
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
