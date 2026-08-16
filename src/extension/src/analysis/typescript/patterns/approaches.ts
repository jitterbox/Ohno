import {
  format,
  formatBigO,
  type AlgorithmApproach,
  type ComplexityEvidence,
  type ComplexityExpression,
  type RecognizedPattern,
} from '../engine';

const Algorithms = new Set([
  'graph-traversal',
  'branching-recursion',
  'linear-recurrence',
  'divide-and-conquer',
]);

export function summarize(
  patterns: readonly RecognizedPattern[],
  evidence: ComplexityEvidence,
  time: ComplexityExpression,
  selection: boolean,
): { approaches: AlgorithmApproach[]; hint: string } {
  const items = collect(patterns, evidence, time)
    .filter(unique)
    .slice(0, 3);
  const hint = items.length > 1
    ? selection
      ? 'This selection still combines more than one approach. '
        + 'Narrow the selection for a tighter per-algorithm bound.'
      : 'This function combines more than one approach. '
        + 'Select a smaller region for a tighter per-algorithm bound.'
    : '';
  return { approaches: items, hint };
}

function collect(
  patterns: readonly RecognizedPattern[],
  evidence: ComplexityEvidence,
  time: ComplexityExpression,
): AlgorithmApproach[] {
  const fromPatterns = patterns.flatMap((p) => fromPattern(p, time));
  if (fromPatterns.some((a) => Algorithms.has(a.id))) {
    return fromPatterns;
  }
  return [...fromPatterns, ...fromEvidence(evidence, patterns)];
}

function fromPattern(
  item: RecognizedPattern,
  time: ComplexityExpression,
): AlgorithmApproach[] {
  const gloss = formatBigO(time);
  if (item.id === 'data-dependent-recursion') {
    return branchApproaches();
  }
  if (Algorithms.has(item.id)) {
    return [one(item, 'dominant', gloss)];
  }
  return [one(item, roleOf(item), '')];
}

function branchApproaches(): AlgorithmApproach[] {
  return [
    {
      id: 'data-dependent-recursion',
      name: 'Data-dependent recursion',
      summary: 'The number of recursive calls depends on input values.',
      role: 'dominant',
    },
    {
      id: 'single-branch',
      name: 'Single-branch path',
      summary: 'If only one recursive call is taken, work is linear.',
      role: 'alternative',
      timeHint: 'O(n)',
    },
    {
      id: 'both-branches',
      name: 'Both branches taken',
      summary: 'If both recursive calls are taken at every step, '
        + 'work is exponential.',
      role: 'alternative',
      timeHint: 'O(2^n)',
    },
  ];
}

function fromEvidence(
  evidence: ComplexityEvidence,
  patterns: readonly RecognizedPattern[],
): AlgorithmApproach[] {
  if (patterns.some((p) => Algorithms.has(p.id))) return [];
  const kids = significant(evidence);
  if (kids.length < 2) return [];
  return kids.slice(0, 3).map((child, i) => ({
    id: `seq:${child.kind}:${i}`,
    name: child.label,
    summary: `This step contributes ${format(child.cost)}.`,
    role: 'sequential',
    timeHint: formatBigO(child.cost),
  }));
}

function significant(
  evidence: ComplexityEvidence,
): ComplexityEvidence[] {
  const root = evidence.kind === 'sequence' && evidence.children.length > 0
    ? evidence.children
    : [evidence];
  return root.filter((c) =>
    c.kind === 'loop' || c.kind === 'recursion' || c.kind === 'call');
}

function one(
  item: RecognizedPattern,
  role: string,
  hint: string,
): AlgorithmApproach {
  return {
    id: item.id,
    name: item.label,
    summary: item.reason,
    role,
    timeHint: hint || undefined,
  };
}

function roleOf(item: RecognizedPattern): string {
  if (item.effect === 'unknown') return 'dominant';
  if (item.id === 'await-opaque') return 'nested';
  return 'dominant';
}

function unique(
  item: AlgorithmApproach,
  index: number,
  all: AlgorithmApproach[],
): boolean {
  return all.findIndex((a) => a.id === item.id) === index;
}
