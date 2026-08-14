import type {
  AnalyzeResponse,
  EvidenceNode,
  FunctionComplexity,
} from '../analysis/types';

export type DeepRunStatus =
  | 'running'
  | 'unchanged'
  | 'changed'
  | 'failed';

export interface DeepChange {
  label: string;
  detail?: string;
}

export interface DeepRun {
  functionId: string;
  status: DeepRunStatus;
  summary: string;
  changes: DeepChange[];
}

export function runningDeepRun(functionId: string): DeepRun {
  return {
    functionId,
    status: 'running',
    summary: 'Deep analysis running…',
    changes: [],
  };
}

export function failedDeepRun(
  functionId: string,
  message: string,
): DeepRun {
  return {
    functionId,
    status: 'failed',
    summary: 'Deep analysis failed',
    changes: [{ label: message }],
  };
}

export function diffResponses(
  before: AnalyzeResponse | undefined,
  after: AnalyzeResponse,
): DeepRun[] {
  const prior = before?.functions ?? [];
  const fileNotes = (after.warnings ?? [])
    .map((w) => w.message)
    .filter((msg) => !(before?.warnings ?? []).some((w) => w.message === msg));
  return after.functions.map((fn) => {
    const run = diffFunction(findPrior(prior, fn), fn);
    if (fileNotes.length === 0) return run;
    return {
      ...run,
      status: 'changed' as const,
      summary: run.status === 'unchanged'
        ? fileNotes[0]
        : run.summary,
      changes: [
        ...run.changes,
        ...fileNotes.map((label) => ({ label })),
      ],
    };
  });
}

export function diffFunction(
  before: FunctionComplexity | undefined,
  after: FunctionComplexity,
): DeepRun {
  if (!before) {
    return {
      functionId: after.id,
      status: 'changed',
      summary: `${after.time} · ${after.space}`,
      changes: [{
        label: 'Deep result',
        detail: `${after.time} · ${after.space}`,
      }],
    };
  }

  const changes = [
    ...scalarChanges(before, after),
    ...evidenceChanges(before.evidence, after.evidence),
    ...listChanges(
      before.warnings.map((w) => w.message),
      after.warnings.map((w) => w.message),
      'warning',
    ),
  ];
  if (changes.length === 0) {
    return {
      functionId: after.id,
      status: 'unchanged',
      summary: 'Nothing additional found',
      changes: [],
    };
  }
  return {
    functionId: after.id,
    status: 'changed',
    summary: changes.length === 1
      ? changes[0].label
      : `Updated ${changes.length} findings`,
    changes,
  };
}

function findPrior(
  prior: FunctionComplexity[],
  after: FunctionComplexity,
): FunctionComplexity | undefined {
  return prior.find((fn) => fn.id === after.id)
    ?? prior.find((fn) => fn.name === after.name);
}

function scalarChanges(
  before: FunctionComplexity,
  after: FunctionComplexity,
): DeepChange[] {
  const fields: Array<keyof Pick<
    FunctionComplexity, 'time' | 'space' | 'confidence'
  >> = ['time', 'space', 'confidence'];
  return fields
    .filter((field) => before[field] !== after[field])
    .map((field) => ({
      label: `${title(field)}: ${before[field]} → ${after[field]}`,
    }));
}

function evidenceChanges(
  before: EvidenceNode,
  after: EvidenceNode,
): DeepChange[] {
  const prev = evidenceMap(before);
  const next = evidenceMap(after);
  const changes: DeepChange[] = [];
  for (const [key, cost] of next) {
    const old = prev.get(key);
    if (old === undefined) {
      changes.push({ label: `Added ${key}: ${cost}` });
    } else if (old !== cost) {
      changes.push({ label: `${key}: ${old} → ${cost}` });
    }
  }
  for (const key of prev.keys()) {
    if (!next.has(key)) changes.push({ label: `Removed ${key}` });
  }
  return changes;
}

function listChanges(
  before: string[],
  after: string[],
  kind: string,
): DeepChange[] {
  const added = after.filter((item) => !before.includes(item));
  const removed = before.filter((item) => !after.includes(item));
  return [
    ...added.map((label) => ({ label: `Added ${kind}: ${label}` })),
    ...removed.map((label) => ({ label: `Removed ${kind}: ${label}` })),
  ];
}

function evidenceMap(
  node: EvidenceNode,
  path = '',
): Map<string, string> {
  const key = path ? `${path} / ${node.label}` : node.label;
  const map = new Map<string, string>([[key, node.cost]]);
  for (const child of node.children) {
    for (const [k, v] of evidenceMap(child, key)) map.set(k, v);
  }
  return map;
}

function title(field: string): string {
  return field.charAt(0).toUpperCase() + field.slice(1);
}
