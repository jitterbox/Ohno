import ts from 'typescript';
import {
  mul,
  One,
  peak,
  type ComplexityExpression,
} from '../engine';
import type { BindKey, SizeState } from './sizes';

export interface Cardinality {
  seed: ComplexityExpression;
  current: ComplexityExpression;
  max: ComplexityExpression;
}

export function collectLoopIndices(root: ts.Node): Set<string> {
  const names = new Set<string>();
  const visit = (node: ts.Node): void => {
    noteLoopIndex(node, names);
    ts.forEachChild(node, visit);
  };
  visit(root);
  return names;
}

export function noteLoopIndex(
  node: ts.Node,
  names: Set<string>,
): void {
  addForIndex(node, names);
}

export function emptyCard(): Cardinality {
  return { seed: One, current: One, max: One };
}

export function cardOf(state: SizeState, name: BindKey): Cardinality {
  const existing = state.cards.get(name);
  if (existing) return existing;
  const created = emptyCard();
  state.cards.set(name, created);
  return created;
}

export function seedCard(
  state: SizeState,
  name: BindKey,
  size: ComplexityExpression,
): void {
  const card = cardOf(state, name);
  card.seed = size;
  card.current = size;
  card.max = peak([card.max, size]);
}

export function growCard(
  state: SizeState,
  name: BindKey,
  amount: ComplexityExpression,
): void {
  const card = cardOf(state, name);
  card.current = peak([card.current, amount]);
  card.max = peak([card.max, card.current]);
}

export function retainedSize(
  bound: ComplexityExpression,
  element: ComplexityExpression,
): ComplexityExpression {
  if (element.kind === 'const') return bound;
  return mul(bound, element);
}

function addForIndex(node: ts.Node, names: Set<string>): void {
  if (!ts.isForStatement(node)) return;
  const inc = incrementIdent(node.incrementor);
  if (inc) names.add(inc);
  const init = initIdent(node.initializer);
  if (init) names.add(init);
}

function incrementIdent(
  node: ts.Expression | undefined,
): string | undefined {
  if (!node) return undefined;
  if ((ts.isPrefixUnaryExpression(node)
    || ts.isPostfixUnaryExpression(node))
    && ts.isIdentifier(node.operand)) {
    return node.operand.text;
  }
  if (ts.isBinaryExpression(node) && ts.isIdentifier(node.left)) {
    return node.left.text;
  }
  return undefined;
}

function initIdent(
  node: ts.ForInitializer | undefined,
): string | undefined {
  if (!node) return undefined;
  if (ts.isVariableDeclarationList(node)) {
    const name = node.declarations[0]?.name;
    return name && ts.isIdentifier(name) ? name.text : undefined;
  }
  if (ts.isBinaryExpression(node) && ts.isIdentifier(node.left)) {
    return node.left.text;
  }
  return undefined;
}

