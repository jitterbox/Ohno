import { add, addAll, mul } from './cx';
import type { ComposedCost } from './composedCost';
import { unitCost } from './composedCost';
import { meaningful, sequenceEvidence } from './evidencePruner';
import type { ComplexityExpression } from './expression';
import { simplify } from './simplifier';
import { minConfidence, type LineSpan } from './types';

export function sequential(
  parts: readonly ComposedCost[],
  span?: LineSpan,
): ComposedCost {
  if (parts.length === 0) return unitCost('sequence', 'empty', span);
  if (parts.length === 1) return parts[0];
  const time = simplify(addAll(parts.map((p) => p.time)));
  const space = simplify(peak(parts.map((p) => p.space)));
  return {
    time,
    space,
    confidence: parts.reduce(
      (c, p) => minConfidence(c, p.confidence),
      parts[0].confidence,
    ),
    evidence: sequenceEvidence(time, span, parts.map((p) => p.evidence)),
    warnings: concat(parts, (p) => p.warnings),
    suggestions: concat(parts, (p) => p.suggestions),
  };
}

export function conditional(
  condition: ComposedCost,
  whenTrue: ComposedCost,
  whenFalse: ComposedCost | undefined,
  span?: LineSpan,
): ComposedCost {
  const branch = whenFalse
    ? maxExpr(whenTrue.time, whenFalse.time)
    : whenTrue.time;
  const time = simplify(add(condition.time, branch));
  const spaces = whenFalse
    ? [condition.space, whenTrue.space, whenFalse.space]
    : [condition.space, whenTrue.space];
  const children = [condition.evidence, whenTrue.evidence];
  if (whenFalse) children.push(whenFalse.evidence);
  const parts = whenFalse
    ? [condition, whenTrue, whenFalse]
    : [condition, whenTrue];
  return {
    time,
    space: simplify(peak(spaces)),
    confidence: minConfidence(
      condition.confidence,
      minConfidence(whenTrue.confidence, whenFalse?.confidence ?? 'high'),
    ),
    evidence: {
      kind: 'conditional',
      label: 'worst-case branch',
      cost: time,
      span,
      children: meaningful(children),
    },
    warnings: concat(parts, (p) => p.warnings),
    suggestions: concat(parts, (p) => p.suggestions),
  };
}

export function loop(
  bound: ComplexityExpression,
  body: ComposedCost,
  label: string,
  span?: LineSpan,
): ComposedCost {
  const time = simplify(mul(bound, body.time));
  return {
    time,
    space: simplify(body.space),
    confidence: body.confidence,
    evidence: {
      kind: 'loop',
      label,
      cost: time,
      span,
      children: [body.evidence],
    },
    warnings: body.warnings,
    suggestions: body.suggestions,
  };
}

export function peak(
  spaces: Iterable<ComplexityExpression>,
): ComplexityExpression {
  const list = [...spaces];
  if (list.length === 0) return { kind: 'const', value: 1 };
  if (list.length === 1) return list[0];
  return simplify(addAll(list));
}

export function maxExpr(
  a: ComplexityExpression,
  b: ComplexityExpression,
): ComplexityExpression {
  return simplify(add(a, b));
}

function concat<T>(
  parts: readonly ComposedCost[],
  select: (part: ComposedCost) => readonly T[],
): T[] {
  return parts.flatMap((part) => [...select(part)]);
}
