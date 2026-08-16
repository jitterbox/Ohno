import ts from 'typescript';

const Grow = new Set(['push', 'unshift', 'enqueue', 'add']);
const Shrink = new Set([
  'shift', 'pop', 'dequeue', 'tryDequeue', 'tryPop',
]);

export interface LoopFacts {
  grows: Set<string>;
  shrinks: Set<string>;
  visited: boolean;
  growCount: number;
  shrinkCount: number;
}

export function loopFacts(body: ts.Node): LoopFacts {
  const facts: LoopFacts = {
    grows: new Set(),
    shrinks: new Set(),
    visited: false,
    growCount: 0,
    shrinkCount: 0,
  };
  const visit = (node: ts.Node): void => {
    recordCall(node, facts);
    ts.forEachChild(node, visit);
  };
  visit(body);
  return facts;
}

export function queueFromCondition(
  condition: ts.Expression,
): string | undefined {
  const access = lengthAccess(condition)
    ?? (ts.isBinaryExpression(condition)
      ? lengthAccess(condition.left) ?? lengthAccess(condition.right)
      : undefined);
  return access ? receiverName(access.expression) : undefined;
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

function looksLikeSet(node: ts.CallExpression): boolean {
  if (!ts.isPropertyAccessExpression(node.expression)) return false;
  const recv = node.expression.expression;
  return ts.isIdentifier(recv)
    && /seen|visit|visited|done/i.test(recv.text);
}
