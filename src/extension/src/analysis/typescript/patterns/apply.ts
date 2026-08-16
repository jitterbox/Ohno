import {
  mul,
  unknown,
  type ComplexityExpression,
} from '../engine';
import type { RecognizedPattern } from '../engine';

const Opaque = new Set([
  'regex',
  'unbounded-worklist',
  'await-foreach',
  'unproven-loop',
]);

export function applyTime(
  time: ComplexityExpression,
  patterns: readonly RecognizedPattern[],
): ComplexityExpression {
  const opaque = patterns.find((p) => Opaque.has(p.id));
  if (opaque) return unknown(opaque.reason);
  const wipe = patterns.find((p) => p.effect === 'unknown');
  if (wipe && time.kind === 'const') {
    return unknown(wipe.reason);
  }
  if (patterns.some((p) => p.id === 'string-concat-loop')
    && time.kind === 'var') {
    return mul(time, time);
  }
  return time;
}

export function applySpace(
  space: ComplexityExpression,
  patterns: readonly RecognizedPattern[],
): ComplexityExpression {
  if (patterns.some((p) => p.id === 'unbounded-worklist')) {
    return unknown('worklist');
  }
  return space;
}

export function isOpaqueId(id: string): boolean {
  return Opaque.has(id);
}
