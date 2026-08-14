import { describe, expect, it } from 'vitest';
import {
  normalizeRange,
  rangeContains,
  rangesIntersect,
} from '../../src/ui/ranges';

describe('ranges', () => {
  it('treats a caret inside a span as intersecting', () => {
    const loop = {
      startLine: 2, startCharacter: 0, endLine: 8, endCharacter: 1,
    };
    const caret = {
      startLine: 4, startCharacter: 6, endLine: 4, endCharacter: 6,
    };
    expect(rangesIntersect(loop, caret)).toBe(true);
    expect(rangeContains(loop, 4, 6)).toBe(true);
  });

  it('intersects multi-line selections with nested spans', () => {
    const enqueue = {
      startLine: 4, startCharacter: 4, endLine: 4, endCharacter: 28,
    };
    const dequeue = {
      startLine: 6, startCharacter: 8, endLine: 6, endCharacter: 20,
    };
    const selection = {
      startLine: 4, startCharacter: 0, endLine: 6, endCharacter: 20,
    };
    expect(rangesIntersect(enqueue, selection)).toBe(true);
    expect(rangesIntersect(dequeue, selection)).toBe(true);
  });

  it('normalizes a reversed selection', () => {
    const range = normalizeRange(6, 10, 4, 2);
    expect(range).toEqual({
      startLine: 4,
      startCharacter: 2,
      endLine: 6,
      endCharacter: 10,
    });
  });

  it('does not intersect disjoint ranges', () => {
    const a = {
      startLine: 1, startCharacter: 0, endLine: 1, endCharacter: 5,
    };
    const b = {
      startLine: 2, startCharacter: 0, endLine: 2, endCharacter: 5,
    };
    expect(rangesIntersect(a, b)).toBe(false);
  });
});
