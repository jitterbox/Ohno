import * as ts from 'typescript';
import type * as vscode from 'vscode';
import type { IComplexityAnalyzer, AnalyzeDocumentRequest } from './analyzer';
import type {
  AnalyzeResponse,
  Confidence,
  EvidenceNode,
  FunctionComplexity,
  LineRange,
} from './types';

interface Cost {
  time: string;
  space: string;
  confidence: Confidence;
  evidence: EvidenceNode;
  warnings: string[];
}

/**
 * TypeScript analyzer. Fast tier walks the syntactic AST. Deep tier uses
 * a TypeChecker for call-target names (still local, no project graph).
 */
export class TypeScriptAnalyzer implements IComplexityAnalyzer {
  readonly languageIds = ['typescript'] as const;
  readonly supportsDeepAnalysis = true;

  analyze(
    request: AnalyzeDocumentRequest,
    token: vscode.CancellationToken,
  ): Promise<AnalyzeResponse> {
    if (token.isCancellationRequested) {
      return Promise.reject(new Error('cancelled'));
    }
    const source = ts.createSourceFile(
      request.uri,
      request.text,
      ts.ScriptTarget.Latest,
      true,
      ts.ScriptKind.TS,
    );
    const checker = request.tier === 'deep'
      ? createChecker(request.uri, request.text)
      : undefined;
    const functions = collectFunctions(source, checker);
    return Promise.resolve({
      uri: request.uri,
      version: request.version,
      functions: functions.map((fn) => ({ ...fn, tier: request.tier })),
      warnings: [],
    });
  }
}

function createChecker(
  fileName: string,
  text: string,
): ts.TypeChecker | undefined {
  const host: ts.CompilerHost = {
    ...ts.createCompilerHost({}),
    getSourceFile: (name) =>
      name === fileName
        ? ts.createSourceFile(name, text, ts.ScriptTarget.Latest, true)
        : undefined,
    fileExists: (name) => name === fileName,
    readFile: (name) => (name === fileName ? text : undefined),
  };
  const program = ts.createProgram([fileName], { noResolve: true }, host);
  return program.getTypeChecker();
}

function collectFunctions(
  source: ts.SourceFile,
  checker: ts.TypeChecker | undefined,
): FunctionComplexity[] {
  const results: FunctionComplexity[] = [];
  const visit = (node: ts.Node): void => {
    if (isFunctionLike(node) && node.body) {
      const name = functionName(node);
      const dims = inferDimensions(node);
      const cost = analyzeBody(node.body, dims, checker, 0);
      results.push({
        id: `${name}:${node.getStart()}`,
        name,
        kind: 'method',
        range: rangeOf(node, source),
        signatureRange: signatureOf(node, source),
        time: `O(${cost.time})`,
        space: `O(${cost.space})`,
        confidence: cost.confidence,
        dimensions: Object.entries(dims).map(([variable, meaning]) => ({
          variable,
          meaning,
        })),
        evidence: cost.evidence,
        warnings: cost.warnings.map((message) => ({ message })),
        boundingSuggestions: [],
        tier: 'fast',
      });
    }
    ts.forEachChild(node, visit);
  };
  visit(source);
  return results;
}

function isFunctionLike(node: ts.Node): node is ts.FunctionLikeDeclaration {
  return (
    ts.isFunctionDeclaration(node)
    || ts.isMethodDeclaration(node)
    || ts.isConstructorDeclaration(node)
    || ts.isFunctionExpression(node)
    || ts.isArrowFunction(node)
  );
}

function functionName(node: ts.FunctionLikeDeclaration): string {
  if (node.name) return node.name.getText();
  if (ts.isConstructorDeclaration(node)) return 'constructor';
  return '<anonymous>';
}

function inferDimensions(
  node: ts.FunctionLikeDeclaration,
): Record<string, string> {
  const letters = ['n', 'm', 'p', 'q'];
  const dims: Record<string, string> = {};
  let i = 0;
  for (const param of node.parameters) {
    const name = param.name.getText();
    const type = param.type?.getText() ?? '';
    if (/^k$/i.test(name) || /count|size|limit/i.test(name)) {
      dims.k = `parameter ${name}`;
      continue;
    }
    if (/\[\]|Array|ReadonlyArray|Set|Map/.test(type) && i < letters.length) {
      const letter = letters[i++];
      dims[letter] = `${name}.length`;
    }
  }
  return dims;
}

function analyzeBody(
  body: ts.Node,
  dims: Record<string, string>,
  checker: ts.TypeChecker | undefined,
  depth: number,
): Cost {
  let time = '1';
  let space = '1';
  let confidence: Confidence = 'high';
  const warnings: string[] = [];
  const children: EvidenceNode[] = [];

  const walk = (node: ts.Node): void => {
    if (isLoop(node)) {
      const bound = loopBound(node, dims);
      const inner = nodeHasBody(node)
        ? analyzeBody(node.body, dims, checker, depth + 1)
        : unitCost(node);
      const combined = mul(bound, inner.time);
      time = add(time, combined);
      space = maxSpace(space, inner.space);
      confidence = minConf(confidence, inner.confidence);
      children.push({
        kind: 'loop',
        label: `loop (${bound})`,
        cost: combined,
        range: undefined,
        children: [inner.evidence],
      });
      return;
    }
    if (ts.isCallExpression(node)) {
      const call = callCost(node, dims, checker);
      time = add(time, call.time);
      space = maxSpace(space, call.space);
      confidence = minConf(confidence, call.confidence);
      children.push(call.evidence);
      warnings.push(...call.warnings);
    }
    ts.forEachChild(node, walk);
  };
  walk(body);

  return {
    time: simplify(time),
    space: simplify(space),
    confidence,
    evidence: {
      kind: 'sequence',
      label: 'body',
      cost: simplify(time),
      children,
    },
    warnings,
  };
}

function isLoop(node: ts.Node): node is ts.ForStatement | ts.ForOfStatement
  | ts.ForInStatement | ts.WhileStatement | ts.DoStatement {
  return ts.isForStatement(node)
    || ts.isForOfStatement(node)
    || ts.isForInStatement(node)
    || ts.isWhileStatement(node)
    || ts.isDoStatement(node);
}

function nodeHasBody(
  node: ts.ForStatement | ts.ForOfStatement | ts.ForInStatement
    | ts.WhileStatement | ts.DoStatement,
): node is typeof node & { body: ts.Node } {
  return 'body' in node && !!node.body;
}

function loopBound(
  node: ts.IterationStatement,
  dims: Record<string, string>,
): string {
  if (ts.isForOfStatement(node) || ts.isForInStatement(node)) {
    const expr = node.expression.getText();
    const match = Object.entries(dims).find(([, meaning]) =>
      meaning.startsWith(expr.split('.')[0]),
    );
    return match?.[0] ?? 'n';
  }
  if (ts.isForStatement(node) && node.condition) {
    const text = node.condition.getText();
    if (/\*=\s*2|<<=/.test(node.incrementor?.getText() ?? '')) return 'log n';
    for (const letter of Object.keys(dims)) {
      if (text.includes(letter) || text.includes(dims[letter].split('.')[0])) {
        return letter;
      }
    }
  }
  return Object.keys(dims)[0] ?? 'n';
}

function callCost(
  node: ts.CallExpression,
  dims: Record<string, string>,
  checker: ts.TypeChecker | undefined,
): Cost {
  const name = callName(node, checker);
  const n = Object.keys(dims)[0] ?? 'n';
  if (name === 'sort' || name === 'toSorted') {
    return known(`n log n`, n, 'call', `${name} (n log n)`);
  }
  if (['indexOf', 'includes', 'find', 'filter', 'map', 'reduce']
    .includes(name)) {
    return known(n, '1', 'call', name);
  }
  if (name === 'has' || name === 'get' || name === 'set') {
    return known('1', '1', 'call', `${name} (expected)`);
  }
  return {
    time: `C(${name})`,
    space: '1',
    confidence: 'low',
    evidence: leaf('call', name, `C(${name})`),
    warnings: [`Unresolved call ${name}`],
  };
}

function callName(
  node: ts.CallExpression,
  checker: ts.TypeChecker | undefined,
): string {
  if (checker) {
    const symbol = checker.getSymbolAtLocation(node.expression);
    if (symbol) return symbol.getName();
  }
  if (ts.isIdentifier(node.expression)) return node.expression.text;
  if (ts.isPropertyAccessExpression(node.expression)) {
    return node.expression.name.text;
  }
  return 'unknown';
}

function known(
  time: string,
  space: string,
  kind: string,
  label: string,
): Cost {
  return {
    time,
    space,
    confidence: 'high',
    evidence: leaf(kind, label, time),
    warnings: [],
  };
}

function unitCost(node: ts.Node): Cost {
  return {
    time: '1',
    space: '1',
    confidence: 'high',
    evidence: leaf('stmt', node.kind.toString(), '1'),
    warnings: [],
  };
}

function leaf(kind: string, label: string, cost: string): EvidenceNode {
  return { kind, label, cost, children: [] };
}

function add(a: string, b: string): string {
  if (a === '1') return b;
  if (b === '1') return a;
  if (a === b) return a;
  return `${a} + ${b}`;
}

function mul(a: string, b: string): string {
  if (a === '1') return b;
  if (b === '1') return a;
  return `${a} ${b}`;
}

function maxSpace(a: string, b: string): string {
  if (a === '1') return b;
  if (b === '1') return a;
  return a.length >= b.length ? a : b;
}

function minConf(a: Confidence, b: Confidence): Confidence {
  const order: Confidence[] = ['unknown', 'low', 'medium', 'high'];
  return order[Math.min(order.indexOf(a), order.indexOf(b))];
}

function simplify(expr: string): string {
  return expr.replace(/\s\+\s1/g, '').replace(/^1\s\+\s/, '') || '1';
}

function rangeOf(node: ts.Node, source: ts.SourceFile): LineRange {
  const start = source.getLineAndCharacterOfPosition(node.getStart());
  const end = source.getLineAndCharacterOfPosition(node.getEnd());
  return {
    startLine: start.line,
    startCharacter: start.character,
    endLine: end.line,
    endCharacter: end.character,
  };
}

function signatureOf(
  node: ts.FunctionLikeDeclaration,
  source: ts.SourceFile,
): LineRange {
  const start = source.getLineAndCharacterOfPosition(node.getStart());
  const nameEnd = node.body
    ? node.body.getStart()
    : node.getEnd();
  const end = source.getLineAndCharacterOfPosition(nameEnd);
  return {
    startLine: start.line,
    startCharacter: start.character,
    endLine: end.line,
    endCharacter: end.character,
  };
}
