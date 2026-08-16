import {
  add,
  binomial,
  call,
  constant,
  factorial,
  log,
  mul,
  unknown,
  variable,
  pow,
} from './cx';
import type { ComplexityExpression } from './expression';

export interface VectorExpr {
  op: string;
  value?: number;
  name?: string;
  inner?: VectorExpr;
  base?: VectorExpr;
  exp?: VectorExpr;
  n?: VectorExpr;
  k?: VectorExpr;
  args?: VectorExpr[];
  reason?: string;
}

export function parseVector(expr: VectorExpr): ComplexityExpression {
  switch (expr.op) {
    case 'const':
      return constant(expr.value ?? 0);
    case 'var':
      return variable(expr.name ?? 'n');
    case 'log':
      return log(parseVector(expr.inner!));
    case 'factorial':
      return factorial(parseVector(expr.inner!));
    case 'pow':
      return pow(parseVector(expr.base!), parseVector(expr.exp!));
    case 'binomial':
      return binomial(parseVector(expr.n!), parseVector(expr.k!));
    case 'add':
      return add(...(expr.args ?? []).map(parseVector));
    case 'mul':
      return mul(...(expr.args ?? []).map(parseVector));
    case 'call':
      return call(expr.name ?? 'f');
    case 'unknown':
      return unknown(expr.reason ?? '');
    default:
      throw new Error(`Unknown algebra op: ${expr.op}`);
  }
}
