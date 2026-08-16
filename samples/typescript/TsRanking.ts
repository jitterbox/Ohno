/**
 * Ranking-function torture after Phase 12. Comments are
 * `// expected: TIME / SPACE`. Tight only when a local step
 * proves a bound; Collatz-like updates stay unknown / C(iterate).
 */

// expected: O(n) / O(1)
export function WhilePlusEquals(n: number): number {
  let i = 0;
  while (i < n) i += 1;
  return i;
}

// expected: O(n) / O(1)
export function WhileAssignPlus(n: number): number {
  let i = 0;
  while (i < n) i = i + 1;
  return i;
}

// expected: O(n) / O(1)
export function WhilePrefixInc(n: number): number {
  let i = 0;
  while (i < n) ++i;
  return i;
}

// expected: O(n) / O(1)
export function WhileFlippedBound(n: number): number {
  let i = 0;
  while (n > i) i++;
  return i;
}

// expected: O(n) / O(1)
export function WhileLengthBound(values: number[]): number {
  let i = 0;
  while (i < values.length) i++;
  return i;
}

// expected: O(n) / O(1)
export function WhileParenCond(n: number): number {
  let i = 0;
  while ((i < n)) i++;
  return i;
}

// expected: O(n) / O(1)
export function WhileLeq(n: number): number {
  let i = 0;
  while (i <= n) i++;
  return i;
}

// expected: O(n) / O(1)
export function DoWhileCountUp(n: number): number {
  let i = 0;
  do {
    i++;
  } while (i < n);
  return i;
}

// expected: O(n) / O(1)
export function ForBodyInc(n: number): number {
  let sum = 0;
  for (let i = 0; i < n;) {
    sum += i;
    i++;
  }
  return sum;
}

// expected: O(n) / O(1)
export function ForEverBreak(n: number): number {
  let i = 0;
  for (;;) {
    if (i >= n) break;
    i++;
  }
  return i;
}

// expected: O(n) / O(1)
export function WhileTrueBreakLength(values: number[]): number {
  let i = 0;
  let sum = 0;
  while (true) {
    if (i >= values.length) break;
    sum += values[i];
    i++;
  }
  return sum;
}

// expected: O(n) / O(1)
export function WhileAndFlag(n: number, ok: boolean): number {
  let i = 0;
  while (i < n && ok) i++;
  return i;
}

// expected: O(n) / O(1)
export function WhileMinusEquals2(n: number): number {
  let i = n;
  while (i > 0) i -= 2;
  return i;
}

// expected: O(log n) / O(1)
export function WhileDoubleUp(n: number): number {
  let i = 1;
  let steps = 0;
  while (i < n) {
    i *= 2;
    steps++;
  }
  return steps;
}

// expected: O(log n) / O(1)
export function WhileAssignTimes(n: number): number {
  let i = 1;
  while (i < n) i = i * 2;
  return i;
}

// expected: O(log n) / O(1)
export function WhileShiftLeft(n: number): number {
  let i = 1;
  while (i < n) i <<= 1;
  return i;
}

// expected: O(log n) / O(1)
export function WhileAssignShift(n: number): number {
  while (n > 1) n = n >> 1;
  return n;
}

// expected: O(log n) / O(1)
export function WhileDivAssign(n: number): number {
  while (n > 1) n /= 2;
  return n;
}

// expected: O(log n) / O(1)
export function WhileUnsignedShift(n: number): number {
  while (n > 1) n >>>= 1;
  return n;
}

// expected: O(log n) / O(1)
export function WhileCeilHalf(n: number): number {
  while (n > 1) n = Math.ceil(n / 2);
  return n;
}

// expected: O(log n) / O(1)
export function WhileTruncHalf(n: number): number {
  while (n > 1) n = Math.trunc(n / 2);
  return n;
}

// expected: O(log n) / O(1)
export function WhileFloorParen(n: number): number {
  while (n > 1) n = Math.floor((n / 2));
  return n;
}

// expected: O(log n) / O(1)
export function WhileTrueBreakHalve(n: number): number {
  while (true) {
    if (n <= 1) break;
    n >>= 1;
  }
  return n;
}

// expected: O(log n) / O(1)
export function ForFloorHalf(n: number): number {
  let steps = 0;
  for (let i = n; i > 1; i = Math.floor(i / 2)) steps++;
  return steps;
}

// expected: O(log n) / O(1)
export function ForAssignTimes(n: number): number {
  let steps = 0;
  for (let i = 1; i < n; i = i * 2) steps++;
  return steps;
}

// expected: O(log n) / O(1)
export function WhileAliasHalve(n: number): number {
  let i = n;
  while (i > 1) i = Math.floor(i / 2);
  return i;
}

// expected: O(n) / O(1)
export function WhileSizeBound(items: Set<number>): number {
  let i = 0;
  while (i < items.size) i++;
  return i;
}

// expected: O(C(iterate)) / O(1)
export function WhileWrongWay(n: number): number {
  let i = 0;
  while (i < n) i--;
  return i;
}

// expected: O(C(iterate)) / O(1)
export function WhileMixedStep(n: number): number {
  let i = 1;
  while (i < n) {
    i++;
    i *= 2;
  }
  return i;
}

// expected: O(C(iterate)) / O(1)
export function WhileTwoBreaks(n: number, m: number): number {
  let i = 0;
  while (true) {
    if (i >= n) break;
    if (i >= m) break;
    i++;
  }
  return i;
}

// expected: O(C(iterate)) / O(1)
export function WhileNeq(n: number): number {
  let i = 0;
  while (i !== n) i++;
  return i;
}

// expected: O(C(iterate)) / O(1)
export function WhileBoundExpr(n: number): number {
  let i = 0;
  while (i < n + 1) i++;
  return i;
}

// expected: O(C(iterate)) / O(1)
export function WhileStepNested(n: number, m: number): number {
  let i = 0;
  while (i < n) {
    let j = 0;
    while (j < m) {
      i++;
      j++;
    }
  }
  return i;
}

// expected: O(C(iterate)) / O(1)
export function WhileTrueWrongBreak(n: number): number {
  let i = 0;
  while (true) {
    if (i < n) break;
    i++;
  }
  return i;
}

// expected: O(C(iterate)) / O(1)
export function WhilePlusIdent(n: number, k: number): number {
  let i = 0;
  while (i < n) i += k;
  return i;
}

// expected: O(C(iterate)) / O(1)
export function WhileOrBounds(n: number, m: number): number {
  let i = 0;
  let j = 0;
  while (i < n || j < m) i++;
  return i;
}

// expected: O(C(iterate)) / O(1)
export function ForEverNoBreak(n: number): number {
  let i = 0;
  for (;;) i = n;
  return i;
}
