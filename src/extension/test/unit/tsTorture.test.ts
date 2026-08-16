import { describe, expect, it } from 'vitest';
import {
  analyzeFile,
  byName,
  parseExpected,
} from './commentHarness';

const files = [
  {
    rel: 'samples/typescript/TsTorture.ts',
    uri: 'file:///TsTorture.ts',
  },
  {
    rel: 'samples/javascript/JsTorture.js',
    uri: 'file:///JsTorture.js',
  },
  {
    rel: 'samples/typescript/TsRanking.ts',
    uri: 'file:///TsRanking.ts',
  },
];

describe.each(files)('torture $rel', ({ rel, uri }) => {
  const { text, functions } = analyzeFile(rel, uri);
  const cases = parseExpected(text);

  it('parses a large unique corpus', () => {
    expect(cases.length).toBeGreaterThan(20);
  });

  it.each(cases)('$name is $time / $space', ({ name, time, space }) => {
    const found = byName(functions, name);
    expect(found, name).toBeDefined();
    expect(found!.time, `${name} time`).toBe(time);
    expect(found!.space, `${name} space`).toBe(space);
  });
});

describe('torture corpus size', () => {
  it('reaches about 100 unique expected cases', () => {
    const ts = parseExpected(
      analyzeFile(
        'samples/typescript/TsTorture.ts',
        'file:///TsTorture.ts',
      ).text,
    );
    const js = parseExpected(
      analyzeFile(
        'samples/javascript/JsTorture.js',
        'file:///JsTorture.js',
      ).text,
    );
    const names = new Set([
      ...ts.map((c) => c.name),
      ...js.map((c) => c.name),
    ]);
    expect(names.size).toBeGreaterThanOrEqual(90);
  });
});
