import { format } from './formatter';
import type { ComplexityExpression } from './expression';

interface VariableMeasure {
  factorial: number;
  exponential: number;
  power: number;
  logPower: number;
}

const Zero: VariableMeasure = {
  factorial: 0,
  exponential: 0,
  power: 0,
  logPower: 0,
};

function addMeasure(
  a: VariableMeasure,
  b: VariableMeasure,
): VariableMeasure {
  return {
    factorial: a.factorial + b.factorial,
    exponential: a.exponential + b.exponential,
    power: a.power + b.power,
    logPower: a.logPower + b.logPower,
  };
}

function compareMeasure(a: VariableMeasure, b: VariableMeasure): number {
  return a.factorial - b.factorial
    || a.exponential - b.exponential
    || a.power - b.power
    || a.logPower - b.logPower;
}

function measure(
  factorial: number,
  exponential: number,
  power: number,
  logPower: number,
): VariableMeasure {
  return { factorial, exponential, power, logPower };
}

export class Monomial {
  constructor(
    readonly variables: ReadonlyMap<string, VariableMeasure>,
    readonly opaques: ReadonlySet<string>,
  ) {}

  get isConstant(): boolean {
    return this.variables.size === 0 && this.opaques.size === 0;
  }

  static tryFrom(
    expression: ComplexityExpression,
  ): Monomial | undefined {
    const variables = new Map<string, VariableMeasure>();
    const opaques = new Set<string>();
    if (!accumulate(expression, variables, opaques)) return undefined;
    return new Monomial(variables, opaques);
  }

  dominates(other: Monomial): boolean {
    if (!isSuperset(this.opaques, other.opaques)) return false;
    if (this.equivalentTo(other)) return true;
    if (this.logProductDominatesVariable(other)) return true;
    if (this.logProductDominatesLogProduct(other)) return true;
    if (other.isConstant) return true;
    if (this.isConstant) return false;

    let anyStrict = this.opaques.size > other.opaques.size;
    const keys = new Set([
      ...this.variables.keys(),
      ...other.variables.keys(),
    ]);
    for (const key of keys) {
      const mine = this.variables.get(key) ?? Zero;
      const theirs = other.variables.get(key) ?? Zero;
      const comparison = compareMeasure(mine, theirs);
      if (comparison < 0) return false;
      if (comparison > 0) anyStrict = true;
    }
    return anyStrict;
  }

  equivalentTo(other: Monomial): boolean {
    if (!setEquals(this.opaques, other.opaques)) return false;
    if (this.variables.size !== other.variables.size) return false;
    for (const [key, mine] of this.variables) {
      const theirs = other.variables.get(key);
      if (!theirs || compareMeasure(mine, theirs) !== 0) return false;
    }
    return true;
  }

  private logProductDominatesVariable(other: Monomial): boolean {
    if (other.opaques.size > 0 || other.variables.size !== 1) {
      return false;
    }
    const [name, otherMeasure] = singleEntry(other.variables);
    if (compareMeasure(otherMeasure, measure(0, 0, 1, 0)) !== 0) {
      return false;
    }
    const mine = this.variables.get(name);
    if (!mine || mine.logPower < 1) return false;
    for (const [key, value] of this.variables) {
      if (key !== name && value.power >= 1) return true;
    }
    return false;
  }

  private logProductDominatesLogProduct(other: Monomial): boolean {
    if (other.opaques.size > 0 || other.variables.size !== 1) {
      return false;
    }
    const [name, otherMeasure] = singleEntry(other.variables);
    if (name !== 'k') return false;
    if (compareMeasure(otherMeasure, measure(0, 0, 1, 1)) !== 0) {
      return false;
    }
    const mine = this.variables.get('k');
    if (!mine || mine.logPower < 1) return false;
    const cover = this.variables.get('n');
    return !!cover && cover.power >= 1;
  }
}

function accumulate(
  expression: ComplexityExpression,
  variables: Map<string, VariableMeasure>,
  opaques: Set<string>,
): boolean {
  switch (expression.kind) {
    case 'const':
      return true;
    case 'var':
      addVar(expression.name, measure(0, 0, 1, 0), variables);
      return true;
    case 'log':
      return accumulateLog(expression.inner, variables);
    case 'pow':
      return accumulatePow(expression, variables);
    case 'factorial':
      if (expression.inner.kind !== 'var') return false;
      addVar(expression.inner.name, measure(1, 0, 0, 0), variables);
      return true;
    case 'mul':
      return expression.factors.every((f) =>
        accumulate(f, variables, opaques));
    case 'binomial':
      opaques.add(`binomial:${format(expression)}`);
      return true;
    case 'call':
      opaques.add(`call:${expression.functionName}`);
      return true;
    case 'unknown':
      opaques.add(`unknown:${expression.reason}`);
      return true;
    default:
      return false;
  }
}

function accumulateLog(
  inner: ComplexityExpression,
  variables: Map<string, VariableMeasure>,
): boolean {
  if (inner.kind === 'var') {
    addVar(inner.name, measure(0, 0, 0, 1), variables);
    return true;
  }
  if (inner.kind === 'pow' && inner.base.kind === 'var'
    && inner.exponent.kind === 'const') {
    addVar(inner.base.name, measure(0, 0, 0, 1), variables);
    return true;
  }
  return false;
}

function accumulatePow(
  expression: Extract<ComplexityExpression, { kind: 'pow' }>,
  variables: Map<string, VariableMeasure>,
): boolean {
  if (expression.base.kind === 'var'
    && expression.exponent.kind === 'const') {
    addVar(
      expression.base.name,
      measure(0, 0, expression.exponent.value, 0),
      variables,
    );
    return true;
  }
  if (expression.base.kind === 'const'
    && expression.exponent.kind === 'var') {
    addVar(expression.exponent.name, measure(0, 1, 0, 0), variables);
    return true;
  }
  return false;
}

function addVar(
  name: string,
  added: VariableMeasure,
  variables: Map<string, VariableMeasure>,
): void {
  variables.set(name, addMeasure(variables.get(name) ?? Zero, added));
}

function isSuperset(a: ReadonlySet<string>, b: ReadonlySet<string>): boolean {
  for (const item of b) {
    if (!a.has(item)) return false;
  }
  return true;
}

function setEquals(a: ReadonlySet<string>, b: ReadonlySet<string>): boolean {
  if (a.size !== b.size) return false;
  return isSuperset(a, b);
}

function singleEntry<K, V>(map: ReadonlyMap<K, V>): [K, V] {
  const iterator = map.entries().next();
  if (iterator.done) throw new Error('empty map');
  return iterator.value;
}
