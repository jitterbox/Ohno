import type { ComplexityExpression } from './expression';

export type AnalysisConfidence = 'unknown' | 'low' | 'medium' | 'high';

export const ConfidenceRank: Record<AnalysisConfidence, number> = {
  unknown: 0,
  low: 1,
  medium: 2,
  high: 3,
};

export function minConfidence(
  a: AnalysisConfidence,
  b: AnalysisConfidence,
): AnalysisConfidence {
  return ConfidenceRank[a] < ConfidenceRank[b] ? a : b;
}

export interface LineSpan {
  startLine: number;
  startCharacter: number;
  endLine: number;
  endCharacter: number;
}

export interface InputDimension {
  variable: string;
  meaning: string;
}

export interface ComplexityEvidence {
  kind: string;
  label: string;
  cost: ComplexityExpression;
  span?: LineSpan;
  children: readonly ComplexityEvidence[];
}

export function evidenceLeaf(
  kind: string,
  label: string,
  cost: ComplexityExpression,
  span?: LineSpan,
): ComplexityEvidence {
  return { kind, label, cost, span, children: [] };
}

export interface AnalysisWarning {
  message: string;
  span?: LineSpan;
}

export interface BoundingSuggestion {
  description: string;
  condition: string;
  resultingTime: ComplexityExpression;
  resultingSpace: ComplexityExpression;
}

export type PatternEffect = 'annotate' | 'unknown' | 'range';

export interface RecognizedPattern {
  id: string;
  label: string;
  reason: string;
  effect: PatternEffect;
  rangeExplanation?: string;
  range?: LineSpan;
}

export interface AlgorithmApproach {
  id: string;
  name: string;
  summary: string;
  role: string;
  timeHint?: string;
}

export interface ComplexityResult {
  time: ComplexityExpression;
  space: ComplexityExpression;
  confidence: AnalysisConfidence;
  dimensions: readonly InputDimension[];
  evidence: ComplexityEvidence;
  warnings: readonly AnalysisWarning[];
  boundingSuggestions: readonly BoundingSuggestion[];
  patterns: readonly RecognizedPattern[];
  explanation: string;
  confidenceReasons: readonly string[];
  approaches: readonly AlgorithmApproach[];
  selectionHint: string;
}
