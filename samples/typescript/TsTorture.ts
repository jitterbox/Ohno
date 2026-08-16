/**
 * Adversarial TypeScript cases that probe walker gaps.
 * `// expected: TIME / SPACE` is asserted by the comment harness.
 */

export class MinHeap<T> {
  private items: { key: number; value: T }[] = [];
  get size(): number { return this.items.length; }
  push(key: number, value: T): void { this.items.push({ key, value }); }
  pop(): T | undefined { return this.items.shift()?.value; }
  peek(): T | undefined { return this.items[0]?.value; }
}

export class ExpensiveHost {
  constructor(private readonly values: number[]) {}
  get value(): number {
    let sum = 0;
    for (const n of this.values) sum += n;
    return sum;
  }
}

export interface Runner {
  run(values: number[]): number;
}

// expected: O(C(run)) / O(1)
export function DynamicDispatch(target: any, n: number): unknown {
  return target.run(n);
}

// expected: O(1) / O(1)
export function ReflectionDispatch(
  target: object,
  key: string,
): unknown {
  return (target as Record<string, unknown>)[key];
}

// expected: O(C(run)) / O(1)
export function InterfaceDispatch(
  algorithm: Runner,
  values: number[],
): number {
  return algorithm.run(values);
}

// expected: O(n C(transform)) / O(1)
export function DelegateInsideLoop(
  values: number[],
  transform: (n: number) => number,
): number {
  let sum = 0;
  for (const n of values) sum += transform(n);
  return sum;
}

// expected: O(n) / O(1)
export function PropertyAccessLooksConstant(
  host: ExpensiveHost,
): number {
  return host.value;
}

// expected: O(m n) / O(1)
export function ForeachOverSlow(
  values: number[],
  host: ExpensiveHost,
): number {
  let sum = 0;
  for (const _ of values) sum += host.value;
  return sum;
}

// expected: O(C(then)) / O(1)
export function AwaitOpaqueWork(
  work: Promise<number>,
): Promise<number> {
  return work.then((n) => n + 1);
}

// expected: O(unknown) / O(1)
export async function ConsumeAsyncStream(
  items: AsyncIterable<number>,
): Promise<number> {
  let sum = 0;
  for await (const n of items) sum += n;
  return sum;
}

// expected: O(unknown) / O(1)
export function RegexBacktracking(s: string): boolean {
  return /(a+)+b/.test(s);
}

// expected: O(n) / O(1)
export function RegexLinear(s: string): boolean {
  return /abc/.test(s);
}

// expected: O(n log n) / O(1)
export function SortWithComparer(nums: number[]): number[] {
  return nums.sort((a, b) => a - b);
}

// expected: O(unknown) / O(1)
export function CollatzSteps(n: number): number {
  let steps = 0;
  while (n > 1) {
    n = n % 2 === 0 ? n / 2 : 3 * n + 1;
    steps++;
  }
  return steps;
}

// expected: O(2^n) / O(n)
export function DataDependentRecursion(n: number): number {
  if (n < 2) return n;
  return DataDependentRecursion(n - 1)
    + DataDependentRecursion(n - 2);
}

// expected: O(n) / O(1)
export function CountLinkedNodes(
  node: { next: unknown } | null,
): number {
  let n = 0;
  while (node) {
    n++;
    node = node.next as { next: unknown } | null;
  }
  return n;
}

// expected: O(n²) / O(1)
export function RepeatedStringConcat(values: string[]): string {
  let text = '';
  for (const part of values) text += part;
  return text;
}

// expected: O(unknown) / O(unknown)
export function BfsNoVisited(
  graph: number[][],
  start: number,
): number {
  const q = [start];
  let count = 0;
  while (q.length) {
    const node = q.shift()!;
    count++;
    for (const next of graph[node]) q.push(next);
  }
  return count;
}

// expected: O(m + n) / O(n)
export function StackDepthFirstCount(
  graph: number[][],
  start: number,
): number {
  const visited = new Array<boolean>(graph.length).fill(false);
  const stack = [start];
  visited[start] = true;
  let count = 0;
  while (stack.length) {
    const node = stack.pop()!;
    count++;
    for (const next of graph[node]) {
      if (visited[next]) continue;
      visited[next] = true;
      stack.push(next);
    }
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

// expected: O(n) / O(1)
export function WindowIndex(values: number[], k: number): number {
  let best = 0;
  let sum = 0;
  for (let i = 0; i < values.length; i++) {
    sum += values[i];
    if (i >= k) sum -= values[i - k];
    if (sum > best) best = sum;
  }
  return best;
}

// expected: O(n log k + n log n) / O(k + n)
export function RunningMedian(nums: number[]): number[] {
  const low = new MinHeap<number>();
  const high = new MinHeap<number>();
  const out: number[] = [];
  for (const x of nums) {
    low.push(-x, x);
    const moved = low.pop();
    if (moved !== undefined) high.push(moved, moved);
    if (high.size > low.size) {
      const back = high.pop();
      if (back !== undefined) low.push(-back, back);
    }
    const peek = low.peek();
    if (peek !== undefined) out.push(peek);
  }
  return out;
}

// expected: O(n) / O(n)
export function StringBuilderJoin(n: number): string {
  const parts: string[] = [];
  for (let i = 0; i < n; i++) parts.push(String(i));
  return parts.join('');
}

// expected: O(m + n) / O(m + n)
export function CollectionSpread(
  values: number[],
  extra: number[],
): number[] {
  return [...values, ...extra];
}

// expected: O(log n) / O(1)
export function HalvingShift(n: number): number {
  let steps = 0;
  for (let i = n; i > 1; i >>= 1) steps++;
  return steps;
}

// expected: O(1) / O(1)
export function UnreachablePush(flag: boolean): number[] {
  const out: number[] = [];
  if (flag && !flag) out.push(1);
  return out;
}

// expected: O(n²) / O(1)
export function LoopIndexNotEmitted(values: number[]): number {
  let n = 0;
  for (let i = 0; i < values.length; i++) {
    for (let j = 0; j < values.length; j++) n += values[i] + values[j];
  }
  return n;
}

// expected: O(C(eval)) / O(1)
export function EvalDispatch(s: string): unknown {
  return eval(s);
}

// expected: O(C(Function)) / O(1)
export function FunctionCtor(s: string): unknown {
  return new Function(s)();
}

// expected: O(C(iterate) + n) / O(n)
export function ForInKeys(values: number[]): number {
  let n = 0;
  const boxed = { ...values };
  for (const key in boxed) n += boxed[key as unknown as number] ?? 0;
  return n;
}

// expected: O(C(iterate) + n) / O(1)
export function GeneratorSum(values: number[]): number {
  function* walk() {
    for (const n of values) yield n;
  }
  let sum = 0;
  for (const n of walk()) sum += n;
  return sum;
}

// expected: O(n) / O(n)
export function MapGrow(values: number[]): Map<number, number> {
  const map = new Map<number, number>();
  for (const n of values) map.set(n, n);
  return map;
}

// expected: O(n) / O(n)
export function SetGrow(values: number[]): Set<number> {
  const set = new Set<number>();
  for (const n of values) set.add(n);
  return set;
}

// expected: O(m n) / O(m)
export function ConcatInLoop(values: number[]): number[] {
  let out: number[] = [];
  for (const n of values) out = out.concat(n);
  return out;
}

// expected: O(m n) / O(1)
export function UnshiftInLoop(values: number[]): number[] {
  const out: number[] = [];
  for (const n of values) out.unshift(n);
  return out;
}

// expected: O(n²) / O(1)
export function IndexOfNested(values: number[]): number {
  let hits = 0;
  for (const n of values) if (values.indexOf(n) >= 0) hits++;
  return hits;
}

// expected: O(n²) / O(1)
export function IncludesNested(values: number[]): number {
  let hits = 0;
  for (const n of values) if (values.includes(n)) hits++;
  return hits;
}

// expected: O(k + n) / O(1)
export function FilterMapChain(values: number[]): number[] {
  return values.filter((n) => n > 0).map((n) => n * 2);
}

// expected: O(n) / O(1)
export function ReduceBuild(values: number[]): number[] {
  return values.reduce<number[]>((acc, n) => {
    acc.push(n);
    return acc;
  }, []);
}

// expected: O(n) / O(1)
export function ObjectKeysLoop(
  map: Record<string, number>,
): number {
  let sum = 0;
  for (const key of Object.keys(map)) sum += map[key];
  return sum;
}

// expected: O(n) / O(n)
export function JsonRoundtrip(values: number[]): number[] {
  return JSON.parse(JSON.stringify(values)) as number[];
}

// expected: O(n) / O(1)
export function TypedArrayScan(values: Int32Array): number {
  let sum = 0;
  for (let i = 0; i < values.length; i++) sum += values[i];
  return sum;
}

// expected: O(n) / O(1)
export function OptionalChainLoop(
  values: number[] | undefined,
): number {
  let sum = 0;
  for (const n of values ?? []) sum += n;
  return sum;
}

// expected: O(n) / O(1)
export function NullishBound(
  values: number[],
  n?: number,
): number {
  const limit = n ?? values.length;
  let sum = 0;
  for (let i = 0; i < limit; i++) sum += values[i] ?? 0;
  return sum;
}

// expected: O(n) / O(1)
export function DoWhileScan(values: number[]): number {
  if (values.length === 0) return 0;
  let i = 0;
  let sum = 0;
  do {
    sum += values[i++];
  } while (i < values.length);
  return sum;
}

// expected: O(n) / O(1)
export function SwitchInLoop(values: number[]): number {
  let sum = 0;
  for (const n of values) {
    switch (n) {
      case 0:
        sum += 1;
        break;
      default:
        sum += n;
    }
  }
  return sum;
}

// expected: O(n) / O(1)
export function TryCatchLoop(values: number[]): number {
  let sum = 0;
  for (const n of values) {
    try {
      sum += n;
    } catch {
      sum += 0;
    }
  }
  return sum;
}

// expected: O(n) / O(1)
export function LabeledBreak(values: number[]): number {
  let sum = 0;
  outer: for (const n of values) {
    if (n < 0) break outer;
    sum += n;
  }
  return sum;
}

// expected: O(n) / O(n)
export function MemoFib(n: number): number {
  const memo = new Array<number>(n + 1).fill(-1);
  const go = (k: number): number => {
    if (k < 2) return k;
    if (memo[k] >= 0) return memo[k];
    memo[k] = go(k - 1) + go(k - 2);
    return memo[k];
  };
  return go(n);
}

// expected: O(n) / O(1)
export function TailishWalk(n: number): number {
  let steps = 0;
  while (n > 0) {
    n--;
    steps++;
  }
  return steps;
}

// expected: O(n) / O(1)
export function RestReduce(...values: number[]): number {
  return values.reduce((a, b) => a + b, 0);
}

// expected: O(n) / O(1)
export function DestructureLoop(
  pairs: [number, number][],
): number {
  let sum = 0;
  for (const [a, b] of pairs) sum += a + b;
  return sum;
}

// expected: O(n) / O(1)
export function ComputedKeys(values: number[]): Record<string, number> {
  const out: Record<string, number> = {};
  for (const n of values) out[`k${n}`] = n;
  return out;
}

// expected: O(n C(iterate)) / O(C(iterate))
export function ObjectSpreadLoop(
  values: number[],
): Record<number, number> {
  let out: Record<number, number> = {};
  for (const n of values) out = { ...out, [n]: n };
  return out;
}

// expected: O(n) / O(n)
export function SliceCopyLoop(values: number[]): number[][] {
  const out: number[][] = [];
  for (let i = 0; i < values.length; i++) out.push(values.slice());
  return out;
}

// expected: O(m n log m) / O(1)
export function SortInLoopOnce(values: number[][]): number[][] {
  for (const row of values) row.sort((a, b) => a - b);
  return values;
}

// expected: O(n) / O(1)
export function BitwiseScan(values: number[]): number {
  let xor = 0;
  for (const n of values) xor ^= n;
  return xor;
}

// expected: O(n) / O(1)
export function CommaLoop(values: number[]): number {
  let i = 0;
  let sum = 0;
  for (; i < values.length; i++, sum += values[i - 1]);
  return sum;
}

// expected: O(C(iterate)) / O(1)
export function WhileTrueBreak(n: number): number {
  let i = 0;
  while (true) {
    if (i >= n) break;
    i++;
  }
  return i;
}

// expected: O(n log n) / O(1)
export function LocaleSort(values: string[]): string[] {
  return values.sort((a, b) => a.localeCompare(b));
}

// expected: O(C(encode)) / O(1)
export function TextEncoderBytes(s: string): Uint8Array {
  return new TextEncoder().encode(s);
}

// expected: O(k + m + n) / O(m + n)
export function SplitJoin(s: string): string {
  return s.split('').reverse().join('');
}

// expected: O(m n) / O(1)
export function StartsWithScan(values: string[], s: string): number {
  let hits = 0;
  for (const w of values) if (w.startsWith(s)) hits++;
  return hits;
}

// expected: O(k n) / O(1)
export function NestedParamLoops(n: number, k: number): number {
  let sum = 0;
  for (let i = 0; i < n; i++) {
    for (let j = 0; j < k; j++) sum++;
  }
  return sum;
}

// expected: O(n) / O(1)
export function ReverseIndex(values: number[]): number {
  let sum = 0;
  for (let i = values.length - 1; i >= 0; i--) sum += values[i];
  return sum;
}

// expected: O(log n) / O(1)
export function BinarySearchInsert(
  nums: number[],
  target: number,
): number {
  let lo = 0;
  let hi = nums.length;
  while (lo < hi) {
    const mid = lo + Math.floor((hi - lo) / 2);
    if (nums[mid] < target) lo = mid + 1;
    else hi = mid;
  }
  return lo;
}

// expected: O(n) / O(1)
export function TwoPointerSum(nums: number[], target: number): boolean {
  let left = 0;
  let right = nums.length - 1;
  while (left < right) {
    const sum = nums[left] + nums[right];
    if (sum === target) return true;
    if (sum < target) left++;
    else right--;
  }
  return false;
}

// expected: O(n) / O(n)
export function PrefixSums(values: number[]): number[] {
  const out = new Array<number>(values.length);
  let sum = 0;
  for (let i = 0; i < values.length; i++) {
    sum += values[i];
    out[i] = sum;
  }
  return out;
}

// expected: O(n) / O(1)
export function KadaneOnce(values: number[]): number {
  let best = values[0] ?? 0;
  let cur = 0;
  for (const n of values) {
    cur = Math.max(n, cur + n);
    best = Math.max(best, cur);
  }
  return best;
}

// expected: O(n) / O(1)
export function MajorityBoyer(values: number[]): number {
  let vote = 0;
  let cand = 0;
  for (const n of values) {
    if (vote === 0) cand = n;
    vote += n === cand ? 1 : -1;
  }
  return cand;
}

// expected: O(n) / O(1)
export function DutchFlag(nums: number[]): number[] {
  let lo = 0;
  let mid = 0;
  let hi = nums.length - 1;
  while (mid <= hi) {
    if (nums[mid] === 0) {
      [nums[lo], nums[mid]] = [nums[mid], nums[lo]];
      lo++;
      mid++;
    } else if (nums[mid] === 1) mid++;
    else {
      [nums[mid], nums[hi]] = [nums[hi], nums[mid]];
      hi--;
    }
  }
  return nums;
}

// expected: O(m C(iterate) + n) / O(C(iterate))
export function BucketCount(values: number[]): number[] {
  const freq = new Array<number>(101).fill(0);
  for (const n of values) freq[n]++;
  const out: number[] = [];
  for (let i = 0; i < freq.length; i++) {
    for (let k = 0; k < freq[i]; k++) out.push(i);
  }
  return out;
}

// expected: O(C(iterate)) / O(1)
export function GcdWalk(n: number, m: number): number {
  while (m) {
    const t = n % m;
    n = m;
    m = t;
  }
  return n;
}

// expected: O(log n) / O(1)
export function FastPow(n: number, k: number): number {
  let base = n;
  let exp = k;
  let out = 1;
  while (exp > 0) {
    if (exp & 1) out *= base;
    base *= base;
    exp >>= 1;
  }
  return out;
}

// expected: O(n) / O(n)
export function RotateExtra(values: number[], k: number): number[] {
  const n = values.length;
  const out = new Array<number>(n);
  for (let i = 0; i < n; i++) out[(i + k) % n] = values[i];
  return out;
}

// expected: O(n) / O(1)
export function InPlaceReverse(values: number[]): number[] {
  let left = 0;
  let right = values.length - 1;
  while (left < right) {
    [values[left], values[right]] = [values[right], values[left]];
    left++;
    right--;
  }
  return values;
}

// expected: O(n) / O(1)
export function RemoveDuplicates(nums: number[]): number {
  if (nums.length === 0) return 0;
  let w = 1;
  for (let i = 1; i < nums.length; i++) {
    if (nums[i] !== nums[w - 1]) nums[w++] = nums[i];
  }
  return w;
}

// expected: O(n) / O(1)
export function MoveZeroes(nums: number[]): void {
  let w = 0;
  for (let i = 0; i < nums.length; i++) {
    if (nums[i] !== 0) nums[w++] = nums[i];
  }
  while (w < nums.length) nums[w++] = 0;
}

// expected: O(m + n) / O(m + n)
export function IntersectHash(
  values: number[],
  extra: number[],
): number[] {
  const seen = new Set(values);
  const out: number[] = [];
  for (const n of extra) if (seen.has(n)) out.push(n);
  return out;
}

// expected: O(m log m + n log n) / O(n)
export function IntersectSort(
  values: number[],
  extra: number[],
): number[] {
  values.sort((a, b) => a - b);
  extra.sort((a, b) => a - b);
  const out: number[] = [];
  let i = 0;
  let j = 0;
  while (i < values.length && j < extra.length) {
    if (values[i] === extra[j]) {
      out.push(values[i]);
      i++;
      j++;
    } else if (values[i] < extra[j]) i++;
    else j++;
  }
  return out;
}

// expected: O(n) / O(1)
export function ValidPalindrome(s: string): boolean {
  let left = 0;
  let right = s.length - 1;
  while (left < right) {
    if (s[left] !== s[right]) return false;
    left++;
    right--;
  }
  return true;
}

// expected: O(n²) / O(1)
export function LongestOnes(nums: number[], k: number): number {
  let left = 0;
  let zeros = 0;
  let best = 0;
  for (let right = 0; right < nums.length; right++) {
    if (nums[right] === 0) zeros++;
    while (zeros > k) {
      if (nums[left] === 0) zeros--;
      left++;
    }
    best = Math.max(best, right - left + 1);
  }
  return best;
}

// expected: O(n) / O(n)
export function DailyTemperatures(values: number[]): number[] {
  const out = new Array<number>(values.length).fill(0);
  const stack: number[] = [];
  for (let i = 0; i < values.length; i++) {
    while (stack.length && values[stack[stack.length - 1]] < values[i]) {
      const j = stack.pop()!;
      out[j] = i - j;
    }
    stack.push(i);
  }
  return out;
}

// expected: O(n) / O(n)
export function NextGreater(values: number[]): number[] {
  const out = new Array<number>(values.length).fill(-1);
  const stack: number[] = [];
  for (let i = 0; i < values.length; i++) {
    while (stack.length && values[stack[stack.length - 1]] < values[i]) {
      out[stack.pop()!] = values[i];
    }
    stack.push(i);
  }
  return out;
}

// expected: O(n) / O(1)
export function SingleNumber(values: number[]): number {
  let x = 0;
  for (const n of values) x ^= n;
  return x;
}

// expected: O(n) / O(1)
export function MissingNumber(values: number[]): number {
  let xor = values.length;
  for (let i = 0; i < values.length; i++) xor ^= i ^ values[i];
  return xor;
}

// expected: O(n) / O(1)
export function MaxConsecutiveOnes(values: number[]): number {
  let best = 0;
  let cur = 0;
  for (const n of values) {
    cur = n === 1 ? cur + 1 : 0;
    if (cur > best) best = cur;
  }
  return cur > best ? cur : best;
}
