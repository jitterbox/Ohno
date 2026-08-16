export type ComplexityExpression =
  | ConstantExpression
  | VariableExpression
  | LogExpression
  | PowerExpression
  | FactorialExpression
  | BinomialExpression
  | ProductExpression
  | SumExpression
  | FunctionCostExpression
  | UnknownExpression;

export interface ConstantExpression {
  kind: 'const';
  value: number;
}

export interface VariableExpression {
  kind: 'var';
  name: string;
}

export interface LogExpression {
  kind: 'log';
  inner: ComplexityExpression;
}

export interface PowerExpression {
  kind: 'pow';
  base: ComplexityExpression;
  exponent: ComplexityExpression;
}

export interface FactorialExpression {
  kind: 'factorial';
  inner: ComplexityExpression;
}

export interface BinomialExpression {
  kind: 'binomial';
  n: ComplexityExpression;
  k: ComplexityExpression;
}

export interface ProductExpression {
  kind: 'mul';
  factors: readonly ComplexityExpression[];
}

export interface SumExpression {
  kind: 'add';
  terms: readonly ComplexityExpression[];
}

export interface FunctionCostExpression {
  kind: 'call';
  functionName: string;
}

export interface UnknownExpression {
  kind: 'unknown';
  reason: string;
}

export function isConst(
  e: ComplexityExpression,
  value?: number,
): e is ConstantExpression {
  return e.kind === 'const' && (value === undefined || e.value === value);
}

export function isVar(e: ComplexityExpression): e is VariableExpression {
  return e.kind === 'var';
}
