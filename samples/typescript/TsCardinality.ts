/**
 * Cardinality twins of samples/roslyn/RoslynCardinalityGaps.cs.
 * `// expected: TIME / SPACE` is asserted by the comment harness.
 */

class MinHeap {
  size = 0;
  push(_key: number, _value?: number): void {
    this.size++;
  }
  pop(): number {
    this.size--;
    return 0;
  }
}

// expected: O(n log n) / O(n)
export function Huffman(freqs: number[]): number {
  const heap = new MinHeap();
  for (const f of freqs) heap.push(f, f);
  let cost = 0;
  while (heap.size > 1) {
    const a = heap.pop();
    const b = heap.pop();
    const merged = a + b;
    cost += merged;
    heap.push(merged, merged);
  }
  return cost;
}

// expected: O(n log n) / O(n)
export function RunningMedian(nums: number[]): void {
  const low = new MinHeap();
  const high = new MinHeap();
  for (const x of nums) {
    low.push(x);
    high.push(low.pop());
    if (high.size > low.size) low.push(high.pop());
  }
}

// expected: O(m + n) / O(n)
export function StackDepthFirstCount(
  graph: number[][],
  start: number,
): number {
  const visited = new Array<boolean>(graph.length);
  const stack: number[] = [];
  visited[start] = true;
  stack.push(start);
  let count = 0;
  let qi = 0;
  while (qi < stack.length) {
    const node = stack[qi++];
    count++;
    for (const next of graph[node]) {
      if (visited[next]) continue;
      visited[next] = true;
      stack.push(next);
    }
  }
  return count;
}

// expected: O(unknown) / O(unknown)
export function BfsNoVisited(
  graph: number[][],
  start: number,
): number {
  const queue: number[] = [start];
  let count = 0;
  let qi = 0;
  while (qi < queue.length) {
    const node = queue[qi++];
    count++;
    for (const next of graph[node]) queue.push(next);
  }
  return count;
}

// expected: O(k n) / O(k)
export function WindowShift(values: number[], k: number): void {
  const window: number[] = [];
  for (const value of values) {
    window.push(value);
    if (window.length > k) window.shift();
  }
}

// expected: O(n) / O(n)
export function HeapifyFromEnumerable(values: number[]): number {
  const heap = new MinHeap();
  for (const v of values) heap.push(v);
  return heap.size;
}

// expected: O(n) / O(n)
export function UniqueSet(values: number[]): number {
  const seen = new Set<number>();
  for (const v of values) seen.add(v);
  return seen.size;
}

// expected: O(n) / O(n)
export function StringBuilderJoin(n: number): string {
  const parts: string[] = [];
  for (let i = 0; i < n; i++) parts.push(String(i));
  return parts.join('');
}

// expected: O(m n) / O(m)
export function ImmutableListBuild(values: number[]): number[] {
  let list: number[] = [];
  for (const v of values) list = [...list, v];
  return list;
}

// expected: O(n) / O(1)
export function TypedArrayScan(values: Int32Array): number {
  let sum = 0;
  for (const v of values) sum += v;
  return sum;
}

// expected: O(m + n) / O(m + n)
export function CollectionSpread(a: number[], b: number[]): number[] {
  return [...a, ...b];
}

// expected: O(log n) / O(1)
export function HalvingShift(n: number): number {
  let steps = 0;
  while (n > 0) {
    n >>= 1;
    steps++;
  }
  return steps;
}

// expected: O(1) / O(1)
export function UnreachableEnqueue(n: number): number[] {
  const q: number[] = [];
  if (false) q.push(n);
  return q;
}

// expected: O(n²) / O(1)
export function LoopIndexNotEmitted(n: number): number {
  let sum = 0;
  for (let i = 0; i < n; i++) {
    for (let j = 0; j < i; j++) sum++;
  }
  return sum;
}

function helperSum(values: number[]): number {
  let n = 0;
  for (const v of values) n += v;
  return n;
}

// expected: O(n) / O(1)
export function UsesHelper(values: number[]): number {
  return helperSum(values);
}

// expected: O(n) / O(1)
export function WhileCountUp(n: number): number {
  let i = 0;
  while (i < n) i++;
  return i;
}

// expected: O(n) / O(1)
export function WhileTrueBreakRank(n: number): number {
  let i = 0;
  while (true) {
    if (i >= n) break;
    i++;
  }
  return i;
}

// expected: O(log n) / O(1)
export function WhileHalveAssign(n: number): number {
  while (n > 1) n = Math.floor(n / 2);
  return n;
}

// expected: O(1) / O(1)
export function WhileLiteralCeiling(): number {
  let i = 0;
  while (i < 8) i++;
  return i;
}
