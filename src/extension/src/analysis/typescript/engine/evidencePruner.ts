import type { ComplexityExpression } from './expression';
import { evidenceLeaf, type ComplexityEvidence, type LineSpan } from './types';

export function prune(evidence: ComplexityEvidence): ComplexityEvidence {
  const children = evidence.children
    .map(prune)
    .filter((child) => !isNoise(child));
  if (evidence.kind === 'sequence' && children.length === 1) {
    return children[0];
  }
  return { ...evidence, children };
}

export function sequenceEvidence(
  time: ComplexityExpression,
  span: LineSpan | undefined,
  parts: Iterable<ComplexityEvidence>,
): ComplexityEvidence {
  const children = [...parts]
    .map(prune)
    .filter((child) => !isNoise(child));
  if (children.length === 1) return children[0];
  if (children.length === 0) {
    return evidenceLeaf('sequence', 'empty', time, span);
  }
  return {
    kind: 'sequence',
    label: 'sequential statements',
    cost: time,
    span,
    children,
  };
}

export function meaningful(
  parts: Iterable<ComplexityEvidence>,
): ComplexityEvidence[] {
  return [...parts].map(prune).filter((child) => !isNoise(child));
}

export function isNoise(evidence: ComplexityEvidence): boolean {
  if (evidence.label === 'empty') return true;
  return evidence.kind === 'sequence'
    && evidence.children.length === 0
    && evidence.cost.kind === 'const'
    && evidence.cost.value === 1;
}
