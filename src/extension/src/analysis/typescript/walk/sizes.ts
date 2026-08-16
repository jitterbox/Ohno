import ts from 'typescript';
import { builtinTypeName } from '../catalog/builtins';
import { call, One, variable, type ComplexityExpression } from '../engine';

const DimNames = ['n', 'm', 'k', 'p', 'q'];

export interface SizeState {
  checker: ts.TypeChecker;
  dims: { variable: string; meaning: string }[];
}

export function createSizeState(checker: ts.TypeChecker): SizeState {
  return { checker, dims: [] };
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
  const type = state.checker.getTypeAtLocation(node);
  if (isOpaqueType(type) && !sizedTypeName(type)) {
    return call('iterate');
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
  const existing = state.dims.find((d) => d.meaning === meaning);
  if (existing) return variable(existing.variable);
  const name = DimNames[state.dims.length] ?? `n${state.dims.length}`;
  state.dims.push({ variable: name, meaning });
  return variable(name);
}

export function sizeOfReceiver(
  state: SizeState,
  node: ts.LeftHandSideExpression,
): ComplexityExpression {
  const type = state.checker.getTypeAtLocation(node);
  if (isOpaqueType(type)) return call('iterate');
  if (!sizedTypeName(type) && !looksLikeArrayLiteral(node)) {
    return One;
  }
  return namedDimension(state, node.getText());
}

function looksLikeArrayLiteral(node: ts.Node): boolean {
  return ts.isArrayLiteralExpression(node)
    || (ts.isNewExpression(node)
      && node.expression.getText() === 'Array');
}
