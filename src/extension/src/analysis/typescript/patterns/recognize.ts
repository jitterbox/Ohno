import ts from 'typescript';
import type { RecognizedPattern } from '../engine';
import { rangeOf } from '../walk/functions';
import { loopFacts, queueFromCondition } from './facts';
import { annotatePattern, unknownPattern } from './make';
import {
  callIsRegexUse,
  callIsTrivialRegex,
  newIsTrivialRegex,
} from './regex';

export function recognize(
  root: ts.Node,
  source: ts.SourceFile,
  checker: ts.TypeChecker,
): RecognizedPattern[] {
  const hits: RecognizedPattern[] = [];
  walk(root, source, checker, false, hits);
  const seen = new Set<string>();
  return hits.filter((p) => {
    if (seen.has(p.id)) return false;
    seen.add(p.id);
    return true;
  });
}

function walk(
  node: ts.Node,
  source: ts.SourceFile,
  checker: ts.TypeChecker,
  inLoop: boolean,
  hits: RecognizedPattern[],
): void {
  const hit = match(node, source, checker, inLoop);
  if (hit) hits.push(hit);
  const nested = inLoop || isLoop(node);
  ts.forEachChild(node, (child) => {
    walk(child, source, checker, nested, hits);
  });
}

function match(
  node: ts.Node,
  source: ts.SourceFile,
  checker: ts.TypeChecker,
  inLoop: boolean,
): RecognizedPattern | undefined {
  const span = rangeOf(node, source);
  return stringConcat(node, checker, inLoop, span)
    ?? regex(node, span)
    ?? awaitFor(node, span)
    ?? worklist(node, span)
    ?? anyDispatch(node, checker, span);
}

function stringConcat(
  node: ts.Node,
  checker: ts.TypeChecker,
  inLoop: boolean,
  span: ReturnType<typeof rangeOf>,
): RecognizedPattern | undefined {
  if (!inLoop) return undefined;
  if (isStringPlusEquals(node, checker) || isTemplateGrow(node)) {
    return annotatePattern(
      'string-concat-loop',
      'Repeated string concatenation',
      'each concatenation copies a growing string',
      span,
    );
  }
  return undefined;
}

function isStringPlusEquals(
  node: ts.Node,
  checker: ts.TypeChecker,
): boolean {
  if (!ts.isBinaryExpression(node)) return false;
  if (node.operatorToken.kind !== ts.SyntaxKind.PlusEqualsToken
    && node.operatorToken.kind !== ts.SyntaxKind.EqualsToken) {
    return false;
  }
  if (node.operatorToken.kind === ts.SyntaxKind.EqualsToken
    && !growsSameString(node)) {
    return false;
  }
  const type = checker.getTypeAtLocation(node.left);
  return !!(type.flags & ts.TypeFlags.StringLike);
}

function growsSameString(node: ts.BinaryExpression): boolean {
  if (!ts.isIdentifier(node.left)) return false;
  const name = node.left.text;
  if (ts.isTemplateExpression(node.right)) {
    return node.right.head.text.length >= 0
      && mentionsIdent(node.right, name);
  }
  return ts.isBinaryExpression(node.right)
    && node.right.operatorToken.kind === ts.SyntaxKind.PlusToken
    && mentionsIdent(node.right, name);
}

function mentionsIdent(node: ts.Node, name: string): boolean {
  if (ts.isIdentifier(node) && node.text === name) return true;
  return node.getChildren().some((c) => mentionsIdent(c, name));
}

function isTemplateGrow(node: ts.Node): boolean {
  return ts.isBinaryExpression(node)
    && node.operatorToken.kind === ts.SyntaxKind.EqualsToken
    && ts.isIdentifier(node.left)
    && ts.isTemplateExpression(node.right)
    && mentionsIdent(node.right, node.left.text);
}

function regex(
  node: ts.Node,
  span: ReturnType<typeof rangeOf>,
): RecognizedPattern | undefined {
  if (ts.isRegularExpressionLiteral(node)) return undefined;
  if (ts.isNewExpression(node) && node.expression.getText() === 'RegExp') {
    if (newIsTrivialRegex(node)) {
      return annotatePattern(
        'regex-linear',
        'Literal regular expression',
        'the pattern is a trivial literal, so the scan is linear',
        span,
      );
    }
    return unknownPattern(
      'regex',
      'Regular expression',
      'matching cost depends on the pattern and can backtrack',
      span,
    );
  }
  if (!ts.isCallExpression(node)) return undefined;
  if (!callIsRegexUse(node) && callName(node) !== 'RegExp') {
    return undefined;
  }
  if (callIsTrivialRegex(node)) {
    return annotatePattern(
      'regex-linear',
      'Literal regular expression',
      'the pattern is a trivial literal, so the scan is linear',
      span,
    );
  }
  return unknownPattern(
    'regex',
    'Regular expression',
    'matching cost depends on the pattern and can backtrack',
    span,
  );
}

function awaitFor(
  node: ts.Node,
  span: ReturnType<typeof rangeOf>,
): RecognizedPattern | undefined {
  if (ts.isForOfStatement(node) && node.awaitModifier) {
    return unknownPattern(
      'await-foreach',
      'Awaited sequence',
      'the async sequence cost is not local',
      span,
    );
  }
  if (ts.isAwaitExpression(node)) {
    return annotatePattern(
      'await-opaque',
      'Awaited work',
      'the awaited operation\'s cost is not the local continuation',
      span,
    );
  }
  return undefined;
}

function worklist(
  node: ts.Node,
  span: ReturnType<typeof rangeOf>,
): RecognizedPattern | undefined {
  if (!ts.isWhileStatement(node) && !ts.isDoStatement(node)) {
    return undefined;
  }
  const queue = queueFromCondition(node.expression);
  if (!queue) return undefined;
  const facts = loopFacts(node.statement);
  if (!facts.grows.has(queue) || !facts.shrinks.has(queue)) {
    return undefined;
  }
  if (facts.visited) {
    return annotatePattern(
      'graph-traversal',
      'Visited worklist',
      'iterations follow the visited set, not the current length',
      span,
    );
  }
  if (facts.shrinkCount > facts.growCount) return undefined;
  return unknownPattern(
    'unbounded-worklist',
    'Unbounded worklist',
    'the queue is refilled without a visit mark and may not halt',
    span,
  );
}

function anyDispatch(
  node: ts.Node,
  checker: ts.TypeChecker,
  span: ReturnType<typeof rangeOf>,
): RecognizedPattern | undefined {
  if (!ts.isCallExpression(node)) return undefined;
  if (!ts.isPropertyAccessExpression(node.expression)
    && !ts.isIdentifier(node.expression)) {
    return undefined;
  }
  const recv = ts.isPropertyAccessExpression(node.expression)
    ? node.expression.expression
    : undefined;
  if (!recv) return undefined;
  const type = checker.getTypeAtLocation(recv);
  if (!(type.flags & (ts.TypeFlags.Any | ts.TypeFlags.Unknown))) {
    return undefined;
  }
  return unknownPattern(
    'interface-dispatch',
    'Untyped dispatch',
    'the receiver is any or unknown, so the target is not fixed',
    span,
  );
}

function isLoop(node: ts.Node): boolean {
  return ts.isForStatement(node) || ts.isForOfStatement(node)
    || ts.isWhileStatement(node) || ts.isDoStatement(node)
    || ts.isForInStatement(node);
}

function callName(node: ts.CallExpression): string {
  if (ts.isIdentifier(node.expression)) return node.expression.text;
  if (ts.isPropertyAccessExpression(node.expression)) {
    return node.expression.name.text;
  }
  return '';
}
