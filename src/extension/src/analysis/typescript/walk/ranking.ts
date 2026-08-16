import ts from 'typescript';
import { log, One, type ComplexityExpression } from '../engine';
import {
  isNumberLike,
  namedDimension,
  sizeOfReceiver,
  type SizeState,
} from './sizes';

interface RankCmp {
  ident: string;
  toward: 'up' | 'down';
  bound: ts.Expression;
}

export function rankingBound(
  node: ts.ForStatement | ts.WhileStatement | ts.DoStatement,
  sizes: SizeState,
): ComplexityExpression | undefined {
  const proof = rankingProof(node);
  if (!proof) return undefined;
  if (isLiteralCeiling(proof.cmp) && proof.step === 'linear') {
    return One;
  }
  const size = sizeOfBound(proof.cmp.bound, sizes, proof.cmp.ident);
  if (!size) return undefined;
  return proof.step === 'log' ? log(size) : size;
}

export function rankingProof(
  node: ts.ForStatement | ts.WhileStatement | ts.DoStatement,
): { cmp: RankCmp; step: 'linear' | 'log' } | undefined {
  const extra = ts.isForStatement(node) ? node.incrementor : undefined;
  const body = bodyOf(node);
  const cond = loopCondition(node);
  let cmps = cond && !isTrue(cond) ? comparesIn(unwrap(cond)) : [];
  if (cmps.length === 0) cmps = breakCompare(body);
  const proven: { cmp: RankCmp; step: 'linear' | 'log' }[] = [];
  for (const cmp of cmps) {
    const step = stepToward(body, cmp, extra);
    if (step) proven.push({ cmp, step });
  }
  return proven.length === 1 ? proven[0] : undefined;
}

function loopCondition(
  node: ts.ForStatement | ts.WhileStatement | ts.DoStatement,
): ts.Expression | undefined {
  if (ts.isForStatement(node)) return node.condition;
  return node.expression;
}

function bodyOf(
  node: ts.ForStatement | ts.WhileStatement | ts.DoStatement,
): ts.Node {
  return node.statement;
}

function comparesIn(cond: ts.Expression): RankCmp[] {
  if (ts.isBinaryExpression(cond)
    && cond.operatorToken.kind
      === ts.SyntaxKind.AmpersandAmpersandToken) {
    return [
      ...comparesIn(unwrap(cond.left)),
      ...comparesIn(unwrap(cond.right)),
    ];
  }
  return parseCompare(unwrap(cond));
}

function breakCompare(body: ts.Node): RankCmp[] {
  const hits: RankCmp[] = [];
  for (const stmt of statementsOf(body)) {
    if (!ts.isIfStatement(stmt) || stmt.elseStatement) continue;
    if (!isBareBreak(stmt.thenStatement)) continue;
    for (const cmp of parseCompare(unwrap(stmt.expression))) {
      hits.push({ ...cmp, toward: flip(cmp.toward) });
    }
  }
  return hits;
}

function parseCompare(cond: ts.Expression): RankCmp[] {
  if (!ts.isBinaryExpression(cond)) return [];
  const toward = towardOf(cond.operatorToken.kind);
  if (!toward) return [];
  const hits: RankCmp[] = [];
  if (ts.isIdentifier(cond.left)) {
    hits.push({
      ident: cond.left.text, toward, bound: cond.right,
    });
  }
  if (ts.isIdentifier(cond.right)) {
    hits.push({
      ident: cond.right.text,
      toward: flip(toward),
      bound: cond.left,
    });
  }
  return hits;
}

function towardOf(op: ts.SyntaxKind): 'up' | 'down' | undefined {
  if (op === ts.SyntaxKind.LessThanToken
    || op === ts.SyntaxKind.LessThanEqualsToken) {
    return 'up';
  }
  if (op === ts.SyntaxKind.GreaterThanToken
    || op === ts.SyntaxKind.GreaterThanEqualsToken) {
    return 'down';
  }
  return undefined;
}

function flip(toward: 'up' | 'down'): 'up' | 'down' {
  return toward === 'up' ? 'down' : 'up';
}

function stepToward(
  body: ts.Node,
  cmp: RankCmp,
  extra?: ts.Expression,
): 'linear' | 'log' | undefined {
  let linear = false;
  let logStep = false;
  let bad = false;
  const note = (kind: 'up' | 'down' | 'log-up' | 'log-down' | 'bad') => {
    if (kind === 'bad') bad = true;
    if (kind === 'up' && cmp.toward === 'up') linear = true;
    if (kind === 'down' && cmp.toward === 'down') linear = true;
    if (kind === 'log-up' && cmp.toward === 'up') logStep = true;
    if (kind === 'log-down' && cmp.toward === 'down') logStep = true;
    if (kind === 'up' && cmp.toward === 'down') bad = true;
    if (kind === 'down' && cmp.toward === 'up') bad = true;
  };
  const visit = (node: ts.Node): void => {
    if (isNestedLoop(node)) return;
    const kind = identStep(node, cmp.ident);
    if (kind) note(kind);
    ts.forEachChild(node, visit);
  };
  visit(body);
  if (extra) visit(extra);
  if (bad || linear === logStep) return undefined;
  return logStep ? 'log' : 'linear';
}

function identStep(
  node: ts.Node,
  ident: string,
): 'up' | 'down' | 'log-up' | 'log-down' | 'bad' | undefined {
  if ((ts.isPrefixUnaryExpression(node)
    || ts.isPostfixUnaryExpression(node))
    && ts.isIdentifier(node.operand)
    && node.operand.text === ident) {
    return node.operator === ts.SyntaxKind.PlusPlusToken ? 'up' : 'down';
  }
  if (!ts.isBinaryExpression(node) || !ts.isIdentifier(node.left)
    || node.left.text !== ident
    || !isAssignOp(node.operatorToken.kind)) {
    return undefined;
  }
  return assignStep(node, ident);
}

function isAssignOp(op: ts.SyntaxKind): boolean {
  return op === ts.SyntaxKind.EqualsToken
    || op === ts.SyntaxKind.PlusEqualsToken
    || op === ts.SyntaxKind.MinusEqualsToken
    || op === ts.SyntaxKind.SlashEqualsToken
    || op === ts.SyntaxKind.AsteriskEqualsToken
    || op === ts.SyntaxKind.GreaterThanGreaterThanEqualsToken
    || op === ts.SyntaxKind.GreaterThanGreaterThanGreaterThanEqualsToken
    || op === ts.SyntaxKind.LessThanLessThanEqualsToken;
}

function assignStep(
  node: ts.BinaryExpression,
  ident: string,
): 'up' | 'down' | 'log-up' | 'log-down' | 'bad' {
  const op = node.operatorToken.kind;
  if (op === ts.SyntaxKind.PlusEqualsToken && isPosLit(node.right)) {
    return 'up';
  }
  if (op === ts.SyntaxKind.MinusEqualsToken && isPosLit(node.right)) {
    return 'down';
  }
  if (op === ts.SyntaxKind.GreaterThanGreaterThanEqualsToken
    || op === ts.SyntaxKind.GreaterThanGreaterThanGreaterThanEqualsToken) {
    return 'log-down';
  }
  if (op === ts.SyntaxKind.SlashEqualsToken && isTwo(node.right)) {
    return 'log-down';
  }
  if (op === ts.SyntaxKind.AsteriskEqualsToken && isTwo(node.right)) {
    return 'log-up';
  }
  if (op === ts.SyntaxKind.LessThanLessThanEqualsToken) return 'log-up';
  if (op === ts.SyntaxKind.EqualsToken) return equalsStep(node.right, ident);
  return 'bad';
}

function equalsStep(
  value: ts.Expression,
  ident: string,
): 'up' | 'down' | 'log-up' | 'log-down' | 'bad' {
  const half = libHalf(value, ident);
  if (half) return 'log-down';
  if (!ts.isBinaryExpression(value)) return 'bad';
  const op = value.operatorToken.kind;
  const leftId = ts.isIdentifier(value.left) && value.left.text === ident;
  const rightId = ts.isIdentifier(value.right) && value.right.text === ident;
  if (op === ts.SyntaxKind.PlusToken && isPosLit(value.right) && leftId) {
    return 'up';
  }
  if (op === ts.SyntaxKind.MinusToken && isPosLit(value.right) && leftId) {
    return 'down';
  }
  if (op === ts.SyntaxKind.SlashToken && isTwo(value.right) && leftId) {
    return 'log-down';
  }
  if (isShiftOne(op) && isOne(value.right) && leftId) return 'log-down';
  if (op === ts.SyntaxKind.AsteriskToken && isTwo(value.right) && leftId) {
    return 'log-up';
  }
  if (leftId || rightId) return 'bad';
  return 'bad';
}

function isShiftOne(op: ts.SyntaxKind): boolean {
  return op === ts.SyntaxKind.GreaterThanGreaterThanToken
    || op === ts.SyntaxKind.GreaterThanGreaterThanGreaterThanToken;
}

function libHalf(value: ts.Expression, ident: string): boolean {
  if (!ts.isCallExpression(value)) return false;
  if (!ts.isPropertyAccessExpression(value.expression)) return false;
  if (!/^(floor|ceil|trunc)$/.test(value.expression.name.text)) {
    return false;
  }
  const arg = value.arguments[0];
  if (!arg) return false;
  const inner = unwrap(arg);
  if (!ts.isBinaryExpression(inner)) return false;
  return ts.isIdentifier(inner.left) && inner.left.text === ident
    && inner.operatorToken.kind === ts.SyntaxKind.SlashToken
    && isTwo(inner.right);
}

function sizeOfBound(
  bound: ts.Expression,
  sizes: SizeState,
  ident: string,
): ComplexityExpression | undefined {
  if (ts.isPropertyAccessExpression(bound)
    && (bound.name.text === 'length' || bound.name.text === 'size')) {
    return sizeOfReceiver(sizes, bound.expression);
  }
  if (ts.isIdentifier(bound)) return numberDim(sizes, bound);
  if (ts.isNumericLiteral(bound)) return namedDimension(sizes, ident);
  return undefined;
}

function numberDim(
  sizes: SizeState,
  ident: ts.Identifier,
): ComplexityExpression | undefined {
  const type = sizes.checker.getTypeAtLocation(ident);
  if (!isNumberLike(type)) return undefined;
  return namedDimension(sizes, ident.text);
}

function isLiteralCeiling(cmp: RankCmp): boolean {
  return cmp.toward === 'up' && ts.isNumericLiteral(cmp.bound);
}

function isTrue(node: ts.Expression): boolean {
  return node.kind === ts.SyntaxKind.TrueKeyword;
}

function unwrap(node: ts.Expression): ts.Expression {
  if (ts.isParenthesizedExpression(node)) return unwrap(node.expression);
  return node;
}

function statementsOf(body: ts.Node): readonly ts.Statement[] {
  if (ts.isBlock(body)) return body.statements;
  if (ts.isStatement(body)) return [body];
  return [];
}

function isBareBreak(node: ts.Statement): boolean {
  if (ts.isBreakStatement(node) && !node.label) return true;
  if (ts.isBlock(node) && node.statements.length === 1) {
    return isBareBreak(node.statements[0]);
  }
  return false;
}

function isNestedLoop(node: ts.Node): boolean {
  return ts.isForStatement(node) || ts.isForOfStatement(node)
    || ts.isWhileStatement(node) || ts.isDoStatement(node)
    || ts.isForInStatement(node);
}

function isPosLit(node: ts.Expression): boolean {
  return ts.isNumericLiteral(node) && Number(node.text) > 0;
}

function isTwo(node: ts.Expression): boolean {
  return ts.isNumericLiteral(node) && node.text === '2';
}

function isOne(node: ts.Expression): boolean {
  return ts.isNumericLiteral(node) && node.text === '1';
}
