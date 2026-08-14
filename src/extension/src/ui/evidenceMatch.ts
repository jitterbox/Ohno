import type {
  EvidenceNode,
  FunctionComplexity,
  LineRange,
} from '../analysis/types';
import { rangeContains, rangeSize, rangesIntersect } from './ranges';

export interface EvidenceHit {
  id: string;
  node: EvidenceNode;
  path: EvidenceNode[];
}

export interface Caret {
  line: number;
  character: number;
}

export function pickFunction(
  functions: FunctionComplexity[],
  selection: LineRange,
  active: Caret,
): FunctionComplexity | undefined {
  const overlapping = functions.filter((fn) =>
    rangesIntersect(fn.range, selection));
  if (overlapping.length === 0) return undefined;
  return overlapping.find((fn) =>
    rangeContains(fn.range, active.line, active.character))
    ?? overlapping[0];
}

export function withFunctionRange(fn: FunctionComplexity): EvidenceNode {
  if (fn.evidence.range) return fn.evidence;
  return { ...fn.evidence, range: fn.range };
}

export function matchEvidence(
  root: EvidenceNode,
  selection: LineRange,
): EvidenceHit[] {
  const hits: EvidenceHit[] = [];
  collect(root, 'root', [], { selection, hits });
  return hits;
}

export function primaryHit(hits: EvidenceHit[]): EvidenceHit | undefined {
  if (hits.length === 0) return undefined;
  return [...hits].sort((a, b) => {
    const aSize = a.node.range
      ? rangeSize(a.node.range)
      : Number.MAX_SAFE_INTEGER;
    const bSize = b.node.range
      ? rangeSize(b.node.range)
      : Number.MAX_SAFE_INTEGER;
    return aSize - bSize;
  })[0];
}

function collect(
  node: EvidenceNode,
  id: string,
  path: EvidenceNode[],
  state: { selection: LineRange; hits: EvidenceHit[] },
): void {
  const next = [...path, node];
  if (node.range && rangesIntersect(node.range, state.selection)) {
    state.hits.push({ id, node, path: next });
  }
  node.children.forEach((child, index) => {
    collect(child, `${id}.${index}`, next, state);
  });
}
