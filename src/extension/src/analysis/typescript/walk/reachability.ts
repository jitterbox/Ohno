import ts from 'typescript';

export function collectUnreachable(root: ts.Node): Set<ts.Node> {
  const dead = new Set<ts.Node>();
  const visit = (node: ts.Node): void => {
    noteUnreachable(node, dead);
    ts.forEachChild(node, visit);
  };
  visit(root);
  return dead;
}

export function noteUnreachable(
  node: ts.Node,
  dead: Set<ts.Node>,
): void {
  markLiteralIf(node, dead);
  markAfterExit(node, dead);
}

export function isLiteralFalse(node: ts.Expression): boolean {
  return node.kind === ts.SyntaxKind.FalseKeyword
    || isFalseAnd(node);
}

export function isLiteralTrue(node: ts.Expression): boolean {
  return node.kind === ts.SyntaxKind.TrueKeyword;
}

function isFalseAnd(node: ts.Expression): boolean {
  if (!ts.isBinaryExpression(node)) return false;
  if (node.operatorToken.kind
    !== ts.SyntaxKind.AmpersandAmpersandToken) {
    return false;
  }
  return isNegationOf(node.right, node.left)
    || isNegationOf(node.left, node.right);
}

function isNegationOf(a: ts.Expression, b: ts.Expression): boolean {
  return ts.isPrefixUnaryExpression(a)
    && a.operator === ts.SyntaxKind.ExclamationToken
    && sameIdent(a.operand, b);
}

function sameIdent(a: ts.Expression, b: ts.Expression): boolean {
  return ts.isIdentifier(a) && ts.isIdentifier(b) && a.text === b.text;
}

function markLiteralIf(node: ts.Node, dead: Set<ts.Node>): void {
  if (!ts.isIfStatement(node)) return;
  if (isLiteralFalse(node.expression)) {
    addTree(node.thenStatement, dead);
  }
  if (isLiteralTrue(node.expression) && node.elseStatement) {
    addTree(node.elseStatement, dead);
  }
}

function markAfterExit(node: ts.Node, dead: Set<ts.Node>): void {
  if (!ts.isBlock(node) && !ts.isSourceFile(node)) return;
  const statements = ts.isBlock(node)
    ? node.statements
    : node.statements;
  let gone = false;
  for (const stmt of statements) {
    if (gone) addTree(stmt, dead);
    if (isExit(stmt)) gone = true;
  }
}

function isExit(node: ts.Statement): boolean {
  return ts.isReturnStatement(node) || ts.isThrowStatement(node);
}

function addTree(node: ts.Node, dead: Set<ts.Node>): void {
  dead.add(node);
  ts.forEachChild(node, (child) => addTree(child, dead));
}
