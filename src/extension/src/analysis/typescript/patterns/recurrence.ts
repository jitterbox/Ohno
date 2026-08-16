import ts from 'typescript';
import {
  log,
  mul,
  ofCost,
  pow,
  variable,
  type ComposedCost,
} from '../engine';

type ArgKind = 'minus1' | 'minus2' | 'half' | 'other';

export interface RecurrenceHit {
  cost: ComposedCost;
  id: string;
}

export function tryRecurrence(
  name: string,
  body: ts.Node | undefined,
): RecurrenceHit | undefined {
  if (!body) return undefined;
  const calls = findSelfCalls(name, body);
  if (calls.length === 0) return undefined;
  const kinds = calls.map(argKind);
  if (isBranching(calls, kinds)) {
    return solved(
      name,
      pow({ kind: 'const', value: 2 }, variable('n')),
      variable('n'),
      'branching-recursion',
      'branching recursion',
    );
  }
  if (calls.length === 1 && kinds[0] === 'minus1') {
    return solved(
      name, variable('n'), variable('n'),
      'linear-recurrence', `${name}(n-1) linear recurrence`,
    );
  }
  if (calls.length === 2 && kinds.every((k) => k === 'half')) {
    const nLogN = mul(variable('n'), log(variable('n')));
    return solved(
      name, nLogN, variable('n'),
      'divide-and-conquer', `${name}: T(n)=2T(n/2)+O(n)`,
    );
  }
  return undefined;
}

function findSelfCalls(name: string, body: ts.Node): ts.CallExpression[] {
  const calls: ts.CallExpression[] = [];
  const visit = (node: ts.Node): void => {
    if (ts.isFunctionDeclaration(node) && node !== body) return;
    if (ts.isCallExpression(node) && calleeIs(node, name)) {
      calls.push(node);
    }
    ts.forEachChild(node, visit);
  };
  visit(body);
  return calls;
}

function calleeIs(node: ts.CallExpression, name: string): boolean {
  if (ts.isIdentifier(node.expression)) {
    return node.expression.text === name;
  }
  return ts.isPropertyAccessExpression(node.expression)
    && node.expression.name.text === name;
}

function argKind(call: ts.CallExpression): ArgKind {
  for (const arg of call.arguments) {
    const kind = classify(arg);
    if (kind !== 'other') return kind;
  }
  return 'other';
}

function classify(arg: ts.Expression): ArgKind {
  if (ts.isCallExpression(arg)
    && ts.isPropertyAccessExpression(arg.expression)
    && arg.expression.name.text === 'slice') {
    return 'half';
  }
  if (!ts.isBinaryExpression(arg)) return 'other';
  const op = arg.operatorToken.kind;
  const right = ts.isNumericLiteral(arg.right)
    ? Number(arg.right.text)
    : undefined;
  if (op === ts.SyntaxKind.MinusToken && right === 1) return 'minus1';
  if (op === ts.SyntaxKind.MinusToken && right === 2) return 'minus2';
  if (op === ts.SyntaxKind.SlashToken && right === 2) return 'half';
  return 'other';
}

function isBranching(
  calls: ts.CallExpression[],
  kinds: ArgKind[],
): boolean {
  if (calls.length < 2) return false;
  if (calls.every(inExclusiveBranch)) return false;
  return kinds.every((k) => k === 'minus1' || k === 'minus2');
}

function inExclusiveBranch(call: ts.CallExpression): boolean {
  let node: ts.Node | undefined = call.parent;
  while (node) {
    if (ts.isIfStatement(node)) return true;
    if (ts.isFunctionLike(node)) break;
    node = node.parent;
  }
  return false;
}

function solved(
  name: string,
  time: ComposedCost['time'],
  space: ComposedCost['space'],
  id: string,
  label: string,
): RecurrenceHit {
  return {
    id,
    cost: ofCost(time, space, 'recursion', `${name}: ${label}`, undefined, 'medium'),
  };
}
