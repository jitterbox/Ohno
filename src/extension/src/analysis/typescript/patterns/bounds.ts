import ts from 'typescript';
import { unknown, type ComplexityExpression } from '../engine';
import {
  bindKey,
  namedDimension,
  type BindKey,
  type SizeState,
} from '../walk/sizes';
import {
  isIndexScan,
  isShrink,
  loopFacts,
  queueIdent,
  receiverName,
} from './facts';

export type WorklistKind = 'visit' | 'graph' | 'nodes' | 'unknown';

export interface BoundMaps {
  heaps: Map<BindKey, ComplexityExpression>;
  worklists: Map<BindKey, ComplexityExpression>;
  worklistKind: Map<BindKey, WorklistKind>;
  reasons: string[];
}

export function emptyBoundMaps(): BoundMaps {
  return {
    heaps: new Map(),
    worklists: new Map(),
    worklistKind: new Map(),
    reasons: [],
  };
}

export function detectBounds(
  root: ts.Node,
  sizes: SizeState,
): BoundMaps {
  const maps = emptyBoundMaps();
  const visit = (node: ts.Node): void => {
    noteBounds(node, sizes, maps);
    ts.forEachChild(node, visit);
  };
  visit(root);
  return maps;
}

export function noteBounds(
  node: ts.Node,
  sizes: SizeState,
  maps: BoundMaps,
): void {
  recordHeap(node, sizes, maps);
  recordWorklist(node, sizes, maps);
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
  const ident = access && ts.isIdentifier(access.expression)
    ? access.expression
    : undefined;
  if (!ident || !containsShrink(node.thenStatement, ident.text)) {
    return;
  }
  if (!ts.isIdentifier(cond.right)) return;
  const bound = namedDimension(sizes, cond.right.text);
  maps.heaps.set(bindKey(sizes.checker, ident), bound);
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
  const ident = queueIdent(node.expression);
  if (!ident) return;
  const queue = ident.text;
  const key = bindKey(sizes.checker, ident);
  const facts = loopFacts(node.statement);
  const indexScan = isIndexScan(node.expression);
  if (!facts.grows.has(queue)) return;
  if (!facts.shrinks.has(queue) && !indexScan) return;
  if (facts.successor) {
    maps.worklistKind.set(key, 'nodes');
    maps.reasons.push(
      'Worklist walks linked-list successors; iterations count nodes',
    );
    return;
  }
  if (facts.visited) {
    const kind: WorklistKind = facts.edges ? 'graph' : 'visit';
    maps.worklistKind.set(key, kind);
    maps.reasons.push(
      facts.edges
        ? 'Graph worklist counts vertices plus edges'
        : 'Worklist iterations follow the visited set, not the current length',
    );
    return;
  }
  if (facts.shrinkCount > facts.growCount) return;
  maps.worklistKind.set(key, 'unknown');
  maps.worklists.set(key, unknown('worklist'));
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
