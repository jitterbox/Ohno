import ts from 'typescript';

const Grow = new Set(['push', 'add', 'set', 'unshift']);
const Immediate = new Set([
  'map', 'filter', 'forEach', 'reduce', 'flatMap',
  'find', 'some', 'every', 'sort', 'toSorted',
  'from',
]);

export function isImmediateCallback(
  node: ts.FunctionLikeDeclaration,
): boolean {
  const parent = node.parent;
  if (!parent) return false;
  if (ts.isCallExpression(parent) && parent.expression === node) {
    return true;
  }
  if (!ts.isCallExpression(parent)) return false;
  if (!parent.arguments.includes(node as ts.Expression)) return false;
  return Immediate.has(calleeName(parent));
}

export function growsOuterCollection(
  node: ts.FunctionLikeDeclaration,
  checker: ts.TypeChecker,
): boolean {
  if (!node.body) return false;
  let found = false;
  const visit = (child: ts.Node): void => {
    if (found) return;
    if (isGrowCall(child) && isOuterReceiver(child, node, checker)) {
      found = true;
      return;
    }
    ts.forEachChild(child, visit);
  };
  visit(node.body);
  return found;
}

function isGrowCall(node: ts.Node): node is ts.CallExpression {
  if (!ts.isCallExpression(node)) return false;
  if (!ts.isPropertyAccessExpression(node.expression)) return false;
  return Grow.has(node.expression.name.text);
}

function isOuterReceiver(
  call: ts.CallExpression,
  fn: ts.FunctionLikeDeclaration,
  checker: ts.TypeChecker,
): boolean {
  if (!ts.isPropertyAccessExpression(call.expression)) return false;
  const recv = call.expression.expression;
  if (!ts.isIdentifier(recv)) return false;
  const symbol = checker.getSymbolAtLocation(recv);
  const decl = symbol?.valueDeclaration;
  if (!decl) return false;
  return !hasAncestor(decl, fn);
}

function hasAncestor(node: ts.Node, ancestor: ts.Node): boolean {
  let current: ts.Node | undefined = node;
  while (current) {
    if (current === ancestor) return true;
    current = current.parent;
  }
  return false;
}

function calleeName(node: ts.CallExpression): string {
  if (ts.isIdentifier(node.expression)) return node.expression.text;
  if (ts.isPropertyAccessExpression(node.expression)) {
    return node.expression.name.text;
  }
  return '';
}
