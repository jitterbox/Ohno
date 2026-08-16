import ts from 'typescript';
import { namedDimension, type SizeState } from '../walk/sizes';
import { unknown, type ComplexityExpression } from '../engine';
import {
  isShrink,
  loopFacts,
  queueFromCondition,
  receiverName,
} from './facts';

export interface BoundMaps {
  heaps: Map<string, ComplexityExpression>;
  worklists: Map<string, ComplexityExpression>;
  reasons: string[];
}

export function detectBounds(
  root: ts.Node,
  sizes: SizeState,
): BoundMaps {
  const maps: BoundMaps = {
    heaps: new Map(),
    worklists: new Map(),
    reasons: [],
  };
  const visit = (node: ts.Node): void => {
    recordHeap(node, sizes, maps);
    recordWorklist(node, sizes, maps);
    ts.forEachChild(node, visit);
  };
  visit(root);
  return maps;
}

function recordHeap(
  node: ts.Node,
  sizes: SizeState,
  maps: BoundMaps,
): void {
  if (!ts.isIfStatement(node)) return;
  const cond = node.expression;
  if (!ts.isBinaryExpression(cond)) return;
  const op = cond.operatorToken.kind;
  if (op !== ts.SyntaxKind.GreaterThanToken
    && op !== ts.SyntaxKind.GreaterThanEqualsToken) {
    return;
  }
  const access = ts.isPropertyAccessExpression(cond.left)
    && (cond.left.name.text === 'length' || cond.left.name.text === 'size')
    ? cond.left
    : undefined;
  const queue = access
    ? receiverName(access.expression)
    : undefined;
  if (!queue || !containsShrink(node.thenStatement, queue)) return;
  const bound = ts.isIdentifier(cond.right)
    ? namedDimension(sizes, cond.right.text)
    : namedDimension(sizes, 'k');
  maps.heaps.set(queue, bound);
  maps.reasons.push(
    'Collection size is assumed bounded by a length > k + shift check',
  );
}

function recordWorklist(
  node: ts.Node,
  sizes: SizeState,
  maps: BoundMaps,
): void {
  if (!ts.isWhileStatement(node) && !ts.isDoStatement(node)) return;
  const queue = queueFromCondition(node.expression);
  if (!queue) return;
  const facts = loopFacts(node.statement);
  if (!facts.grows.has(queue) || !facts.shrinks.has(queue)) return;
  if (facts.visited) {
    maps.worklists.set(queue, namedDimension(sizes, 'visited'));
    maps.reasons.push(
      'Worklist iterations follow the visited set, not the current length',
    );
    return;
  }
  if (facts.shrinkCount > facts.growCount) return;
  maps.worklists.set(queue, unknown('worklist'));
  maps.reasons.push(
    'A refill worklist has no visit mark; iterations are not '
      + 'bounded by length',
  );
}

function containsShrink(body: ts.Node, queue: string): boolean {
  let found = false;
  const visit = (node: ts.Node): void => {
    if (ts.isCallExpression(node)
      && ts.isPropertyAccessExpression(node.expression)
      && isShrink(node.expression.name.text)
      && receiverName(node.expression.expression) === queue) {
      found = true;
    }
    ts.forEachChild(node, visit);
  };
  visit(body);
  return found;
}
