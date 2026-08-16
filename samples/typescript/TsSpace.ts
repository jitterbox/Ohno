/**
 * Space-pattern twins of samples/roslyn/RoslynSpaceComplexityPatterns.cs.
 * `// expected: TIME / SPACE` is asserted by the comment harness.
 */

// expected: O(n) / O(1)
export function ConstantSpace(values: number[]): number {
  let sum = 0;
  let max = 0;
  for (const value of values) {
    sum += value;
    if (value > max) max = value;
  }
  return sum + max;
}

// expected: O(n) / O(n)
export function LinearArray(n: number): number[] {
  return new Array<number>(n);
}

// expected: O(m + n) / O(m + n)
export function TwoIndependentArrays(m: number, n: number): void {
  const left = new Array<number>(m);
  const right = new Array<number>(n);
  left[0] = right[0] = 0;
}

// expected: O(m n) / O(m n)
export function RectangularMatrix(m: number, n: number): number[][] {
  return Array.from({ length: m }, () => new Array<number>(n));
}

// expected: O(n²) / O(n²)
export function SquareMatrix(n: number): number[][] {
  return Array.from({ length: n }, () => new Array<number>(n));
}

// expected: O(n²) / O(n)
export function RepeatedButNotRetained(n: number): void {
  for (let i = 0; i < n; i++) {
    const buffer = new Array<number>(n);
    buffer[0] = i;
  }
}

// expected: O(n) / O(n²)
export function RepeatedAndRetained(n: number): number[][] {
  const buffers: number[][] = [];
  for (let i = 0; i < n; i++) buffers.push(new Array<number>(n));
  return buffers;
}

// expected: O(k n) / O(k n)
export function TwoDimensionalTable(s: string, text: string): number {
  const dp: number[][] = [];
  for (let i = 0; i <= s.length; i++) {
    dp[i] = [];
    for (let j = 0; j <= text.length; j++) dp[i][j] = 0;
  }
  return dp[s.length][text.length];
}

// expected: O(n) / O(n)
export function UniqueSet(values: number[]): number {
  const seen = new Set<number>();
  for (const v of values) seen.add(v);
  return seen.size;
}
