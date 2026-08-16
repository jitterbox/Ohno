import ts from 'typescript';
import { builtinTypeName } from '../catalog/builtins';
import { call, One, variable, type ComplexityExpression } from '../engine';
import type { Cardinality } from './cardinality';

const DimNames = ['n', 'm', 'k', 'p', 'q'];
const Iterators = new Set(['values', 'keys', 'entries']);

export type BindKey = ts.Symbol | string;

export interface SizeState {
  checker: ts.TypeChecker;
  dims: { variable: string; meaning: string }[];
  heaps: Map<BindKey, ComplexityExpression>;
  loopIndices: Set<string>;
  cards: Map<BindKey, Cardinality>;
}

export function bindKey(
  checker: ts.TypeChecker,
  ident: ts.Identifier,
): BindKey {
  return checker.getSymbolAtLocation(ident) ?? ident.text;
}

export function createSizeState(checker: ts.TypeChecker): SizeState {
  return {
    checker,
    dims: [],
    heaps: new Map(),
    loopIndices: new Set(),
    cards: new Map(),
  };
}

export function sizedTypeName(type: ts.Type): string | undefined {
  if (type.flags & ts.TypeFlags.StringLike) return 'String';
  if (type.flags & (ts.TypeFlags.Any | ts.TypeFlags.Unknown)) {
    return undefined;
  }
  const name = typeNameOf(type);
  return name ? builtinTypeName(name) : undefined;
}

export function typeNameOf(type: ts.Type): string | undefined {
  const symbol = type.getSymbol() ?? type.aliasSymbol;
  const name = symbol?.getName();
  if (name) return name;
  if (type.flags & ts.TypeFlags.StringLike) return 'String';
  if (type.isUnion()) {
    for (const part of type.types) {
      const partName = typeNameOf(part);
      if (partName) return partName;
    }
  }
  return undefined;
}

export function isOpaqueType(type: ts.Type): boolean {
  return !!(type.flags & (ts.TypeFlags.Any | ts.TypeFlags.Unknown));
}

export function dimensionFor(
  state: SizeState,
  node: ts.Node,
  meaning: string,
): ComplexityExpression {
  const iterated = iteratorSize(state, node);
  if (iterated) return iterated;
  const lengthProp = objectLengthDim(state, node);
  if (lengthProp) return lengthProp;
  const type = state.checker.getTypeAtLocation(node);
  if (isOpaqueType(type) && !sizedTypeName(type)) {
    return call('iterate');
  }
  const fromLength = lengthDimension(state, node);
  if (fromLength) return fromLength;
  if (ts.isNumericLiteral(node)) {
    return One;
  }
  if (isNumberLike(type)) {
    const ident = numericIdent(node);
    if (ident) return namedDimension(state, ident);
  }
  if (!sizedTypeName(type) && !looksLikeArrayLiteral(node)) {
    return call('iterate');
  }
  return namedDimension(state, meaning);
}

export function namedDimension(
  state: SizeState,
  meaning: string,
): ComplexityExpression {
  if (state.loopIndices.has(meaning)) {
    const first = state.dims[0];
    if (first) return variable(first.variable);
    meaning = 'n';
  }
  const existing = state.dims.find((d) => d.meaning === meaning);
  if (existing) return variable(existing.variable);
  const prefer = aliasLetter(meaning);
  const used = new Set(state.dims.map((d) => d.variable));
  if (prefer && used.has(prefer) && meaning === prefer) {
    return variable(prefer);
  }
  const name = pickVar(state, meaning);
  state.dims.push({ variable: name, meaning });
  return variable(name);
}

function pickVar(state: SizeState, meaning: string): string {
  const used = new Set(state.dims.map((d) => d.variable));
  const prefer = aliasLetter(meaning);
  if (prefer && !used.has(prefer)) return prefer;
  if (prefer === 'n' && !used.has('k')) return 'k';
  const next = DimNames.find((d) => !used.has(d));
  return next ?? `n${state.dims.length}`;
}

function aliasLetter(meaning: string): string | undefined {
  const text = meaning.replace(/^\W+|\W+$/g, '');
  const key = text.split(/[^A-Za-z0-9]/)[0] ?? text;
  if (/^(k|lists)$/i.test(key)) return 'k';
  if (/^(m|amount|times|prerequisites|edges|queries)$/i.test(key)) {
    return 'm';
  }
  if (/^(n|nums|values|items|arr|prices|height|graph|freqs|numCourses|coins|s|strs|text|head|node|current)$/i.test(key)) {
    return 'n';
  }
  return undefined;
}

export function sizeOfReceiver(
  state: SizeState,
  node: ts.LeftHandSideExpression,
): ComplexityExpression {
  if (ts.isIdentifier(node)) {
    const key = bindKey(state.checker, node);
    if (state.heaps.has(key)) return state.heaps.get(key)!;
  }
  if (ts.isNewExpression(node)
    && node.expression.getText() === 'Array'
    && node.arguments?.[0]) {
    const arg = node.arguments[0];
    return dimensionFor(state, arg, arg.getText());
  }
  const type = state.checker.getTypeAtLocation(node);
  if (isOpaqueType(type)) return call('iterate');
  const sized = sizedTypeName(type);
  if (sized === 'MinHeap' && !heapBound(state, node)) {
    return One;
  }
  if (!sized && !looksLikeArrayLiteral(node)) {
    return One;
  }
  return namedDimension(state, node.getText());
}

function heapBound(
  state: SizeState,
  node: ts.LeftHandSideExpression,
): boolean {
  if (!ts.isIdentifier(node)) return false;
  return state.heaps.has(bindKey(state.checker, node));
}

function looksLikeArrayLiteral(node: ts.Node): boolean {
  return ts.isArrayLiteralExpression(node)
    || (ts.isNewExpression(node)
      && node.expression.getText() === 'Array');
}

export function isNumberLike(type: ts.Type): boolean {
  if (type.flags & (
    ts.TypeFlags.NumberLike
    | ts.TypeFlags.Number
    | ts.TypeFlags.NumberLiteral
  )) {
    return true;
  }
  return type.isUnion() && type.types.some(isNumberLike);
}

function numericIdent(node: ts.Node): string | undefined {
  if (ts.isIdentifier(node)) return node.text;
  if (ts.isParenthesizedExpression(node)) {
    return numericIdent(node.expression);
  }
  if (ts.isPrefixUnaryExpression(node)) {
    return numericIdent(node.operand);
  }
  if (ts.isBinaryExpression(node)) {
    return numericIdent(node.left) ?? numericIdent(node.right);
  }
  return undefined;
}

function objectLengthDim(
  state: SizeState,
  node: ts.Node,
): ComplexityExpression | undefined {
  if (!ts.isObjectLiteralExpression(node)) return undefined;
  for (const prop of node.properties) {
    if (!ts.isPropertyAssignment(prop)) continue;
    const key = ts.isIdentifier(prop.name)
      ? prop.name.text
      : ts.isStringLiteral(prop.name) ? prop.name.text : '';
    if (key !== 'length') continue;
    return dimensionFor(state, prop.initializer, prop.initializer.getText());
  }
  return undefined;
}

function iteratorSize(
  state: SizeState,
  node: ts.Node,
): ComplexityExpression | undefined {
  if (!ts.isCallExpression(node)) return undefined;
  if (!ts.isPropertyAccessExpression(node.expression)) return undefined;
  if (!Iterators.has(node.expression.name.text)) return undefined;
  const recv = node.expression.expression;
  const sized = sizedTypeName(state.checker.getTypeAtLocation(recv));
  if (sized !== 'Map' && sized !== 'Set') return undefined;
  return sizeOfReceiver(state, recv);
}

function lengthDimension(
  state: SizeState,
  node: ts.Node,
): ComplexityExpression | undefined {
  if (ts.isPropertyAccessExpression(node)
    && (node.name.text === 'length' || node.name.text === 'size')) {
    return sizeOfReceiver(state, node.expression);
  }
  if (ts.isParenthesizedExpression(node)) {
    return lengthDimension(state, node.expression);
  }
  if (ts.isBinaryExpression(node)) {
    return lengthDimension(state, node.left)
      ?? lengthDimension(state, node.right);
  }
  return undefined;
}
