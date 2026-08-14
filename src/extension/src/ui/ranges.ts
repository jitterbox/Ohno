import type { LineRange } from '../analysis/types';

export function comparePosition(
  line: number,
  character: number,
  otherLine: number,
  otherCharacter: number,
): number {
  if (line !== otherLine) return line < otherLine ? -1 : 1;
  if (character === otherCharacter) return 0;
  return character < otherCharacter ? -1 : 1;
}

export function rangeContains(
  range: LineRange,
  line: number,
  character: number,
): boolean {
  const afterStart = comparePosition(
    line, character, range.startLine, range.startCharacter,
  ) >= 0;
  const beforeEnd = comparePosition(
    line, character, range.endLine, range.endCharacter,
  ) <= 0;
  return afterStart && beforeEnd;
}

export function rangesIntersect(a: LineRange, b: LineRange): boolean {
  const aFirst = comparePosition(
    a.startLine, a.startCharacter, b.startLine, b.startCharacter,
  ) <= 0;
  const left = aFirst ? a : b;
  const right = aFirst ? b : a;
  return comparePosition(
    left.endLine,
    left.endCharacter,
    right.startLine,
    right.startCharacter,
  ) >= 0;
}

export function normalizeRange(
  startLine: number,
  startCharacter: number,
  endLine: number,
  endCharacter: number,
): LineRange {
  const reversed = comparePosition(
    startLine, startCharacter, endLine, endCharacter,
  ) > 0;
  return reversed
    ? {
      startLine: endLine,
      startCharacter: endCharacter,
      endLine: startLine,
      endCharacter: startCharacter,
    }
    : { startLine, startCharacter, endLine, endCharacter };
}

export function rangeSize(range: LineRange): number {
  if (range.startLine === range.endLine) {
    return range.endCharacter - range.startCharacter;
  }
  return (range.endLine - range.startLine) * 1000
    + range.endCharacter
    + (1000 - range.startCharacter);
}
