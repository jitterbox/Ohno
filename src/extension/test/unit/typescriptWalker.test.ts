import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { analyzeDocument } from '../../src/analysis/typescript/analyze';

const root = resolve(
  dirname(fileURLToPath(import.meta.url)),
  '../../../..',
);

function analyzeFile(rel: string, uri: string) {
  const text = readFileSync(resolve(root, rel), 'utf8');
  return analyzeDocument({
    uri,
    text,
    version: 1,
    tier: 'fast',
  });
}

function fn(rel: string, uri: string, name: string) {
  const result = analyzeFile(rel, uri);
  const found = result.functions.find((item) => item.name === name);
  expect(found, name).toBeDefined();
  return found!;
}

describe('TypeScript catalog', () => {
  const file = 'samples/typescript/TsBclCatalog.ts';
  const uri = 'file:///TsBclCatalog.ts';

  it('treats toSorted and sort as n log n', () => {
    expect(fn(file, uri, 'sortNums').time).toContain('n log n');
    expect(fn(file, uri, 'sortInPlace').time).toContain('n log n');
  });

  it('walks map as a loop, not a free O(n)', () => {
    expect(fn(file, uri, 'mapped').time).toMatch(/O\(n/);
  });

  it('catalogs Map.has as expected constant', () => {
    const result = fn(file, uri, 'hasKey');
    expect(result.time).toBe('O(1)');
    expect(result.confidence).not.toBe('high');
  });

  it('scans a string in linear time', () => {
    expect(fn(file, uri, 'mentions').time).toBe('O(n)');
  });
});

describe('TypeScript honesty', () => {
  const file = 'samples/typescript/TsHonesty.ts';
  const uri = 'file:///TsHonesty.ts';

  it('does not invent O(1) for a user get', () => {
    const result = fn(file, uri, 'userGet');
    expect(result.time).toContain('C(get)');
    expect(result.time).not.toBe('O(1)');
  });

  it('does not invent O(1) for any.sort', () => {
    const result = fn(file, uri, 'anyCall');
    expect(result.time).toContain('C(sort)');
  });

  it('marks for-await unknown', () => {
    const result = fn(file, uri, 'drain');
    expect(result.time).toBe('O(unknown)');
  });
});

describe('JavaScript untyped', () => {
  const file = 'samples/javascript/JsUntyped.js';
  const uri = 'file:///JsUntyped.js';

  it('costs an untyped sort as C(sort)', () => {
    expect(fn(file, uri, 'mysterySort').time).toContain('C(sort)');
  });

  it('catalogs a syntactic array sort', () => {
    expect(fn(file, uri, 'literalSort').time).toContain('n log n');
  });
});

describe('TypeScript loops', () => {
  const file = 'samples/typescript/TsLoops.ts';
  const uri = 'file:///TsLoops.ts';

  it('sizes for-of and counted loops from the array', () => {
    expect(fn(file, uri, 'contains').time).toBe('O(n)');
    expect(fn(file, uri, 'counted').time).toBe('O(n)');
  });
});
