import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { analyzeDocument } from '../../src/analysis/typescript/analyze';

const file = resolve(
  dirname(fileURLToPath(import.meta.url)),
  '../../../../samples/typescript/TsPatterns.ts',
);
const uri = 'file:///TsPatterns.ts';
const text = readFileSync(file, 'utf8');

function fn(name: string) {
  const result = analyzeDocument({
    uri, text, version: 1, tier: 'fast',
  });
  const found = result.functions.find((item) => item.name === name);
  expect(found, name).toBeDefined();
  return found!;
}

function hasPattern(name: string, id: string): boolean {
  return fn(name).patterns.some((p) => p.id === id);
}

describe('TypeScript patterns', () => {
  it('marks repeated string concat as quadratic', () => {
    expect(fn('grow').time).toBe('O(n²)');
    expect(hasPattern('grow', 'string-concat-loop')).toBe(true);
    expect(fn('templateGrow').time).toBe('O(n²)');
  });

  it('keeps a trivial regex linear and wipes a backtracking one', () => {
    expect(fn('scan').time).toBe('O(n)');
    expect(hasPattern('scan', 'regex-linear')).toBe(true);
    expect(fn('backtrack').time).toBe('O(unknown)');
    expect(hasPattern('backtrack', 'regex')).toBe(true);
  });

  it('bounds a visited worklist and wipes an unbounded refill', () => {
    expect(fn('drain').time).not.toBe('O(unknown)');
    expect(hasPattern('drain', 'graph-traversal')).toBe(true);
    expect(fn('refill').time).toBe('O(unknown)');
    expect(hasPattern('refill', 'unbounded-worklist')).toBe(true);
  });

  it('notes a sliding-window heap cap', () => {
    const result = fn('window');
    expect(result.confidenceReasons.some((r) => r.includes('length > k')))
      .toBe(true);
    expect(result.time).toMatch(/O\(/);
    expect(result.time).not.toBe('O(1)');
  });

  it('classifies linear and branching recursion', () => {
    expect(fn('walkDown').time).toBe('O(n)');
    expect(hasPattern('walkDown', 'linear-recurrence')).toBe(true);
    expect(fn('fib').time).toBe('O(2^n)');
    expect(hasPattern('fib', 'branching-recursion')).toBe(true);
    expect(fn('fib').approaches.length).toBeGreaterThan(0);
  });

  it('treats map as a loop even when used like JSX children', () => {
    expect(fn('List').time).toMatch(/O\(n/);
  });
});
