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
  prune,
  simplify,
  type ComposedCost,
  type RecognizedPattern,
} from './engine';
import { applySpace, applyTime } from './patterns/apply';
import { summarize } from './patterns/approaches';
import { detectBounds } from './patterns/bounds';
import { recognize } from './patterns/recognize';
import { tryRecurrence } from './patterns/recurrence';
import { refine } from './patterns/refine';
import { getProgram } from './program';
import { createContext, walkList, walkNode } from './walk/body';
import {
  collectFunctions,
  overlaps,
  rangeOf,
  type CollectedFunction,
} from './walk/functions';
import ts from 'typescript';

export function analyzeDocument(
  request: AnalyzeDocumentRequest,
): AnalyzeResponse {
  const loaded = getProgram(request.uri, request.text, true);
  const checker = loaded.program.getTypeChecker();
  const collected = collectFunctions(loaded.source);
  const warnings = fallbackWarnings(request.tier, loaded.fallback);
  if (request.selection) {
    const fn = analyzeSelection(
      request, collected, checker, loaded.source,
    );
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
    functions: collected.map((fn) =>
      analyzeOne(fn, checker, loaded.source, request.tier, false)),
    warnings,
  };
}

function analyzeOne(
  fn: CollectedFunction,
  checker: ts.TypeChecker,
  source: ts.SourceFile,
  tier: AnalysisTier,
  selection: boolean,
): FunctionComplexity {
  const ctx = createContext(checker, source);
  const root = fn.body ?? fn.node;
  const bounds = detectBounds(root, ctx.sizes);
  ctx.sizes.heaps = bounds.heaps;
  ctx.worklists = bounds.worklists;
  ctx.reasons.push(...bounds.reasons);
  const raw = recognize(fn.node, source, checker);
  const rec = tryRecurrence(fn.name, fn.body);
  const walked = rec?.cost ?? walkNode(ctx, root);
  const patterns = refine(raw, walked.time, rec?.id);
  return toDto({
    name: fn.name,
    kind: fn.kind,
    range: fn.range,
    signatureRange: fn.signatureRange,
    cost: walked,
    patterns,
    reasons: ctx.reasons,
    dims: ctx.sizes.dims,
    tier,
    selection,
  });
}

function analyzeSelection(
  request: AnalyzeDocumentRequest,
  collected: CollectedFunction[],
  checker: ts.TypeChecker,
  source: ts.SourceFile,
): FunctionComplexity | undefined {
  const selection = request.selection!;
  const enclosing = collected.find((fn) => overlaps(fn.range, selection));
  const ctx = createContext(checker, source);
  const nodes = overlappingStatements(source, selection);
  const root = enclosing?.body ?? source;
  const bounds = detectBounds(root, ctx.sizes);
  ctx.sizes.heaps = bounds.heaps;
  ctx.worklists = bounds.worklists;
  ctx.reasons.push(...bounds.reasons);
  const cost = walkList(ctx, nodes, selection);
  const raw = recognize(root, source, checker);
  const patterns = refine(raw, cost.time);
  return toDto({
    name: enclosing ? `${enclosing.name} (selection)` : 'selection',
    kind: enclosing?.kind ?? 'method',
    range: selection,
    signatureRange: selection,
    cost,
    patterns,
    reasons: ctx.reasons,
    dims: ctx.sizes.dims,
    tier: request.tier,
    selection: true,
  });
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

function fallbackWarnings(
  tier: AnalysisTier,
  fallback: boolean,
): { message: string }[] {
  if (tier === 'deep' && fallback) {
    return [{ message: 'Fell back to an ad-hoc TypeScript program.' }];
  }
  return [];
}
