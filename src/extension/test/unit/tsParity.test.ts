import { pathToFileURL } from 'node:url';
import { describe, expect, it } from 'vitest';
import { analyzeDocument } from '../../src/analysis/typescript/analyze';
import {
  analyzeFile,
  byName,
  parseExpected,
  repoPath,
} from './commentHarness';

const files = [
  {
    rel: 'samples/typescript/TsCardinality.ts',
    uri: 'file:///TsCardinality.ts',
  },
  {
    rel: 'samples/typescript/TsSpace.ts',
    uri: 'file:///TsSpace.ts',
  },
  {
    rel: 'samples/typescript/TsPatternsMore.ts',
    uri: 'file:///TsPatternsMore.ts',
  },
  {
    rel: 'samples/javascript/JsClosures.js',
    uri: 'file:///JsClosures.js',
  },
  {
    rel: 'samples/typescript/TsThis.ts',
    uri: 'file:///TsThis.ts',
  },
];

describe.each(files)('parity $rel', ({ rel, uri }) => {
  const { text, functions } = analyzeFile(rel, uri);
  const cases = parseExpected(text);

  it.each(cases)('$name is $time / $space', ({ name, time, space }) => {
    const found = byName(functions, name);
    expect(found, name).toBeDefined();
    expect(found!.time, `${name} time`).toBe(time);
    expect(found!.space, `${name} space`).toBe(space);
  });
});

describe('cardinality honesty', () => {
  it('does not emit loop indices as dimensions', () => {
    const { functions } = analyzeFile(
      'samples/typescript/TsCardinality.ts',
      'file:///TsCardinality.ts',
    );
    const found = byName(functions, 'LoopIndexNotEmitted');
    expect(found).toBeDefined();
    expect(found!.time).not.toMatch(/\bi\b/);
    expect(found!.time).not.toMatch(/\bj\b/);
    expect(found!.dimensions.every((d) => d.variable !== 'i')).toBe(true);
  });

  it('names cache-history and iterator-yield', () => {
    const { functions } = analyzeFile(
      'samples/typescript/TsPatternsMore.ts',
      'file:///TsPatternsMore.ts',
    );
    const cached = byName(functions, 'CachedGet');
    expect(cached?.patterns.some((p) => p.id === 'cache-history'))
      .toBe(true);
    const gen = byName(functions, 'YieldRange');
    expect(gen?.patterns.some((p) => p.id === 'iterator-yield'))
      .toBe(true);
  });
});

describe('same-program walk', () => {
  it('inlines a resolved helper from another file', () => {
    const abs = repoPath('samples/typescript/interop/root.ts');
    const text = analyzeFile(
      'samples/typescript/interop/root.ts',
      pathToFileURL(abs).href,
      'deep',
    );
    const found = byName(text.functions, 'Twice');
    expect(found, 'Twice').toBeDefined();
    expect(found!.time).toBe('O(n)');
    expect(found!.space).toBe('O(1)');
  });
});

describe('ad-hoc analyze still works', () => {
  it('returns functions for a buffer', () => {
    const result = analyzeDocument({
      uri: 'file:///inline.ts',
      text: 'export function id(n: number) { return n; }',
      version: 1,
      tier: 'fast',
    });
    expect(result.functions[0]?.time).toBe('O(1)');
  });

  it('collects a nameless default export', () => {
    const result = analyzeDocument({
      uri: 'file:///default.ts',
      text: [
        'export default function(xs: number[]) {',
        '  let s = 0;',
        '  for (const x of xs) s += x;',
        '  return s;',
        '}',
      ].join('\n'),
      version: 1,
      tier: 'fast',
    });
    const found = byName(result.functions, 'default');
    expect(found, 'default').toBeDefined();
    expect(found!.time).toBe('O(n)');
  });

  it('counts two helper calls as O(n), not O(1)', () => {
    const result = analyzeDocument({
      uri: 'file:///twice-helper.ts',
      text: [
        'function scan(xs: number[]) {',
        '  let s = 0;',
        '  for (const x of xs) s += x;',
        '  return s;',
        '}',
        'export function twice(a: number[], b: number[]) {',
        '  return scan(a) + scan(b);',
        '}',
      ].join('\n'),
      version: 1,
      tier: 'fast',
    });
    const found = byName(result.functions, 'twice');
    expect(found, 'twice').toBeDefined();
    expect(found!.time).toBe('O(n)');
  });

  it('collects object-literal methods', () => {
    const result = analyzeDocument({
      uri: 'file:///methods.ts',
      text: [
        'export const box = {',
        '  scan(xs: number[]) {',
        '    let s = 0;',
        '    for (const x of xs) s += x;',
        '    return s;',
        '  },',
        '  "sum": function(xs: number[]) { return xs.length; },',
        '};',
      ].join('\n'),
      version: 1,
      tier: 'fast',
    });
    expect(byName(result.functions, 'scan')?.time).toBe('O(n)');
    expect(byName(result.functions, 'sum')?.time).toBe('O(1)');
  });
});
