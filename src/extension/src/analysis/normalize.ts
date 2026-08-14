import type {
  AnalyzeResponse,
  BoundingSuggestion,
  EvidenceNode,
  FunctionComplexity,
  InputDimension,
  LineRange,
  AnalysisWarning,
} from './types';

export function normalizeAnalyzeResponse(raw: unknown): AnalyzeResponse {
  const r = asRecord(raw);
  return {
    uri: str(r, 'uri', 'Uri'),
    version: num(r, 'version', 'Version'),
    functions: asArray(pick(r, 'functions', 'Functions')).map(normalizeFn),
    warnings: asArray(pick(r, 'warnings', 'Warnings')).map(normalizeWarning),
  };
}

function normalizeFn(raw: unknown): FunctionComplexity {
  const r = asRecord(raw);
  return {
    id: str(r, 'id', 'Id'),
    name: str(r, 'name', 'Name'),
    kind: (str(r, 'kind', 'Kind') || 'method') as FunctionComplexity['kind'],
    range: normalizeRange(pick(r, 'range', 'Range')),
    signatureRange: normalizeRange(
      pick(r, 'signatureRange', 'SignatureRange'),
    ),
    time: str(r, 'time', 'Time') || 'O(unknown)',
    space: str(r, 'space', 'Space') || 'O(unknown)',
    confidence: (str(r, 'confidence', 'Confidence') || 'unknown')
      .toLowerCase() as FunctionComplexity['confidence'],
    dimensions: asArray(pick(r, 'dimensions', 'Dimensions')).map(normalizeDim),
    evidence: normalizeEvidence(pick(r, 'evidence', 'Evidence')),
    warnings: asArray(pick(r, 'warnings', 'Warnings')).map(normalizeWarning),
    boundingSuggestions: asArray(
      pick(r, 'boundingSuggestions', 'BoundingSuggestions'),
    ).map(normalizeSuggestion),
    tier: (str(r, 'tier', 'Tier') || 'fast') as FunctionComplexity['tier'],
  };
}

function normalizeEvidence(raw: unknown): EvidenceNode {
  const r = asRecord(raw);
  return {
    kind: str(r, 'kind', 'Kind') || 'sequence',
    label: str(r, 'label', 'Label') || '',
    cost: str(r, 'cost', 'Cost') || '1',
    range: pick(r, 'range', 'Range')
      ? normalizeRange(pick(r, 'range', 'Range'))
      : undefined,
    children: asArray(pick(r, 'children', 'Children')).map(normalizeEvidence),
  };
}

function normalizeRange(raw: unknown): LineRange {
  const r = asRecord(raw);
  return {
    startLine: num(r, 'startLine', 'StartLine'),
    startCharacter: num(r, 'startCharacter', 'StartCharacter'),
    endLine: num(r, 'endLine', 'EndLine'),
    endCharacter: num(r, 'endCharacter', 'EndCharacter'),
  };
}

function normalizeDim(raw: unknown): InputDimension {
  const r = asRecord(raw);
  return {
    variable: str(r, 'variable', 'Variable'),
    meaning: str(r, 'meaning', 'Meaning'),
  };
}

function normalizeWarning(raw: unknown): AnalysisWarning {
  const r = asRecord(raw);
  return {
    message: str(r, 'message', 'Message'),
    range: pick(r, 'range', 'Range')
      ? normalizeRange(pick(r, 'range', 'Range'))
      : undefined,
  };
}

function normalizeSuggestion(raw: unknown): BoundingSuggestion {
  const r = asRecord(raw);
  return {
    description: str(r, 'description', 'Description'),
    condition: str(r, 'condition', 'Condition'),
    resultingTime: str(r, 'resultingTime', 'ResultingTime'),
    resultingSpace: str(r, 'resultingSpace', 'ResultingSpace'),
  };
}

function asRecord(value: unknown): Record<string, unknown> {
  return value && typeof value === 'object'
    ? value as Record<string, unknown>
    : {};
}

function asArray(value: unknown): unknown[] {
  return Array.isArray(value) ? value : [];
}

function pick(
  record: Record<string, unknown>,
  ...keys: string[]
): unknown {
  for (const key of keys) {
    if (record[key] !== undefined) return record[key];
  }
  return undefined;
}

function str(
  record: Record<string, unknown>,
  ...keys: string[]
): string {
  const value = pick(record, ...keys);
  return typeof value === 'string' ? value : '';
}

function num(
  record: Record<string, unknown>,
  ...keys: string[]
): number {
  const value = pick(record, ...keys);
  return typeof value === 'number' ? value : 0;
}
