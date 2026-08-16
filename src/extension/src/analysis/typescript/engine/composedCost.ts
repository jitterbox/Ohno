import { One } from './cx';
import type { ComplexityExpression } from './expression';
import {
  evidenceLeaf,
  minConfidence,
  type AnalysisConfidence,
  type AnalysisWarning,
  type BoundingSuggestion,
  type ComplexityEvidence,
  type LineSpan,
} from './types';

export interface ComposedCost {
  time: ComplexityExpression;
  space: ComplexityExpression;
  confidence: AnalysisConfidence;
  evidence: ComplexityEvidence;
  warnings: readonly AnalysisWarning[];
  suggestions: readonly BoundingSuggestion[];
}

export function unitCost(
  kind: string,
  label: string,
  span?: LineSpan,
): ComposedCost {
  return {
    time: One,
    space: One,
    confidence: 'high',
    evidence: evidenceLeaf(kind, label, One, span),
    warnings: [],
    suggestions: [],
  };
}

export function ofCost(
  time: ComplexityExpression,
  space: ComplexityExpression,
  kind: string,
  label: string,
  span?: LineSpan,
  confidence: AnalysisConfidence = 'high',
): ComposedCost {
  return {
    time,
    space,
    confidence,
    evidence: evidenceLeaf(kind, label, time, span),
    warnings: [],
    suggestions: [],
  };
}

export { minConfidence };
