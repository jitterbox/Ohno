import ts from 'typescript';
import type { FunctionKind, LineRange } from '../../types';

export interface CollectedFunction {
  name: string;
  kind: FunctionKind;
  node: ts.Node;
  body?: ts.Node;
  range: LineRange;
  signatureRange: LineRange;
}

export function collectFunctions(
  source: ts.SourceFile,
): CollectedFunction[] {
  const found: CollectedFunction[] = [];
  const visit = (node: ts.Node): void => {
    const collected = asFunction(node, source);
    if (collected) found.push(collected);
    ts.forEachChild(node, visit);
  };
  visit(source);
  return found;
}

function asFunction(
  node: ts.Node,
  source: ts.SourceFile,
): CollectedFunction | undefined {
  if (ts.isFunctionDeclaration(node) && node.name && node.body) {
    return make(node.name.text, 'method', node, node.body, source);
  }
  if (ts.isMethodDeclaration(node) && ts.isIdentifier(node.name)
    && node.body) {
    return make(node.name.text, 'method', node, node.body, source);
  }
  if (ts.isConstructorDeclaration(node) && node.body) {
    return make('constructor', 'constructor', node, node.body, source);
  }
  if (ts.isGetAccessorDeclaration(node) && ts.isIdentifier(node.name)
    && node.body) {
    return make(node.name.text, 'property', node, node.body, source);
  }
  if (ts.isSetAccessorDeclaration(node) && ts.isIdentifier(node.name)
    && node.body) {
    return make(node.name.text, 'property', node, node.body, source);
  }
  if (ts.isVariableDeclaration(node) && node.initializer
    && isFunctionLike(node.initializer) && ts.isIdentifier(node.name)) {
    return make(
      node.name.text,
      'lambda',
      node,
      node.initializer,
      source,
    );
  }
  return undefined;
}

function isFunctionLike(node: ts.Node): node is ts.FunctionLikeDeclaration {
  return ts.isArrowFunction(node) || ts.isFunctionExpression(node);
}

function make(
  name: string,
  kind: FunctionKind,
  node: ts.Node,
  body: ts.Node,
  source: ts.SourceFile,
): CollectedFunction {
  return {
    name,
    kind,
    node,
    body,
    range: rangeOf(node, source),
    signatureRange: signatureOf(node, source),
  };
}

export function rangeOf(node: ts.Node, source: ts.SourceFile): LineRange {
  const start = source.getLineAndCharacterOfPosition(node.getStart(source));
  const end = source.getLineAndCharacterOfPosition(node.getEnd());
  return {
    startLine: start.line,
    startCharacter: start.character,
    endLine: end.line,
    endCharacter: end.character,
  };
}

function signatureOf(node: ts.Node, source: ts.SourceFile): LineRange {
  const start = node.getStart(source);
  let end = start;
  if (ts.isFunctionDeclaration(node) || ts.isMethodDeclaration(node)
    || ts.isConstructorDeclaration(node)
    || ts.isGetAccessorDeclaration(node)
    || ts.isSetAccessorDeclaration(node)) {
    end = node.body?.getStart(source) ?? node.getEnd();
  } else if (ts.isVariableDeclaration(node)) {
    end = node.initializer?.getStart(source) ?? node.getEnd();
  } else {
    end = node.getEnd();
  }
  const a = source.getLineAndCharacterOfPosition(start);
  const b = source.getLineAndCharacterOfPosition(Math.max(start, end - 1));
  return {
    startLine: a.line,
    startCharacter: a.character,
    endLine: b.line,
    endCharacter: b.character,
  };
}

export function overlaps(a: LineRange, b: LineRange): boolean {
  const aStart = a.startLine * 1e6 + a.startCharacter;
  const bStart = b.startLine * 1e6 + b.startCharacter;
  const aEnd = a.endLine * 1e6 + a.endCharacter;
  const bEnd = b.endLine * 1e6 + b.endCharacter;
  return aStart <= bEnd && bStart <= aEnd;
}
