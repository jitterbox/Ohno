import { describe, expect, it } from 'vitest';
import {
  analyzeFile,
  byName,
  parseExpected,
} from './commentHarness';

const files = [
  {
    rel: 'samples/typescript/TsOptimalSolutions.ts',
    uri: 'file:///TsOptimalSolutions.ts',
  },
  {
    rel: 'samples/javascript/JsOptimalSolutions.js',
    uri: 'file:///JsOptimalSolutions.js',
  },
];

describe.each(files)('optimal solutions $rel', ({ rel, uri }) => {
  const { text, functions } = analyzeFile(rel, uri);
  const cases = parseExpected(text);

  it('parses expected comments', () => {
    expect(cases.length).toBeGreaterThan(10);
  });

  it.each(cases)('$name is $time / $space', ({ name, time, space }) => {
    const found = byName(functions, name);
    expect(found, name).toBeDefined();
    expect(found!.time, `${name} time`).toBe(time);
    expect(found!.space, `${name} space`).toBe(space);
  });
});
