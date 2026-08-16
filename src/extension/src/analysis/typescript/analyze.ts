import type { AnalyzeDocumentRequest } from '../analyzer';
import type {
  AnalyzeResponse,
  AnalysisTier,
  FunctionComplexity,
  FunctionKind,
  LineRange,
  RecognizedPattern as ProtocolPattern,
} from '../types';
import {
  format,
  formatBigO,
  formatExplanation,
  peak,
  prune,
  simplify,
  type ComplexityExpression,
  type ComposedCost,
  type RecognizedPattern,
} from './engine';
import { applySpace, applyTime } from './patterns/apply';
import { summarize } from './patterns/approaches';
import {
  emptyBoundMaps,
  noteBounds,
  type BoundMaps,
} from './patterns/bounds';
import { recognize } from './patterns/recognize';
import { tryRecurrence } from './patterns/recurrence';
import { refine } from './patterns/refine';
import { getProgram } from './program';
import { createContext, walkList, walkNode } from './walk/body';
import { noteLoopIndex } from './walk/cardinality';
import {
  collectFunctions,
  overlaps,
  rangeOf,
  type CollectedFunction,
} from './walk/functions';
import { noteUnreachable } from './walk/reachability';
import ts from 'typescript';

export function analyzeDocument(
  request: AnalyzeDocumentRequest,
  abort?: () => boolean,
): AnalyzeResponse {
  const loaded = getProgram(
    request.uri, request.text, request.tier === 'deep',
  );
  const checker = loaded.program.getTypeChecker();
  const collected = collectFunctions(loaded.source);
  const warnings = fallbackWarnings(request.tier, loaded.fallback);
  const host = {
    checker,
    source: loaded.source,
    program: loaded.program,
  };
  if (request.selection) {
    if (abort?.()) throw new Error('cancelled');
    const fn = analyzeSelection(request, collected, host);
    return {
      uri: request.uri,
      version: request.version,
      functions: fn ? [fn] : [],
      warnings,
    };
  }
  return {
    uri: request.uri,
    version: request.version,
    functions: analyzeAll(collected, host, request.tier, abort),
    warnings,
  };
}

interface AnalyzeHost {
  checker: ts.TypeChecker;
  source: ts.SourceFile;
  program: ts.Program;
}

function analyzeAll(
  collected: CollectedFunction[],
  host: AnalyzeHost,
  tier: AnalysisTier,
  abort?: () => boolean,
): FunctionComplexity[] {
  const functions: FunctionComplexity[] = [];
  for (const fn of collected) {
    if (abort?.()) throw new Error('cancelled');
    functions.push(analyzeOne(fn, host, tier, false));
  }
  return functions;
}

function analyzeOne(
  fn: CollectedFunction,
  host: AnalyzeHost,
  tier: AnalysisTier,
  selection: boolean,
): FunctionComplexity {
  const ctx = primedContext(host, fn.body ?? fn.node);
  const raw = recognize(fn.node, host.source, host.checker);
  const rec = tryRecurrence(fn.name, fn.body);
  const walked = walkNode(ctx, fn.body ?? fn.node);
  const cost = withRecurrence(walked, rec);
  const patterns = refine(raw, cost.time, rec?.id);
  return toDto({
    name: fn.name,
    kind: fn.kind,
    range: fn.range,
    signatureRange: fn.signatureRange,
    cost: withAllocs(cost, ctx.allocs),
    patterns,
    reasons: ctx.reasons,
    dims: ctx.sizes.dims,
    tier,
    selection,
  });
}

function withRecurrence(
  walked: ComposedCost,
  rec: ReturnType<typeof tryRecurrence>,
): ComposedCost {
  if (!rec) return walked;
  return {
    ...walked,
    time: rec.cost.time,
    space: peak([walked.space, rec.cost.space]),
  };
}

function analyzeSelection(
  request: AnalyzeDocumentRequest,
  collected: CollectedFunction[],
  host: AnalyzeHost,
): FunctionComplexity | undefined {
  const selection = request.selection!;
  const enclosing = collected.find((fn) => overlaps(fn.range, selection));
  const root = enclosing?.body ?? host.source;
  const ctx = primedContext(host, root);
  const nodes = overlappingStatements(host.source, selection);
  const cost = walkList(ctx, nodes, selection);
  const raw = recognize(root, host.source, host.checker);
  const patterns = refine(raw, cost.time);
  return toDto({
    name: enclosing ? `${enclosing.name} (selection)` : 'selection',
    kind: enclosing?.kind ?? 'method',
    range: selection,
    signatureRange: selection,
    cost: withAllocs(cost, ctx.allocs),
    patterns,
    reasons: ctx.reasons,
    dims: ctx.sizes.dims,
    tier: request.tier,
    selection: true,
  });
}

function primedContext(
  host: AnalyzeHost,
  root: ts.Node,
) {
  const ctx = createContext(host.checker, host.source, host.program);
  const bounds = emptyBoundMaps();
  const visit = (node: ts.Node): void => {
    noteLoopIndex(node, ctx.sizes.loopIndices);
    noteUnreachable(node, ctx.unreachable);
    noteBounds(node, ctx.sizes, bounds);
    ts.forEachChild(node, visit);
  };
  visit(root);
  applyBounds(ctx, bounds);
  return ctx;
}

function applyBounds(
  ctx: ReturnType<typeof createContext>,
  bounds: BoundMaps,
): void {
  ctx.sizes.heaps = bounds.heaps;
  ctx.worklists = bounds.worklists;
  ctx.worklistKind = bounds.worklistKind;
  ctx.reasons.push(...bounds.reasons);
  for (const bound of bounds.heaps.values()) ctx.allocs.push(bound);
}

function overlappingStatements(
  source: ts.SourceFile,
  selection: LineRange,
): ts.Node[] {
  const nodes: ts.Node[] = [];
  const visit = (node: ts.Node): void => {
    const range = rangeOf(node, source);
    if (!overlaps(range, selection)) return;
    if (ts.isStatement(node) && !ts.isBlock(node)
      && !ts.isSourceFile(node)) {
      nodes.push(node);
      return;
    }
    ts.forEachChild(node, visit);
  };
  visit(source);
  return nodes;
}

interface DtoInput {
  name: string;
  kind: FunctionKind;
  range: LineRange;
  signatureRange: LineRange;
  cost: ComposedCost;
  patterns: RecognizedPattern[];
  reasons: string[];
  dims: { variable: string; meaning: string }[];
  tier: AnalysisTier;
  selection: boolean;
}

function toDto(input: DtoInput): FunctionComplexity {
  const time = applyTime(simplify(input.cost.time), input.patterns);
  const space = applySpace(simplify(input.cost.space), input.patterns);
  const evidence = prune(input.cost.evidence);
  const confidence = confidenceOf(time, input);
  const { approaches, hint } = summarize(
    input.patterns, evidence, time, input.selection,
  );
  return {
    id: `${input.name}:${input.range.startLine}`,
    name: input.name,
    kind: input.kind,
    range: input.range,
    signatureRange: input.signatureRange,
    time: formatBigO(time),
    space: formatBigO(space),
    confidence,
    dimensions: input.dims,
    evidence: {
      kind: evidence.kind,
      label: evidence.label,
      cost: format(evidence.cost),
      range: evidence.span,
      children: evidence.children.map((child) => ({
        kind: child.kind,
        label: child.label,
        cost: format(child.cost),
        range: child.span,
        children: [],
      })),
    },
    warnings: [...input.cost.warnings],
    boundingSuggestions: [],
    explanation: formatExplanation(time, input.patterns),
    patterns: input.patterns.map(toProtocolPattern),
    confidenceReasons: input.reasons,
    approaches,
    selectionHint: hint,
    tier: input.tier,
  };
}

function confidenceOf(
  time: ReturnType<typeof simplify>,
  input: DtoInput,
): FunctionComplexity['confidence'] {
  if (time.kind === 'unknown') return 'unknown';
  if (input.patterns.some((p) => p.effect === 'unknown')) return 'low';
  if (input.cost.confidence === 'high' && input.reasons.length > 0) {
    return 'medium';
  }
  return input.cost.confidence;
}

function toProtocolPattern(item: RecognizedPattern): ProtocolPattern {
  return {
    id: item.id,
    label: item.label,
    reason: item.reason,
    effect: item.effect,
    range: item.range,
  };
}

function withAllocs(
  cost: ComposedCost,
  allocs: ComplexityExpression[],
): ComposedCost {
  if (allocs.length === 0) return cost;
  return {
    ...cost,
    space: simplify(peak([cost.space, ...allocs])),
  };
}

function fallbackWarnings(
  tier: AnalysisTier,
  fallback: boolean,
): { message: string }[] {
  if (tier === 'deep' && fallback) {
    return [{ message: 'Fell back to an ad-hoc TypeScript program.' }];
  }
  return [];
}
