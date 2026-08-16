import type { LineSpan, PatternEffect, RecognizedPattern } from '../engine';

export function pattern(
  id: string,
  label: string,
  reason: string,
  effect: PatternEffect,
  range?: LineSpan,
  rangeExplanation?: string,
): RecognizedPattern {
  return { id, label, reason, effect, range, rangeExplanation };
}

export function unknownPattern(
  id: string,
  label: string,
  reason: string,
  range?: LineSpan,
): RecognizedPattern {
  return pattern(id, label, reason, 'unknown', range);
}

export function annotatePattern(
  id: string,
  label: string,
  reason: string,
  range?: LineSpan,
): RecognizedPattern {
  return pattern(id, label, reason, 'annotate', range);
}
