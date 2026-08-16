import type { ComplexityExpression, RecognizedPattern } from '../engine';
import { annotatePattern } from './make';

const Soft = new Set([
  'await-opaque',
  'interface-dispatch',
  'dynamic-dispatch',
  'delegate-invoke',
  'parallel-loop',
  'cache-history',
]);

export function refine(
  patterns: readonly RecognizedPattern[],
  time: ComplexityExpression,
  recurrenceId?: string,
): RecognizedPattern[] {
  const extra: RecognizedPattern[] = [];
  if (recurrenceId) {
    extra.push(annotatePattern(
      recurrenceId,
      title(recurrenceId),
      'closed form from the recursive call shape, '
        + 'not a named textbook proof',
    ));
  }
  const structural = hasStructural(time);
  return [...extra, ...patterns]
    .filter((p) => !recurrenceId || p.id !== 'data-dependent-recursion')
    .filter(unique)
    .map((p) => soften(p, structural));
}

function soften(
  item: RecognizedPattern,
  structural: boolean,
): RecognizedPattern {
  if (!structural || !Soft.has(item.id) || item.effect !== 'unknown') {
    return item;
  }
  return {
    ...item,
    effect: 'annotate',
    reason: `${item.reason}; the local loop or recurrence bound is kept`,
  };
}

function hasStructural(time: ComplexityExpression): boolean {
  return time.kind === 'var' || time.kind === 'log' || time.kind === 'pow'
    || time.kind === 'mul' || time.kind === 'add'
    || time.kind === 'factorial' || time.kind === 'binomial';
}

function title(id: string): string {
  switch (id) {
    case 'branching-recursion':
      return 'Branching recursion';
    case 'linear-recurrence':
      return 'Linear recursion';
    case 'divide-and-conquer':
      return 'Divide and conquer';
    default:
      return id;
  }
}

function unique(
  item: RecognizedPattern,
  index: number,
  all: RecognizedPattern[],
): boolean {
  return all.findIndex((p) => sameHit(p, item)) === index;
}

function sameHit(a: RecognizedPattern, b: RecognizedPattern): boolean {
  return a.id === b.id
    && a.range?.startLine === b.range?.startLine
    && a.range?.startCharacter === b.range?.startCharacter;
}
