import type { AnalyzeDocumentRequest } from '../analyzer';
import type {
  AnalyzeResponse,
  AnalysisTier,
  FunctionComplexity,
  FunctionKind,
  LineRange,
} from '../types';
import {
  format,
  formatBigO,
  formatExplanation,
  prune,
  simplify,
  type ComposedCost,
} from './engine';
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
      analyzeOne(fn, checker, loaded.source, request.tier)),
    warnings,
  };
}

function analyzeOne(
  fn: CollectedFunction,
  checker: ts.TypeChecker,
  source: ts.SourceFile,
  tier: AnalysisTier,
): FunctionComplexity {
  const ctx = createContext(checker, source);
  const cost = fn.body
    ? walkNode(ctx, fn.body)
    : walkNode(ctx, fn.node);
  return toDto(fn.name, fn.kind, fn.range, fn.signatureRange, cost, ctx, tier);
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
  const cost = walkList(ctx, nodes, selection);
  const name = enclosing
    ? `${enclosing.name} (selection)`
    : 'selection';
  return toDto(
    name,
    enclosing?.kind ?? 'method',
    selection,
    selection,
    cost,
    ctx,
    request.tier,
  );
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

function toDto(
  name: string,
  kind: FunctionKind,
  range: LineRange,
  signatureRange: LineRange,
  cost: ComposedCost,
  ctx: ReturnType<typeof createContext>,
  tier: AnalysisTier,
): FunctionComplexity {
  const time = simplify(cost.time);
  const space = simplify(cost.space);
  const confidence = cost.confidence === 'high' && ctx.reasons.length > 0
    ? 'medium'
    : cost.confidence;
  const evidence = prune(cost.evidence);
  return {
    id: `${name}:${range.startLine}`,
    name,
    kind,
    range,
    signatureRange,
    time: formatBigO(time),
    space: formatBigO(space),
    confidence,
    dimensions: ctx.sizes.dims,
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
    warnings: [...cost.warnings],
    boundingSuggestions: [],
    explanation: formatExplanation(time, []),
    patterns: [],
    confidenceReasons: ctx.reasons,
    approaches: [{
      id: 'dominant',
      name: 'Structural',
      summary: formatBigO(time),
      role: 'dominant',
      timeHint: formatBigO(time),
    }],
    selectionHint: '',
    tier,
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
