import ts from 'typescript';
import {
  builtinTypeName,
  lookupBuiltin,
  type BuiltinEntry,
} from '../catalog/builtins';
import {
  call,
  conditional,
  loop,
  log,
  mul,
  ofCost,
  One,
  sequential,
  simplify,
  unitCost,
  type ComposedCost,
  type ComplexityExpression,
  type LineSpan,
} from '../engine';
import { rangeOf } from './functions';
import {
  createSizeState,
  dimensionFor,
  isOpaqueType,
  namedDimension,
  sizeOfReceiver,
  sizedTypeName,
  typeNameOf,
  type SizeState,
} from './sizes';

export interface WalkContext {
  checker: ts.TypeChecker;
  source: ts.SourceFile;
  sizes: SizeState;
  visited: Set<ts.Node>;
  reasons: string[];
}

export function createContext(
  checker: ts.TypeChecker,
  source: ts.SourceFile,
): WalkContext {
  return {
    checker,
    source,
    sizes: createSizeState(checker),
    visited: new Set(),
    reasons: [],
  };
}

export function walkNode(ctx: WalkContext, node: ts.Node): ComposedCost {
  if (ctx.visited.has(node)) return unitCost('call', 'recursive');
  ctx.visited.add(node);
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
  const body = walkNode(ctx, node.statement);
  const concat = stringConcatInLoop(ctx, node.statement);
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
  const name = callName(node);
  if (name === 'eval' || name === 'Function') {
    return ofCost(call(name), One, 'call', name, span, 'unknown');
  }
  const receiver = ts.isPropertyAccessExpression(node.expression)
    ? node.expression.expression
    : undefined;
  const typeName = receiverTypeName(ctx, node);
  const arity = node.arguments.length;
  const entry = typeName
    ? lookupBuiltin(typeName, name, arity)
    : undefined;
  if (entry && receiver && !receiverIsOpaque(ctx, receiver)) {
    return bindCall(ctx, node, entry, receiver, name, span);
  }
  if (entry && !receiver) {
    return bindCall(ctx, node, entry, node.arguments[0], name, span);
  }
  const local = localBody(ctx, node);
  if (local) return walkFunctionBody(ctx, local);
  noteUnresolved(ctx, name, receiver);
  return ofCost(call(name), One, 'call', name, span, 'low');
}

function walkNew(
  ctx: WalkContext,
  node: ts.NewExpression,
  span: LineSpan,
): ComposedCost {
  const name = node.expression.getText();
  const arg = node.arguments?.[0];
  if ((name === 'Array' || name === 'Map' || name === 'Set') && arg) {
    const size = dimensionFor(ctx.sizes, arg, arg.getText());
    return ofCost(size, size, 'new', name, span);
  }
  if (name === 'Array' || name === 'Map' || name === 'Set') {
    return unitCost('new', name, span);
  }
  return ofCost(call(name), One, 'new', name, span, 'low');
}

function walkBinary(
  ctx: WalkContext,
  node: ts.BinaryExpression,
  span: LineSpan,
): ComposedCost {
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
  return walkNode(ctx, node.expression);
}

function walkIndex(
  ctx: WalkContext,
  node: ts.ElementAccessExpression,
  span: LineSpan,
): ComposedCost {
  const type = ctx.checker.getTypeAtLocation(node.expression);
  if (sizedTypeName(type) === 'Array') {
    return unitCost('index', 'array index', span);
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
  if (!node.body) return unitCost('lambda', 'empty');
  return walkNode(ctx, node.body);
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
  if (!entry.loop) {
    return ofCost(time, space, 'call', name, span, confidence);
  }
  const callback = node.arguments.find((arg) =>
    ts.isArrowFunction(arg) || ts.isFunctionExpression(arg));
  const body = callback
    ? walkFunctionBody(ctx, callback)
    : ofCost(call('fn'), One, 'call', 'callback', span, 'low');
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

function loopBound(
  ctx: WalkContext,
  node: ts.ForStatement | ts.ForOfStatement
    | ts.WhileStatement | ts.DoStatement,
): ComplexityExpression {
  if (ts.isForOfStatement(node)) {
    return dimensionFor(
      ctx.sizes, node.expression, node.expression.getText(),
    );
  }
  if (ts.isForStatement(node) && node.condition) {
    const logBound = logUpdate(node);
    if (logBound) return log(namedDimension(ctx.sizes, 'n'));
    const length = lengthBound(ctx, node.condition);
    if (length) return length;
  }
  if ((ts.isWhileStatement(node) || ts.isDoStatement(node))
    && ts.isPrefixUnaryExpression(node.expression) === false) {
    const length = lengthBound(ctx, node.expression);
    if (length) return length;
  }
  ctx.reasons.push('loop bound is not a known size');
  return call('iterate');
}

function lengthBound(
  ctx: WalkContext,
  condition: ts.Expression,
): ComplexityExpression | undefined {
  if (!ts.isBinaryExpression(condition)) return undefined;
  const access = lengthAccess(condition.right)
    ?? lengthAccess(condition.left);
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
  return undefined;
}

function logUpdate(node: ts.ForStatement): boolean {
  const increment = node.incrementor;
  if (!increment) return false;
  const text = increment.getText();
  return text.includes('*= 2') || text.includes('/= 2')
    || text.includes('>>= 1') || text.includes('<<= 1');
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

function localBody(
  ctx: WalkContext,
  node: ts.CallExpression,
): ts.FunctionLikeDeclaration | undefined {
  const sig = ctx.checker.getResolvedSignature(node);
  const decl = sig?.declaration;
  if (!decl || !ts.isFunctionLike(decl) || !decl.body) return undefined;
  if (decl.getSourceFile() !== ctx.source) return undefined;
  return decl;
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
