import ts from 'typescript';
import {
  builtinTypeName,
  lookupBuiltin,
  type BuiltinEntry,
} from '../catalog/builtins';
import {
  add,
  call,
  conditional,
  loop,
  log,
  mul,
  ofCost,
  One,
  sequential,
  simplify,
  unknown,
  unitCost,
  variable,
  type ComposedCost,
  type ComplexityExpression,
  type LineSpan,
} from '../engine';
import { rangeOf } from './functions';
import { loopFacts, queueIdent } from '../patterns/facts';
import {
  callIsTrivialRegex,
  isRegexCall,
} from '../patterns/regex';
import {
  binarySearchBound,
  countdownBound,
  isUnprovenCountdown,
  nullWalkBound,
  numericParamBound,
  twoPointerBound,
} from './loopShapes';
import { rankingBound } from './ranking';
import {
  growCard,
  retainedSize,
  seedCard,
} from './cardinality';
import {
  growsOuterCollection,
  isImmediateCallback,
} from './captures';
import {
  bindKey,
  createSizeState,
  dimensionFor,
  isOpaqueType,
  namedDimension,
  sizeOfReceiver,
  sizedTypeName,
  typeNameOf,
  type BindKey,
} from './sizes';

export interface WalkContext {
  checker: ts.TypeChecker;
  source: ts.SourceFile;
  program?: ts.Program;
  sizes: ReturnType<typeof createSizeState>;
  analyzing: Set<ts.Node>;
  bodyCache: Map<ts.Node, ComposedCost>;
  depth: number;
  reasons: string[];
  worklists: Map<BindKey, ComplexityExpression>;
  worklistKind: Map<BindKey, 'visit' | 'graph' | 'nodes' | 'unknown'>;
  loopStack: ComplexityExpression[];
  allocs: ComplexityExpression[];
  flattenAdj: boolean;
  graphWalked: boolean;
  inTwoPointer: boolean;
  unreachable: Set<ts.Node>;
}

export function createContext(
  checker: ts.TypeChecker,
  source: ts.SourceFile,
  program?: ts.Program,
): WalkContext {
  return {
    checker,
    source,
    program,
    sizes: createSizeState(checker),
    analyzing: new Set(),
    bodyCache: new Map(),
    depth: 0,
    reasons: [],
    worklists: new Map(),
    worklistKind: new Map(),
    loopStack: [],
    allocs: [],
    flattenAdj: false,
    graphWalked: false,
    inTwoPointer: false,
    unreachable: new Set(),
  };
}

export function walkNode(ctx: WalkContext, node: ts.Node): ComposedCost {
  if (ctx.unreachable.has(node)) return unitCost('dead', 'unreachable');
  const span = rangeOf(node, ctx.source);
  if (ts.isBlock(node)) return walkList(ctx, node.statements, span);
  if (ts.isSourceFile(node)) return walkList(ctx, node.statements, span);
  if (isLoop(node)) return walkLoop(ctx, node, span);
  if (ts.isIfStatement(node)) return walkIf(ctx, node, span);
  if (ts.isForInStatement(node)) return walkForIn(ctx, node, span);
  if (ts.isTryStatement(node)) return walkTry(ctx, node, span);
  if (ts.isReturnStatement(node) && node.expression) {
    return walkNode(ctx, node.expression);
  }
  if (ts.isExpressionStatement(node)) return walkNode(ctx, node.expression);
  if (ts.isVariableStatement(node)) {
    return walkList(ctx, [...node.declarationList.declarations], span);
  }
  if (ts.isVariableDeclaration(node) && node.initializer) {
    return walkNode(ctx, node.initializer);
  }
  return walkExpression(ctx, node, span);
}

export function walkList(
  ctx: WalkContext,
  nodes: readonly ts.Node[],
  span?: LineSpan,
): ComposedCost {
  return sequential(nodes.map((n) => walkNode(ctx, n)), span);
}

function walkExpression(
  ctx: WalkContext,
  node: ts.Node,
  span: LineSpan,
): ComposedCost {
  if (ts.isCallExpression(node)) return walkCall(ctx, node, span);
  if (ts.isNewExpression(node)) return walkNew(ctx, node, span);
  if (ts.isArrayLiteralExpression(node)) {
    return walkArrayLiteral(ctx, node, span);
  }
  if (ts.isSpreadElement(node) || ts.isSpreadAssignment(node)) {
    const size = dimensionFor(
      ctx.sizes, node.expression, node.expression.getText(),
    );
    ctx.allocs.push(size);
    return ofCost(size, size, 'spread', 'spread', span);
  }
  if (ts.isAwaitExpression(node)) return walkNode(ctx, node.expression);
  if (ts.isBinaryExpression(node)) return walkBinary(ctx, node, span);
  if (ts.isPropertyAccessExpression(node)) {
    return walkProperty(ctx, node, span);
  }
  if (ts.isElementAccessExpression(node)) {
    return walkIndex(ctx, node, span);
  }
  if (ts.isArrowFunction(node) || ts.isFunctionExpression(node)) {
    return walkFunctionBody(ctx, node);
  }
  if (ts.isJsxElement(node) || ts.isJsxSelfClosingElement(node)
    || ts.isJsxFragment(node)) {
    return walkJsx(ctx, node, span);
  }
  return walkChildren(ctx, node, span);
}

function walkChildren(
  ctx: WalkContext,
  node: ts.Node,
  span: LineSpan,
): ComposedCost {
  const parts: ComposedCost[] = [];
  ts.forEachChild(node, (child) => {
    parts.push(walkNode(ctx, child));
  });
  return parts.length === 0
    ? unitCost('expr', node.kind.toString(), span)
    : sequential(parts, span);
}

function isLoop(
  node: ts.Node,
): node is ts.ForStatement | ts.ForOfStatement
  | ts.WhileStatement | ts.DoStatement {
  return ts.isForStatement(node)
    || ts.isForOfStatement(node)
    || ts.isWhileStatement(node)
    || ts.isDoStatement(node);
}

function walkLoop(
  ctx: WalkContext,
  node: ts.ForStatement | ts.ForOfStatement
    | ts.WhileStatement | ts.DoStatement,
  span: LineSpan,
): ComposedCost {
  if (ts.isForOfStatement(node) && node.awaitModifier) {
    ctx.reasons.push('for-await is opaque');
    return ofCost(
      { kind: 'unknown', reason: 'for-await' },
      One, 'loop', 'for await', span, 'unknown',
    );
  }
  const bound = loopBound(ctx, node);
  const prevFlat = ctx.flattenAdj;
  const prevGraph = ctx.graphWalked;
  const prevTp = ctx.inTwoPointer;
  if ((ts.isWhileStatement(node) || ts.isDoStatement(node))
    && twoPointerBound(node, ctx.sizes)) {
    ctx.inTwoPointer = true;
  }
  if (isGraphWorklist(ctx, node)) {
    ctx.flattenAdj = true;
    ctx.graphWalked = true;
  } else {
    ctx.graphWalked = false;
  }
  ctx.loopStack.push(bound);
  const body = walkNode(ctx, node.statement);
  ctx.loopStack.pop();
  const innerGraph = ctx.graphWalked;
  ctx.flattenAdj = prevFlat;
  ctx.graphWalked = prevGraph || innerGraph;
  ctx.inTwoPointer = prevTp;
  const concat = stringConcatInLoop(ctx, node.statement);
  if (ts.isForStatement(node) && containsGraphWhile(node.statement, ctx)) {
    return sequential([
      ofCost(bound, One, 'loop', loopLabel(node), span),
      body,
    ], span);
  }
  const cost = loop(bound, body, loopLabel(node), span);
  if (!concat) return cost;
  return { ...cost, time: simplify(mul(bound, bound)) };
}

function walkForIn(
  ctx: WalkContext,
  node: ts.ForInStatement,
  span: LineSpan,
): ComposedCost {
  ctx.reasons.push('for…in key count is approximate');
  const bound = dimensionFor(ctx.sizes, node.expression, node.expression.getText());
  return loop(bound, walkNode(ctx, node.statement), 'for…in', span);
}

function walkIf(
  ctx: WalkContext,
  node: ts.IfStatement,
  span: LineSpan,
): ComposedCost {
  return conditional(
    walkNode(ctx, node.expression),
    walkNode(ctx, node.thenStatement),
    node.elseStatement ? walkNode(ctx, node.elseStatement) : undefined,
    span,
  );
}

function walkTry(
  ctx: WalkContext,
  node: ts.TryStatement,
  span: LineSpan,
): ComposedCost {
  const parts = [walkNode(ctx, node.tryBlock)];
  if (node.catchClause) parts.push(walkNode(ctx, node.catchClause.block));
  if (node.finallyBlock) parts.push(walkNode(ctx, node.finallyBlock));
  return sequential(parts, span);
}

function walkCall(
  ctx: WalkContext,
  node: ts.CallExpression,
  span: LineSpan,
): ComposedCost {
  if (ts.isNewExpression(node.expression)) {
    return walkNode(ctx, node.expression);
  }
  const name = callName(node);
  const receiver = ts.isPropertyAccessExpression(node.expression)
    ? node.expression.expression
    : undefined;
  if (name === 'call' || name === 'apply' || name === 'bind') {
    return ofCost(call(name), One, 'call', name, span, 'low');
  }
  if (name === 'from' && receiver && ts.isIdentifier(receiver)
    && receiver.text === 'Array') {
    return walkArrayFrom(ctx, node, span);
  }
  if (isRegexCall(name) && callIsTrivialRegex(node)) {
    const subject = name === 'test' || name === 'exec'
      ? node.arguments[0]
      : receiver;
    if (subject) {
      const size = sizeOfReceiver(
        ctx.sizes, subject as ts.LeftHandSideExpression,
      );
      return ofCost(size, One, 'call', name, span);
    }
  }
  if (name === 'eval' || name === 'Function') {
    return ofCost(call(name), One, 'call', name, span, 'unknown');
  }
  const typeName = receiverTypeName(ctx, node);
  const arity = node.arguments.length;
  const entry = typeName
    ? lookupBuiltin(typeName, name, arity)
    : undefined;
  if (entry && receiver && !receiverIsOpaque(ctx, receiver)) {
    const source = sizeSource(node, receiver);
    return withReceiver(ctx, receiver, bindCall(
      ctx, node, entry, source, name, span,
    ));
  }
  if (entry && !receiver) {
    return bindCall(ctx, node, entry, node.arguments[0], name, span);
  }
  const local = localBody(ctx, node);
  if (local) return walkLocal(ctx, local);
  noteUnresolved(ctx, name, receiver);
  return ofCost(call(name), One, 'call', name, span, 'low');
}

function walkNew(
  ctx: WalkContext,
  node: ts.NewExpression,
  span: LineSpan,
): ComposedCost {
  const name = ctorName(node);
  if (name === 'Proxy' || name === 'Function') {
    return ofCost(call(name), One, 'new', name, span, 'low');
  }
  const arg = node.arguments?.[0];
  if ((name === 'Array' || name === 'Map' || name === 'Set') && arg) {
    const size = dimensionFor(ctx.sizes, arg, arg.getText());
    noteAlloc(ctx, node, size);
    return ofCost(size, size, 'new', name, span);
  }
  if (name === 'Array' || name === 'Map' || name === 'Set'
    || name === 'MinHeap' || name === 'ListNode') {
    noteAlloc(ctx, node, One);
    return unitCost('new', name, span);
  }
  return ofCost(call(name), One, 'new', name, span, 'low');
}

function walkBinary(
  ctx: WalkContext,
  node: ts.BinaryExpression,
  span: LineSpan,
): ComposedCost {
  if (isAssign(node.operatorToken.kind)) {
    noteMatrix(ctx, node.left);
  }
  const left = walkNode(ctx, node.left);
  const right = walkNode(ctx, node.right);
  return sequential([left, right], span);
}

function walkProperty(
  ctx: WalkContext,
  node: ts.PropertyAccessExpression,
  span: LineSpan,
): ComposedCost {
  if (node.name.text === 'length' || node.name.text === 'size') {
    return unitCost('field', node.name.text, span);
  }
  const getter = localGetter(ctx, node);
  if (getter) return walkNode(ctx, getter);
  return walkNode(ctx, node.expression);
}

function walkIndex(
  ctx: WalkContext,
  node: ts.ElementAccessExpression,
  span: LineSpan,
): ComposedCost {
  const type = ctx.checker.getTypeAtLocation(node.expression);
  const sized = sizedTypeName(type);
  if (sized === 'Array' || sized === 'String') {
    return unitCost('index', 'index', span);
  }
  if (!isOpaqueType(type) && type.getNumberIndexType()) {
    return unitCost('index', 'index', span);
  }
  if (!isOpaqueType(type) && type.getStringIndexType()) {
    return ofCost(One, One, 'index', 'index', span, 'medium');
  }
  return ofCost(call('get'), One, 'index', 'index', span, 'low');
}

function walkJsx(
  ctx: WalkContext,
  node: ts.Node,
  span: LineSpan,
): ComposedCost {
  return walkChildren(ctx, node, span);
}

function walkFunctionBody(
  ctx: WalkContext,
  node: ts.FunctionLikeDeclaration,
): ComposedCost {
  return cachedBody(ctx, node, () => walkFreshBody(ctx, node));
}

function walkFreshBody(
  ctx: WalkContext,
  node: ts.FunctionLikeDeclaration,
): ComposedCost {
  if (!node.body) return unitCost('lambda', 'empty');
  if (!isImmediateCallback(node)
    && growsOuterCollection(node, ctx.checker)) {
    ctx.reasons.push(
      'stored callback mutates a captured collection',
    );
    return ofCost(
      call('mutate'), One, 'lambda', 'stored', undefined, 'low',
    );
  }
  return walkNode(ctx, node.body);
}

function cachedBody(
  ctx: WalkContext,
  node: ts.Node,
  walk: () => ComposedCost,
): ComposedCost {
  const cached = ctx.bodyCache.get(node);
  if (cached) return cached;
  if (ctx.analyzing.has(node)) return unitCost('call', 'recursive');
  ctx.analyzing.add(node);
  const cost = walk();
  ctx.analyzing.delete(node);
  ctx.bodyCache.set(node, cost);
  return cost;
}

function bindCall(
  ctx: WalkContext,
  node: ts.CallExpression,
  entry: BuiltinEntry,
  receiver: ts.Node | undefined,
  name: string,
  span: LineSpan,
): ComposedCost {
  const size = receiver
    ? sizeOfReceiver(ctx.sizes, receiver as ts.LeftHandSideExpression)
    : One;
  const time = bindSize(entry.time, size);
  const space = bindSize(entry.space, size);
  const confidence = entry.kind === 'exact' ? 'high' : 'medium';
  if (entry.kind !== 'exact') {
    ctx.reasons.push(`${name} is ${entry.kind}`);
  }
  noteGrow(ctx, node, name);
  const matrix = arrayFromSpace(ctx, node, name, size);
  if (matrix) ctx.allocs.push(matrix);
  if (!entry.loop) {
    return ofCost(time, space, 'call', name, span, confidence);
  }
  ctx.loopStack.push(size);
  const callback = node.arguments.find((arg) =>
    ts.isArrowFunction(arg) || ts.isFunctionExpression(arg));
  const body = callback
    ? walkFunctionBody(ctx, callback)
    : ofCost(call('fn'), One, 'call', 'callback', span, 'low');
  ctx.loopStack.pop();
  return loop(size, body, name, span);
}

function bindSize(
  kind: BuiltinEntry['time'],
  size: ComplexityExpression,
): ComplexityExpression {
  if (kind === 'constant') return One;
  if (kind === 'receiver') return size;
  if (kind === 'logReceiver') return log(size);
  return mul(size, log(size));
}

type LoopNode = ts.ForStatement | ts.ForOfStatement
  | ts.WhileStatement | ts.DoStatement;

function loopBound(
  ctx: WalkContext,
  node: LoopNode,
): ComplexityExpression {
  if (ts.isForOfStatement(node)) return forOfBound(ctx, node);
  if (ts.isForStatement(node)) {
    const bound = forLoopBound(ctx, node);
    if (bound) return bound;
  }
  if (ts.isWhileStatement(node) || ts.isDoStatement(node)) {
    const bound = whileLoopBound(ctx, node);
    if (bound) return bound;
  }
  return fallbackBound(ctx, node);
}

function forOfBound(
  ctx: WalkContext,
  node: ts.ForOfStatement,
): ComplexityExpression {
  if (ctx.flattenAdj
    && ts.isElementAccessExpression(node.expression)) {
    return One;
  }
  return dimensionFor(
    ctx.sizes, node.expression, node.expression.getText(),
  );
}

/** for: log increment → length → ranking → init length → numeric */
function forLoopBound(
  ctx: WalkContext,
  node: ts.ForStatement,
): ComplexityExpression | undefined {
  if (!node.condition) return undefined;
  if (isLogIncrement(node)) {
    const logBound = logForBound(ctx, node);
    if (logBound) return logBound;
  }
  const length = lengthBound(ctx, node.condition);
  if (length) return length;
  const ranked = rankedBound(ctx, node);
  if (ranked) return ranked;
  const fromInit = initLengthBound(ctx, node);
  if (fromInit) return fromInit;
  return numericParamBound(
    node.condition, ctx.sizes, incrementName(node),
  );
}

/**
 * while/do: worklist → binary search → two-pointer → countdown →
 * nested shrink → pointerAdvance → ranking → unproven →
 * null walk → length
 */
function whileLoopBound(
  ctx: WalkContext,
  node: ts.WhileStatement | ts.DoStatement,
): ComplexityExpression | undefined {
  const q = queueKey(ctx, node.expression);
  if (q && ctx.worklistKind.has(q.key)) {
    return resolveWorklist(ctx, q.key);
  }
  if (q && ctx.worklists.has(q.key)) return ctx.worklists.get(q.key);
  const binary = binarySearchBound(node, ctx.sizes);
  if (binary) return binary;
  const pointers = twoPointerBound(node, ctx.sizes);
  if (pointers) return ctx.inTwoPointer ? One : pointers;
  const count = countdownBound(node, ctx.sizes);
  if (count) return ctx.loopStack.length > 0 ? One : count;
  if (q && nestedShrink(ctx, node, q.name)) return One;
  if (ctx.loopStack.length > 0 && pointerAdvance(node)) return One;
  const ranked = rankedBound(ctx, node);
  if (ranked) return ranked;
  if (isUnprovenCountdown(node)) {
    ctx.reasons.push('loop update is not a proven shrinkage');
    return unknown('unproven-loop');
  }
  const chain = nullWalkBound(node, ctx.sizes);
  if (chain) {
    ctx.reasons.push('null-terminated walk assumes a finite chain');
    return chain;
  }
  return lengthBound(ctx, node.expression);
}

function fallbackBound(
  ctx: WalkContext,
  node: LoopNode,
): ComplexityExpression {
  if (!ts.isForOfStatement(node)) {
    const ranked = rankedBound(ctx, node);
    if (ranked) return ranked;
  }
  ctx.reasons.push('loop bound is not a known size');
  return call('iterate');
}

function rankedBound(
  ctx: WalkContext,
  node: ts.ForStatement | ts.WhileStatement | ts.DoStatement,
): ComplexityExpression | undefined {
  const ranked = rankingBound(node, ctx.sizes);
  if (!ranked) return undefined;
  ctx.reasons.push('loop bound from a proven ranking update');
  return ranked;
}

function queueKey(
  ctx: WalkContext,
  cond: ts.Expression,
): { key: BindKey; name: string } | undefined {
  const ident = queueIdent(cond);
  if (!ident) return undefined;
  return { key: bindKey(ctx.checker, ident), name: ident.text };
}

function nestedShrink(
  ctx: WalkContext,
  node: ts.WhileStatement | ts.DoStatement,
  queue: string,
): boolean {
  if (ctx.loopStack.length === 0) return false;
  const facts = loopFacts(node.statement);
  return facts.shrinks.has(queue) && !facts.grows.has(queue);
}

function logForBound(
  ctx: WalkContext,
  node: ts.ForStatement,
): ComplexityExpression | undefined {
  const ranked = rankedBound(ctx, node);
  if (ranked) return ranked;
  if (!node.condition) return undefined;
  const length = lengthBound(ctx, node.condition);
  if (length) return log(length);
  const numeric = numericParamBound(
    node.condition, ctx.sizes, incrementName(node),
  );
  return numeric ? log(numeric) : undefined;
}

function lengthBound(
  ctx: WalkContext,
  condition: ts.Expression,
): ComplexityExpression | undefined {
  const access = lengthAccess(condition)
    ?? (ts.isBinaryExpression(condition)
      ? lengthAccess(condition.right) ?? lengthAccess(condition.left)
      : undefined);
  if (!access) return undefined;
  return sizeOfReceiver(ctx.sizes, access.expression);
}

function lengthAccess(
  node: ts.Expression,
): ts.PropertyAccessExpression | undefined {
  if (ts.isPropertyAccessExpression(node)
    && (node.name.text === 'length' || node.name.text === 'size')) {
    return node;
  }
  if (ts.isParenthesizedExpression(node)) {
    return lengthAccess(node.expression);
  }
  if (ts.isPrefixUnaryExpression(node)) {
    return lengthAccess(node.operand);
  }
  if (ts.isBinaryExpression(node)) {
    return lengthAccess(node.left) ?? lengthAccess(node.right);
  }
  return undefined;
}

function isLogIncrement(node: ts.ForStatement): boolean {
  const increment = node.incrementor;
  return !!increment && isLogStep(increment);
}

function isLogStep(expr: ts.Expression): boolean {
  if (!ts.isBinaryExpression(expr)) return false;
  const op = expr.operatorToken.kind;
  if (op === ts.SyntaxKind.AsteriskEqualsToken
    || op === ts.SyntaxKind.SlashEqualsToken) {
    return isTwoLit(expr.right);
  }
  if (op === ts.SyntaxKind.GreaterThanGreaterThanEqualsToken
    || op === ts.SyntaxKind.LessThanLessThanEqualsToken
    || op === ts.SyntaxKind.GreaterThanGreaterThanGreaterThanEqualsToken) {
    return isOneLit(expr.right);
  }
  return false;
}

function isTwoLit(node: ts.Expression): boolean {
  return ts.isNumericLiteral(node) && node.text === '2';
}

function isOneLit(node: ts.Expression): boolean {
  return ts.isNumericLiteral(node) && node.text === '1';
}

function stringConcatInLoop(
  ctx: WalkContext,
  body: ts.Node,
): boolean {
  let found = false;
  const visit = (node: ts.Node): void => {
    if (ts.isBinaryExpression(node)
      && node.operatorToken.kind === ts.SyntaxKind.PlusEqualsToken) {
      const type = ctx.checker.getTypeAtLocation(node.left);
      if (type.flags & ts.TypeFlags.StringLike) found = true;
    }
    ts.forEachChild(node, visit);
  };
  visit(body);
  return found;
}

function loopLabel(node: ts.Node): string {
  if (ts.isForOfStatement(node)) return 'for…of';
  if (ts.isForStatement(node)) return 'for';
  if (ts.isDoStatement(node)) return 'do';
  return 'while';
}

function callName(node: ts.CallExpression): string {
  if (ts.isIdentifier(node.expression)) return node.expression.text;
  if (ts.isPropertyAccessExpression(node.expression)) {
    return node.expression.name.text;
  }
  return 'call';
}

function receiverTypeName(
  ctx: WalkContext,
  node: ts.CallExpression,
): string | undefined {
  if (ts.isPropertyAccessExpression(node.expression)) {
    const type = ctx.checker.getTypeAtLocation(node.expression.expression);
    const sized = sizedTypeName(type);
    if (sized) return sized;
    return builtinTypeName(typeNameOf(type) ?? '');
  }
  if (ts.isIdentifier(node.expression)) {
    return builtinTypeName(node.expression.text);
  }
  return undefined;
}

function receiverIsOpaque(
  ctx: WalkContext,
  receiver: ts.Expression,
): boolean {
  return isOpaqueType(ctx.checker.getTypeAtLocation(receiver));
}

function localGetter(
  ctx: WalkContext,
  node: ts.PropertyAccessExpression,
): ts.Node | undefined {
  const symbol = ctx.checker.getSymbolAtLocation(node.name);
  const decl = symbol?.declarations?.find((item) =>
    ts.isGetAccessorDeclaration(item) && item.body);
  if (!decl || !ts.isGetAccessorDeclaration(decl) || !decl.body) {
    return undefined;
  }
  if (!inProgram(ctx, decl)) return undefined;
  return decl.body;
}

function localBody(
  ctx: WalkContext,
  node: ts.CallExpression,
): ts.FunctionLikeDeclaration | undefined {
  const sig = ctx.checker.getResolvedSignature(node);
  const decl = callableBody(sig?.declaration);
  if (!decl) return undefined;
  if (!inProgram(ctx, decl)) return undefined;
  if (ctx.depth >= 8) return undefined;
  return decl;
}

function callableBody(
  decl: ts.Declaration | undefined,
): ts.FunctionLikeDeclaration | undefined {
  if (!decl || !ts.isFunctionLike(decl)) return undefined;
  if (!ts.isFunctionDeclaration(decl)
    && !ts.isMethodDeclaration(decl)
    && !ts.isConstructorDeclaration(decl)
    && !ts.isGetAccessorDeclaration(decl)
    && !ts.isSetAccessorDeclaration(decl)
    && !ts.isFunctionExpression(decl)
    && !ts.isArrowFunction(decl)) {
    return undefined;
  }
  return decl.body ? decl : undefined;
}

function walkLocal(
  ctx: WalkContext,
  node: ts.FunctionLikeDeclaration,
): ComposedCost {
  const cached = ctx.bodyCache.get(node);
  if (cached) return cached;
  if (ctx.analyzing.has(node)) return unitCost('call', 'recursive');
  ctx.depth += 1;
  const cost = walkFunctionBody(ctx, node);
  ctx.depth -= 1;
  return cost;
}

function inProgram(ctx: WalkContext, decl: ts.Node): boolean {
  const file = decl.getSourceFile();
  if (file === ctx.source) return true;
  if (!ctx.program || isLibFile(file.fileName)) return false;
  return !!ctx.program.getSourceFile(file.fileName);
}

function isLibFile(fileName: string): boolean {
  return fileName.includes('node_modules')
    || /lib\.(es|dom|webworker|scripthost)/.test(fileName);
}

function withReceiver(
  ctx: WalkContext,
  receiver: ts.Expression,
  cost: ComposedCost,
): ComposedCost {
  if (ts.isIdentifier(receiver)) return cost;
  return sequential([walkNode(ctx, receiver), cost]);
}

function containsGraphWhile(
  body: ts.Node,
  ctx: WalkContext,
): boolean {
  let found = false;
  const visit = (node: ts.Node): void => {
    if (found) return;
    if ((ts.isWhileStatement(node) || ts.isDoStatement(node))
      && isGraphWorklist(ctx, node)) {
      found = true;
      return;
    }
    ts.forEachChild(node, visit);
  };
  visit(body);
  return found;
}

function pointerAdvance(
  node: ts.WhileStatement | ts.DoStatement,
): boolean {
  const names = identsIn(node.expression);
  if (names.size === 0) return false;
  return bodyAdvances(node.statement, names);
}

function identsIn(expr: ts.Expression): Set<string> {
  const names = new Set<string>();
  const visit = (n: ts.Node): void => {
    if (ts.isIdentifier(n)) names.add(n.text);
    ts.forEachChild(n, visit);
  };
  visit(expr);
  return names;
}

function bodyAdvances(body: ts.Node, names: Set<string>): boolean {
  let found = false;
  const visit = (n: ts.Node): void => {
    if (found) return;
    if (isIncDec(n, names)) {
      found = true;
      return;
    }
    ts.forEachChild(n, visit);
  };
  visit(body);
  return found;
}

function isIncDec(node: ts.Node, names: Set<string>): boolean {
  if (!ts.isPrefixUnaryExpression(node)
    && !ts.isPostfixUnaryExpression(node)) {
    return false;
  }
  if (!ts.isIdentifier(node.operand)) return false;
  if (!names.has(node.operand.text)) return false;
  return node.operator === ts.SyntaxKind.PlusPlusToken
    || node.operator === ts.SyntaxKind.MinusMinusToken;
}

function incrementName(
  node: ts.ForStatement,
): string | undefined {
  const inc = node.incrementor;
  if (!inc) return undefined;
  if ((ts.isPrefixUnaryExpression(inc)
    || ts.isPostfixUnaryExpression(inc))
    && ts.isIdentifier(inc.operand)) {
    return inc.operand.text;
  }
  if (ts.isBinaryExpression(inc) && ts.isIdentifier(inc.left)) {
    return inc.left.text;
  }
  return undefined;
}

function initLengthBound(
  ctx: WalkContext,
  node: ts.ForStatement,
): ComplexityExpression | undefined {
  const expr = initValue(node);
  if (!expr) return undefined;
  const length = lengthBound(ctx, expr);
  if (length) return length;
  if (ts.isIdentifier(expr)) return namedDimension(ctx.sizes, expr.text);
  return undefined;
}

function initValue(
  node: ts.ForStatement,
): ts.Expression | undefined {
  const init = node.initializer;
  if (!init) return undefined;
  if (ts.isVariableDeclarationList(init)) {
    return init.declarations[0]?.initializer;
  }
  if (ts.isBinaryExpression(init)) return init.right;
  return undefined;
}

function noteGrow(
  ctx: WalkContext,
  node: ts.CallExpression,
  name: string,
): void {
  if (ctx.loopStack.length === 0) return;
  if (name !== 'set' && name !== 'add' && name !== 'push') return;
  const receiver = ts.isPropertyAccessExpression(node.expression)
    ? node.expression.expression
    : undefined;
  const ident = receiver && ts.isIdentifier(receiver)
    ? receiver
    : undefined;
  const key = ident ? bindKey(ctx.checker, ident) : undefined;
  if (key && ctx.sizes.heaps.has(key) && name === 'push') return;
  const bound = ctx.loopStack[ctx.loopStack.length - 1];
  const amount = retainedSize(bound, pushElement(ctx, node));
  ctx.allocs.push(amount);
  if (key) {
    growCard(ctx.sizes, key, amount);
    if (!ctx.sizes.heaps.has(key)) ctx.sizes.heaps.set(key, amount);
  }
}

function pushElement(
  ctx: WalkContext,
  node: ts.CallExpression,
): ComplexityExpression {
  const arg = node.arguments[0];
  if (!arg) return One;
  const size = arrayAllocSize(ctx, arg);
  return size ?? One;
}

function ctorName(node: ts.NewExpression): string {
  return ts.isIdentifier(node.expression)
    ? node.expression.text
    : node.expression.getText();
}

function arrayAllocSize(
  ctx: WalkContext,
  node: ts.Expression,
): ComplexityExpression | undefined {
  if (ts.isNewExpression(node)
    && ctorName(node) === 'Array'
    && node.arguments?.[0]) {
    const arg = node.arguments[0];
    return dimensionFor(ctx.sizes, arg, arg.getText());
  }
  return undefined;
}

function resolveWorklist(
  ctx: WalkContext,
  queue: BindKey,
): ComplexityExpression {
  const kind = ctx.worklistKind.get(queue);
  if (kind === 'unknown') return unknown('worklist');
  if (kind === 'nodes') return namedDimension(ctx.sizes, 'n');
  const visit = existingDim(ctx, 0, 'n');
  if (!ctx.sizes.heaps.has(queue)) ctx.sizes.heaps.set(queue, visit);
  if (kind === 'graph') {
    return add(visit, existingDim(ctx, 1, 'm'));
  }
  return visit;
}

function existingDim(
  ctx: WalkContext,
  index: number,
  fallback: string,
): ComplexityExpression {
  const dim = ctx.sizes.dims[index];
  return dim ? variable(dim.variable) : namedDimension(ctx.sizes, fallback);
}

function isGraphWorklist(
  ctx: WalkContext,
  node: ts.ForStatement | ts.ForOfStatement
    | ts.WhileStatement | ts.DoStatement,
): boolean {
  if (!ts.isWhileStatement(node) && !ts.isDoStatement(node)) {
    return false;
  }
  const q = queueKey(ctx, node.expression);
  return !!q && ctx.worklistKind.get(q.key) === 'graph';
}

function noteUnresolved(
  ctx: WalkContext,
  name: string,
  receiver?: ts.Expression,
): void {
  if (receiver && receiverIsOpaque(ctx, receiver)) {
    ctx.reasons.push(`${name} receiver is any`);
  }
}

function walkArrayFrom(
  ctx: WalkContext,
  node: ts.CallExpression,
  span: LineSpan,
): ComposedCost {
  const src = node.arguments[0];
  const size = src
    ? dimensionFor(ctx.sizes, src, src.getText())
    : call('iterate');
  ctx.loopStack.push(size);
  const callback = node.arguments[1];
  const body = callback
    && (ts.isArrowFunction(callback) || ts.isFunctionExpression(callback))
    ? walkFunctionBody(ctx, callback)
    : unitCost('call', 'from', span);
  ctx.loopStack.pop();
  const matrix = arrayFromSpace(ctx, node, 'from', size);
  if (matrix) ctx.allocs.push(matrix);
  return loop(size, body, 'from', span);
}

function walkArrayLiteral(
  ctx: WalkContext,
  node: ts.ArrayLiteralExpression,
  span: LineSpan,
): ComposedCost {
  const key = assignedKey(ctx, node);
  if (key) seedCard(ctx.sizes, key, One);
  return walkChildren(ctx, node, span);
}

function sizeSource(
  node: ts.CallExpression,
  receiver: ts.Expression,
): ts.Node {
  if (ts.isIdentifier(receiver) && /^[A-Z]/.test(receiver.text)) {
    return node.arguments[0] ?? receiver;
  }
  return receiver;
}

function noteAlloc(
  ctx: WalkContext,
  node: ts.NewExpression,
  size: ComplexityExpression,
): void {
  const key = assignedKey(ctx, node);
  if (key) seedCard(ctx.sizes, key, size);
  if (isRetainedAlloc(node)) return;
  ctx.allocs.push(size);
}

function isRetainedAlloc(node: ts.NewExpression): boolean {
  const parent = node.parent;
  if (!parent || !ts.isCallExpression(parent)) return false;
  if (!parent.arguments.includes(node as ts.Expression)) return false;
  return callName(parent) === 'push' || callName(parent) === 'add';
}

function noteMatrix(ctx: WalkContext, target: ts.Expression): void {
  if (!ts.isElementAccessExpression(target)) return;
  if (!ts.isElementAccessExpression(target.expression)) return;
  if (ctx.loopStack.length < 2) return;
  const ident = rootIdent(target);
  if (!ident) return;
  const key = bindKey(ctx.checker, ident);
  const card = ctx.sizes.cards.get(key);
  if (!card || card.max.kind !== 'const') return;
  const space = mul(
    ctx.loopStack[ctx.loopStack.length - 2],
    ctx.loopStack[ctx.loopStack.length - 1],
  );
  seedCard(ctx.sizes, key, space);
  ctx.allocs.push(space);
}

function arrayFromSpace(
  ctx: WalkContext,
  node: ts.CallExpression,
  name: string,
  rows: ComplexityExpression,
): ComplexityExpression | undefined {
  if (name !== 'from' || node.arguments.length < 2) return undefined;
  const inner = innerArraySize(ctx, node.arguments[1]);
  if (!inner) return undefined;
  const space = mul(rows, inner);
  const key = assignedKey(ctx, node);
  if (key) seedCard(ctx.sizes, key, space);
  return space;
}

function innerArraySize(
  ctx: WalkContext,
  callback: ts.Expression,
): ComplexityExpression | undefined {
  if (!ts.isArrowFunction(callback) && !ts.isFunctionExpression(callback)) {
    return undefined;
  }
  const body = ts.isBlock(callback.body)
    ? callback.body.statements[0]
    : callback.body;
  const expr = body && ts.isReturnStatement(body)
    ? body.expression
    : body && ts.isExpression(body) ? body : undefined;
  if (!expr) return undefined;
  const alloc = ts.isCallExpression(expr) && callName(expr) === 'fill'
    && ts.isPropertyAccessExpression(expr.expression)
    ? expr.expression.expression
    : expr;
  return arrayAllocSize(ctx, alloc as ts.Expression);
}

function assignedIdent(node: ts.Node): ts.Identifier | undefined {
  const parent = node.parent;
  if (!parent) return undefined;
  if (ts.isVariableDeclaration(parent) && ts.isIdentifier(parent.name)) {
    return parent.name;
  }
  if (ts.isBinaryExpression(parent) && ts.isIdentifier(parent.left)
    && parent.right === node) {
    return parent.left;
  }
  return undefined;
}

function assignedKey(
  ctx: WalkContext,
  node: ts.Node,
): BindKey | undefined {
  const ident = assignedIdent(node);
  return ident ? bindKey(ctx.checker, ident) : undefined;
}

function rootIdent(node: ts.Expression): ts.Identifier | undefined {
  if (ts.isIdentifier(node)) return node;
  if (ts.isElementAccessExpression(node)) {
    return rootIdent(node.expression);
  }
  if (ts.isPropertyAccessExpression(node)) {
    return rootIdent(node.expression);
  }
  return undefined;
}

function isAssign(kind: ts.SyntaxKind): boolean {
  return kind === ts.SyntaxKind.EqualsToken
    || kind === ts.SyntaxKind.PlusEqualsToken;
}

