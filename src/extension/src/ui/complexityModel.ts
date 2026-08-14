import type {
  EvidenceNode,
  FunctionComplexity,
  LineRange,
} from '../analysis/types';
import type { DeepRun } from './deepDiff';
import { withFunctionRange } from './evidenceMatch';

export type ItemKind =
  | 'summary'
  | 'group'
  | 'dimension'
  | 'evidence'
  | 'warning'
  | 'bound'
  | 'deep';

export interface ComplexityItem {
  id: string;
  kind: ItemKind;
  label: string;
  description?: string;
  tooltip?: string;
  icon: string;
  range?: LineRange;
  uri?: string;
  children: ComplexityItem[];
  highlighted: boolean;
  italic?: boolean;
}

export interface PanelModel {
  summary: ComplexityItem[];
  derivation: ComplexityItem[];
}

export function buildPanelModel(
  fn: FunctionComplexity,
  uri: string,
  highlighted: ReadonlySet<string>,
  deepRun?: DeepRun,
): PanelModel {
  return {
    summary: buildSummary(fn, uri, deepRun),
    derivation: [
      toEvidenceItem(withFunctionRange(fn), 'root', uri, highlighted),
    ],
  };
}

function buildSummary(
  fn: FunctionComplexity,
  uri: string,
  deepRun?: DeepRun,
): ComplexityItem[] {
  return [
    ...deepGroup(deepRun),
    ...headlineItems(fn),
    ...dimensionGroup(fn),
    ...warningGroup(fn, uri),
    ...boundGroup(fn),
  ];
}

function deepGroup(run?: DeepRun): ComplexityItem[] {
  if (!run) return [];
  const icon = deepIcon(run.status);
  if (run.changes.length === 0) {
    return [leaf({
      id: 'deep',
      kind: 'deep',
      label: run.summary,
      icon,
      description: 'deep analysis',
      tooltip: run.summary,
    })];
  }
  return [group(
    'deep',
    run.summary,
    icon,
    run.changes.map((change, i) => leaf({
      id: `deep:${i}`,
      kind: 'deep',
      label: change.label,
      icon: run.status === 'failed' ? 'error' : 'zap',
      description: change.detail,
      tooltip: change.detail ?? change.label,
    })),
  )];
}

function deepIcon(status: DeepRun['status']): string {
  switch (status) {
    case 'running':
      return 'loading~spin';
    case 'unchanged':
      return 'pass';
    case 'failed':
      return 'error';
    default:
      return 'zap';
  }
}

function headlineItems(fn: FunctionComplexity): ComplexityItem[] {
  return [
    leaf({
      id: 'name',
      kind: 'summary',
      label: fn.name,
      icon: kindIcon(fn.kind),
      description: fn.kind,
    }),
    leaf({
      id: 'analysis',
      kind: 'summary',
      label: `${fn.time} · ${fn.space}`,
      icon: 'dashboard',
    }),
    ...explanationItems(fn),
    ...patternItems(fn),
    ...confidenceItems(fn),
  ];
}

function confidenceItems(fn: FunctionComplexity): ComplexityItem[] {
  const icon = confidenceIcon(fn.confidence);
  const label = `Confidence: ${fn.confidence}`;
  if (!fn.confidenceReasons?.length) {
    return [leaf({
      id: 'confidence',
      kind: 'summary',
      label,
      icon,
      description: `${fn.tier} tier`,
    })];
  }

  return [group(
    'confidence',
    label,
    icon,
    fn.confidenceReasons.map((reason, i) => leaf({
      id: `confidence:${i}`,
      kind: 'summary',
      label: reason,
      icon: 'info',
      tooltip: reason,
      italic: true,
    })),
  )];
}

function explanationItems(fn: FunctionComplexity): ComplexityItem[] {
  if (!fn.explanation) return [];
  return [leaf({
    id: 'explanation',
    kind: 'summary',
    label: fn.explanation,
    icon: 'comment',
    tooltip: fn.explanation,
    italic: true,
  })];
}

function patternItems(fn: FunctionComplexity): ComplexityItem[] {
  if (!fn.patterns?.length) return [];
  return [group(
    'patterns',
    'Recognized patterns',
    'tag',
    fn.patterns.map((p) => leaf({
      id: `pattern:${p.id}`,
      kind: 'summary',
      label: p.label,
      icon: 'tag',
      description: p.reason,
      tooltip: `${p.label}: ${p.reason}`,
    })),
  )];
}

function dimensionGroup(fn: FunctionComplexity): ComplexityItem[] {
  if (!fn.dimensions.length) return [];
  return [group(
    'dims',
    'Dimensions',
    'symbol-ruler',
    fn.dimensions.map((d) => leaf({
      id: `dim:${d.variable}`,
      kind: 'dimension',
      label: d.variable,
      icon: 'symbol-variable',
      description: d.meaning,
      tooltip: `${d.variable} = ${d.meaning}`,
    })),
  )];
}

function warningGroup(
  fn: FunctionComplexity,
  uri: string,
): ComplexityItem[] {
  if (!fn.warnings.length) return [];
  return [group(
    'warnings',
    'Why this is an estimate',
    'warning',
    fn.warnings.map((w, i) => leaf({
      id: `warn:${i}`,
      kind: 'warning',
      label: w.message,
      icon: 'info',
      tooltip: w.message,
      range: w.range,
      uri,
    })),
  )];
}

function boundGroup(fn: FunctionComplexity): ComplexityItem[] {
  if (!fn.boundingSuggestions.length) return [];
  return [group(
    'bounds',
    'Bounding opportunities',
    'lightbulb',
    fn.boundingSuggestions.map((s, i) => leaf({
      id: `bound:${i}`,
      kind: 'bound',
      label: s.description,
      icon: 'lightbulb',
      description: `${s.resultingTime} / ${s.resultingSpace}`,
      tooltip: `${s.condition} → ${s.resultingTime}`,
    })),
  )];
}

function toEvidenceItem(
  node: EvidenceNode,
  id: string,
  uri: string,
  highlighted: ReadonlySet<string>,
): ComplexityItem {
  return {
    id,
    kind: 'evidence',
    label: `${node.label}: ${node.cost}`,
    icon: evidenceIcon(node.kind),
    range: node.range,
    uri,
    highlighted: highlighted.has(id),
    children: node.children.map((child, index) =>
      toEvidenceItem(child, `${id}.${index}`, uri, highlighted)),
  };
}

function group(
  id: string,
  label: string,
  icon: string,
  children: ComplexityItem[],
): ComplexityItem {
  return {
    id,
    kind: 'group',
    label,
    icon,
    children,
    highlighted: false,
  };
}

function leaf(spec: {
  id: string;
  kind: ItemKind;
  label: string;
  icon: string;
  description?: string;
  tooltip?: string;
  range?: LineRange;
  uri?: string;
  italic?: boolean;
}): ComplexityItem {
  return { ...spec, children: [], highlighted: false };
}

function kindIcon(kind: FunctionComplexity['kind']): string {
  switch (kind) {
    case 'constructor':
      return 'symbol-constructor';
    case 'localFunction':
    case 'lambda':
      return 'symbol-method';
    case 'property':
      return 'symbol-property';
    case 'operator':
      return 'symbol-operator';
    default:
      return 'symbol-method';
  }
}

function confidenceIcon(confidence: string): string {
  switch (confidence) {
    case 'high':
      return 'pass';
    case 'medium':
      return 'warning';
    case 'low':
      return 'error';
    default:
      return 'circle-slash';
  }
}

function evidenceIcon(kind: string): string {
  switch (kind) {
    case 'loop':
      return 'sync';
    case 'conditional':
      return 'question';
    case 'call':
      return 'symbol-method';
    case 'linq':
      return 'filter';
    case 'recursion':
      return 'refresh';
    case 'allocation':
    case 'alloc':
      return 'symbol-variable';
    default:
      return 'list-tree';
  }
}
