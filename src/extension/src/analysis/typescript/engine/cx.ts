import type {
  ComplexityExpression,
  PowerExpression,
  ProductExpression,
} from './expression';

export const One: ComplexityExpression = { kind: 'const', value: 1 };

export function constant(value: number): ComplexityExpression {
  return value === 1 ? One : { kind: 'const', value };
}

export function variable(name: string): ComplexityExpression {
  return { kind: 'var', name };
}

export function log(inner: ComplexityExpression): ComplexityExpression {
  return inner.kind === 'const' ? One : { kind: 'log', inner };
}

export function factorial(
  inner: ComplexityExpression,
): ComplexityExpression {
  return { kind: 'factorial', inner };
}

export function binomial(
  n: ComplexityExpression,
  k: ComplexityExpression,
): ComplexityExpression {
  return { kind: 'binomial', n, k };
}

export function unknown(reason: string): ComplexityExpression {
  return { kind: 'unknown', reason };
}

export function call(functionName: string): ComplexityExpression {
  return { kind: 'call', functionName };
}

export function pow(
  baseExpr: ComplexityExpression,
  exponent: ComplexityExpression,
): ComplexityExpression {
  if (isConstValue(exponent, 0)) return One;
  if (isConstValue(exponent, 1)) return baseExpr;
  if (isConstValue(baseExpr, 1)) return One;
  if (baseExpr.kind === 'pow' && isConst(baseExpr.exponent)
    && isConst(exponent)) {
    const inner = baseExpr as PowerExpression;
    const a = (inner.exponent as { value: number }).value;
    const b = (exponent as { value: number }).value;
    return {
      kind: 'pow',
      base: inner.base,
      exponent: constant(a * b),
    };
  }
  return { kind: 'pow', base: baseExpr, exponent };
}

export function add(
  ...terms: ComplexityExpression[]
): ComplexityExpression {
  return addAll(terms);
}

export function addAll(
  terms: Iterable<ComplexityExpression>,
): ComplexityExpression {
  const flat: ComplexityExpression[] = [];
  for (const term of terms) {
    if (isConstValue(term, 0)) continue;
    if (term.kind === 'add') flat.push(...term.terms);
    else flat.push(term);
  }
  if (flat.length === 0) return One;
  if (flat.length === 1) return flat[0];
  return { kind: 'add', terms: flat };
}

export function mul(
  ...factors: ComplexityExpression[]
): ComplexityExpression {
  return mulAll(factors);
}

export function mulAll(
  factors: Iterable<ComplexityExpression>,
): ComplexityExpression {
  const flat: ComplexityExpression[] = [];
  let constantFactor = 1;
  for (const factor of factors) {
    if (isConstValue(factor, 0)) return One;
    if (factor.kind === 'const') {
      constantFactor *= factor.value;
      continue;
    }
    if (factor.kind === 'mul') {
      flat.push(...factor.factors);
      continue;
    }
    flat.push(factor);
  }
  const combined = combinePowers(flat);
  if (constantFactor !== 1) combined.unshift(constant(constantFactor));
  if (combined.length === 0) return One;
  if (combined.length === 1) return combined[0];
  return { kind: 'mul', factors: combined };
}

function combinePowers(
  factors: ComplexityExpression[],
): ComplexityExpression[] {
  const result: ComplexityExpression[] = [];
  const powers: {
    key: string;
    base: ComplexityExpression;
    exponent: number;
  }[] = [];
  const index = new Map<string, number>();

  for (const factor of factors) {
    const parsed = powerIncrement(factor);
    if (!parsed) {
      result.push(factor);
      continue;
    }
    const existing = index.get(parsed.key);
    if (existing !== undefined) {
      powers[existing].exponent += parsed.increment;
      continue;
    }
    index.set(parsed.key, powers.length);
    powers.push({
      key: parsed.key,
      base: parsed.base,
      exponent: parsed.increment,
    });
  }

  for (const item of powers) {
    result.push(
      item.exponent === 1
        ? item.base
        : { kind: 'pow', base: item.base, exponent: constant(item.exponent) },
    );
  }
  return result;
}

function powerIncrement(factor: ComplexityExpression): {
  key: string;
  base: ComplexityExpression;
  increment: number;
} | undefined {
  if (factor.kind === 'var') {
    return { key: factor.name, base: factor, increment: 1 };
  }
  if (factor.kind === 'pow' && factor.base.kind === 'var'
    && factor.exponent.kind === 'const') {
    return {
      key: factor.base.name,
      base: factor.base,
      increment: factor.exponent.value,
    };
  }
  return undefined;
}

function isConst(
  expression: ComplexityExpression,
): expression is { kind: 'const'; value: number } {
  return expression.kind === 'const';
}

function isConstValue(
  expression: ComplexityExpression,
  value: number,
): boolean {
  return expression.kind === 'const' && expression.value === value;
}

export function isProduct(
  expression: ComplexityExpression,
): expression is ProductExpression {
  return expression.kind === 'mul';
}
