import ts from 'typescript';

const Grow = new Set(['push', 'unshift', 'enqueue', 'add']);
const Shrink = new Set([
  'shift', 'pop', 'dequeue', 'tryDequeue', 'tryPop',
]);

export interface LoopFacts {
  grows: Set<string>;
  shrinks: Set<string>;
  visited: boolean;
  edges: boolean;
  successor: boolean;
  growCount: number;
  shrinkCount: number;
}

export function loopFacts(body: ts.Node): LoopFacts {
  const facts: LoopFacts = {
    grows: new Set(),
    shrinks: new Set(),
    visited: false,
    edges: false,
    successor: false,
    growCount: 0,
    shrinkCount: 0,
  };
  const visit = (node: ts.Node): void => {
    recordCall(node, facts);
    recordVisitWrite(node, facts);
    recordEdgeWalk(node, facts);
    ts.forEachChild(node, visit);
  };
  visit(body);
  return facts;
}

export function queueIdent(
  condition: ts.Expression,
): ts.Identifier | undefined {
  const access = lengthAccess(condition)
    ?? (ts.isBinaryExpression(condition)
      ? lengthAccess(condition.left) ?? lengthAccess(condition.right)
      : undefined);
  const recv = access?.expression;
  return recv && ts.isIdentifier(recv) ? recv : undefined;
}

export function queueFromCondition(
  condition: ts.Expression,
): string | undefined {
  return queueIdent(condition)?.text;
}

export function isIndexScan(condition: ts.Expression): boolean {
  if (!ts.isBinaryExpression(condition)) return false;
  const access = lengthAccess(condition.right)
    ?? lengthAccess(condition.left);
  const other = lengthAccess(condition.right)
    ? condition.left
    : condition.right;
  return !!access && ts.isIdentifier(other);
}

export function lengthAccess(
  node: ts.Expression,
): ts.PropertyAccessExpression | undefined {
  if (ts.isPropertyAccessExpression(node)
    && (node.name.text === 'length' || node.name.text === 'size')) {
    return node;
  }
  if (ts.isPrefixUnaryExpression(node)) {
    return lengthAccess(node.operand);
  }
  if (ts.isParenthesizedExpression(node)) {
    return lengthAccess(node.expression);
  }
  if (ts.isBinaryExpression(node)) {
    return lengthAccess(node.left) ?? lengthAccess(node.right);
  }
  return undefined;
}

export function receiverName(node: ts.Expression): string | undefined {
  return ts.isIdentifier(node) ? node.text : undefined;
}

export function isGrow(name: string): boolean {
  return Grow.has(name);
}

export function isShrink(name: string): boolean {
  return Shrink.has(name);
}

function recordCall(node: ts.Node, facts: LoopFacts): void {
  if (ts.isCallExpression(node)
    && ts.isPropertyAccessExpression(node.expression)) {
    const name = node.expression.name.text;
    const owner = receiverName(node.expression.expression);
    if (isGrow(name)) {
      facts.growCount++;
      if (owner) facts.grows.add(owner);
      if (node.arguments.some(isSuccessorArg)) facts.successor = true;
    }
    if (isShrink(name)) {
      facts.shrinkCount++;
      if (owner) facts.shrinks.add(owner);
    }
    if (name === 'has' || name === 'add') {
      facts.visited = facts.visited || looksLikeSet(node);
    }
  }
}

function recordVisitWrite(node: ts.Node, facts: LoopFacts): void {
  const name = writtenArray(node);
  if (name && /visit|seen|indeg|dist|done|mark/i.test(name)) {
    facts.visited = true;
  }
}

function recordEdgeWalk(node: ts.Node, facts: LoopFacts): void {
  if (ts.isForOfStatement(node)
    && ts.isElementAccessExpression(node.expression)) {
    facts.edges = true;
  }
}

function writtenArray(node: ts.Node): string | undefined {
  const target = assignmentTarget(node);
  if (target && ts.isElementAccessExpression(target)
    && ts.isIdentifier(target.expression)) {
    return target.expression.text;
  }
  return undefined;
}

function assignmentTarget(node: ts.Node): ts.Expression | undefined {
  if (ts.isPrefixUnaryExpression(node)
    || ts.isPostfixUnaryExpression(node)) {
    return node.operand;
  }
  if (ts.isBinaryExpression(node) && isAssign(node.operatorToken.kind)) {
    return node.left;
  }
  return undefined;
}

function isAssign(kind: ts.SyntaxKind): boolean {
  return kind === ts.SyntaxKind.EqualsToken
    || kind === ts.SyntaxKind.PlusEqualsToken
    || kind === ts.SyntaxKind.MinusEqualsToken;
}

function isSuccessorArg(arg: ts.Expression): boolean {
  const text = arg.getText();
  return /\.next\b|\.Next\b/.test(text);
}

function looksLikeSet(node: ts.CallExpression): boolean {
  if (!ts.isPropertyAccessExpression(node.expression)) return false;
  const recv = node.expression.expression;
  return ts.isIdentifier(recv)
    && /seen|visit|visited|done/i.test(recv.text);
}
