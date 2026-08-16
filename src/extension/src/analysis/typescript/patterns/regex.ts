import ts from 'typescript';

const Meta = /[\\^$.*+?()[\]{}|]/;

export function regexSource(node: ts.Node): string | undefined {
  if (ts.isRegularExpressionLiteral(node)) {
    const text = node.text;
    const last = text.lastIndexOf('/');
    return last > 0 ? text.slice(1, last) : undefined;
  }
  if (ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node)) {
    return node.text;
  }
  return undefined;
}

export function isTrivialRegex(source: string): boolean {
  return source.length > 0 && !Meta.test(source);
}

export function isRegexCall(name: string): boolean {
  return name === 'match' || name === 'matchAll' || name === 'replace'
    || name === 'replaceAll' || name === 'search' || name === 'split'
    || name === 'test' || name === 'exec';
}

export function regexArgument(
  node: ts.CallExpression,
): ts.Expression | undefined {
  const name = ts.isPropertyAccessExpression(node.expression)
    ? node.expression.name.text
    : ts.isIdentifier(node.expression)
      ? node.expression.text
      : '';
  if (name === 'RegExp' || name === 'test' || name === 'exec') {
    return node.arguments[0];
  }
  if (isRegexCall(name)) return node.arguments[0];
  return undefined;
}

export function regexPattern(
  node: ts.CallExpression,
): ts.Expression | undefined {
  if (ts.isPropertyAccessExpression(node.expression)) {
    const recv = node.expression.expression;
    if (ts.isRegularExpressionLiteral(recv)) return recv;
    const name = node.expression.name.text;
    if (name === 'test' || name === 'exec') return recv;
  }
  return regexArgument(node);
}

export function callIsTrivialRegex(node: ts.CallExpression): boolean {
  const pattern = regexPattern(node);
  if (!pattern) return false;
  const source = regexSource(pattern);
  return source !== undefined && isTrivialRegex(source);
}

export function callIsRegexUse(node: ts.CallExpression): boolean {
  const name = ts.isPropertyAccessExpression(node.expression)
    ? node.expression.name.text
    : ts.isIdentifier(node.expression)
      ? node.expression.text
      : '';
  if (name === 'RegExp' || name === 'test' || name === 'exec') return true;
  if (!isRegexCall(name)) return false;
  const pattern = regexPattern(node);
  return !!pattern && (
    ts.isRegularExpressionLiteral(pattern)
    || ts.isNewExpression(pattern)
  );
}

export function newIsTrivialRegex(node: ts.NewExpression): boolean {
  if (node.expression.getText() !== 'RegExp') return false;
  const arg = node.arguments?.[0];
  if (!arg) return false;
  const source = regexSource(arg);
  return source !== undefined && isTrivialRegex(source);
}
