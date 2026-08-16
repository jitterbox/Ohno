import ts from 'typescript';
import { log, type ComplexityExpression } from '../engine';
import { namedDimension, type SizeState } from './sizes';

export function binarySearchBound(
  node: ts.WhileStatement | ts.DoStatement,
  sizes: SizeState,
): ComplexityExpression | undefined {
  if (!twoIdCompare(unwrapAnd(node.expression))) return undefined;
  const text = node.statement.getText();
  if (!text.includes('/ 2') && !text.includes('/2')
    && !text.includes('>> 1') && !text.includes('>>1')) {
    return undefined;
  }
  if (!/(mid|middle)/i.test(text)) return undefined;
  if (sizes.dims[0]) {
    return log({ kind: 'var', name: sizes.dims[0].variable });
  }
  return log(namedDimension(sizes, 'n'));
}

export function twoPointerBound(
  node: ts.WhileStatement | ts.DoStatement,
  sizes: SizeState,
): ComplexityExpression | undefined {
  if (!twoIdCompare(unwrapAnd(node.expression))) return undefined;
  const text = node.statement.getText();
  if (/(mid|middle)/i.test(text) && text.includes('/')) {
    return undefined;
  }
  if (!/\+\+|--|\+= 1|-= 1/.test(text)) return undefined;
  const cond = unwrapAnd(node.expression);
  if (ts.isBinaryExpression(cond)
    && ts.isIdentifier(cond.left)
    && ts.isIdentifier(cond.right)) {
    const a = cond.left.text;
    const b = cond.right.text;
    const moves = new RegExp(`\\b(${a}|${b})\\s*(\\+\\+|--)`);
    if (!moves.test(text)) return undefined;
  }
  if (sizes.dims[0]) {
    return { kind: 'var', name: sizes.dims[0].variable };
  }
  return namedDimension(sizes, 'n');
}

export function numericParamBound(
  condition: ts.Expression,
  sizes: SizeState,
  skip?: string,
): ComplexityExpression | undefined {
  if (!ts.isBinaryExpression(condition)) return undefined;
  if (ts.isPropertyAccessExpression(condition.left)
    || ts.isPropertyAccessExpression(condition.right)) {
    return undefined;
  }
  const name = ts.isIdentifier(condition.right)
    ? condition.right.text
    : ts.isIdentifier(condition.left)
      ? condition.left.text
      : undefined;
  if (!name || name === skip) return undefined;
  return namedDimension(sizes, name);
}

export function countdownBound(
  node: ts.WhileStatement | ts.DoStatement,
  sizes: SizeState,
): ComplexityExpression | undefined {
  const name = countdownName(node.expression);
  if (!name) return undefined;
  const text = node.statement.getText();
  const dec = new RegExp(`${name}\\s*--|-=\\s*1`);
  const half = new RegExp(`${name}\\s*([/]=\\s*2|>>=\\s*1)`);
  if (half.test(text)) return log(namedDimension(sizes, name));
  if (dec.test(text) && !/[*=]|3\\s*\\*/.test(text)) {
    return namedDimension(sizes, name);
  }
  return undefined;
}

export function isUnprovenCountdown(
  node: ts.WhileStatement | ts.DoStatement,
): boolean {
  const name = countdownName(node.expression);
  if (!name) return false;
  const text = node.statement.getText();
  if (new RegExp(`${name}\\s*=`).test(text)
    && !new RegExp(`${name}\\s*--|-=\\s*1|/=\\s*2|>>=\\s*1`).test(text)) {
    return true;
  }
  return /3\s*\*\s*/.test(text);
}

function countdownName(condition: ts.Expression): string | undefined {
  if (!ts.isBinaryExpression(condition)) return undefined;
  const ident = ts.isIdentifier(condition.left)
    ? condition.left.text
    : ts.isIdentifier(condition.right)
      ? condition.right.text
      : undefined;
  if (!ident) return undefined;
  const other = ts.isIdentifier(condition.left)
    ? condition.right
    : condition.left;
  if (!ts.isNumericLiteral(other) && !isZeroOne(other)) return undefined;
  return ident;
}

function isZeroOne(node: ts.Expression): boolean {
  return ts.isNumericLiteral(node)
    && (node.text === '0' || node.text === '1');
}

export function nullWalkBound(
  node: ts.WhileStatement | ts.DoStatement,
  sizes: SizeState,
): ComplexityExpression | undefined {
  if (!isChainCondition(node.expression)) return undefined;
  const text = node.statement.getText();
  if (!/\.next|\.Next/.test(text)) return undefined;
  return namedDimension(sizes, 'n');
}

function unwrapAnd(condition: ts.Expression): ts.Expression {
  if (ts.isBinaryExpression(condition)
    && condition.operatorToken.kind
      === ts.SyntaxKind.AmpersandAmpersandToken) {
    return unwrapAnd(condition.left);
  }
  return condition;
}

function twoIdCompare(condition: ts.Expression): boolean {
  if (!ts.isBinaryExpression(condition)) return false;
  return ts.isIdentifier(condition.left)
    && ts.isIdentifier(condition.right);
}

function isChainCondition(condition: ts.Expression): boolean {
  if (ts.isIdentifier(condition)) return true;
  if (!ts.isBinaryExpression(condition)) return false;
  const op = condition.operatorToken.kind;
  return op === ts.SyntaxKind.ExclamationEqualsEqualsToken
    || op === ts.SyntaxKind.ExclamationEqualsToken
    || op === ts.SyntaxKind.AmpersandAmpersandToken;
}
