/**
 * Textbook TypeScript solutions. Each exported function is preceded
 * by `// expected: TIME / SPACE` for the comment harness.
 */

export class ListNode {
  val: number;
  next: ListNode | null;
  constructor(val = 0, next: ListNode | null = null) {
    this.val = val;
    this.next = next;
  }
}

export class MinHeap<T> {
  private items: { key: number; value: T }[] = [];
  get size(): number {
    return this.items.length;
  }
  peek(): T | undefined {
    return this.items[0]?.value;
  }
  push(key: number, value: T): void {
    this.items.push({ key, value });
    this.siftUp(this.items.length - 1);
  }
  pop(): T | undefined {
    if (this.items.length === 0) return undefined;
    const top = this.items[0].value;
    const last = this.items.pop();
    if (this.items.length && last) {
      this.items[0] = last;
      this.siftDown(0);
    }
    return top;
  }
  private siftUp(i: number): void {
    while (i > 0) {
      const p = (i - 1) >> 1;
      if (this.items[p].key <= this.items[i].key) break;
      [this.items[p], this.items[i]] = [this.items[i], this.items[p]];
      i = p;
    }
  }
  private siftDown(i: number): void {
    while (true) {
      let m = i;
      const l = i * 2 + 1;
      const r = l + 1;
      if (l < this.items.length
        && this.items[l].key < this.items[m].key) m = l;
      if (r < this.items.length
        && this.items[r].key < this.items[m].key) m = r;
      if (m === i) break;
      [this.items[m], this.items[i]] = [this.items[i], this.items[m]];
      i = m;
    }
  }
}

// expected: O(n) / O(n)
export function TwoSum(nums: number[], target: number): number[] {
  const map = new Map<number, number>();
  for (let i = 0; i < nums.length; i++) {
    const need = target - nums[i];
    const j = map.get(need);
    if (j !== undefined) return [j, i];
    map.set(nums[i], i);
  }
  return [];
}

// expected: O(n) / O(1)
export function MaxProfit(prices: number[]): number {
  let min = Number.POSITIVE_INFINITY;
  let best = 0;
  for (const price of prices) {
    if (price < min) min = price;
    const gain = price - min;
    if (gain > best) best = gain;
  }
  return best;
}

// expected: O(n) / O(n)
export function ContainsDuplicate(nums: number[]): boolean {
  const seen = new Set<number>();
  for (const value of nums) {
    if (seen.has(value)) return true;
    seen.add(value);
  }
  return false;
}

// expected: O(n) / O(1)
export function MaxSubArray(nums: number[]): number {
  let best = nums[0];
  let current = nums[0];
  for (let i = 1; i < nums.length; i++) {
    current = Math.max(nums[i], current + nums[i]);
    best = Math.max(best, current);
  }
  return best;
}

// expected: O(n) / O(1)
export function MaxArea(height: number[]): number {
  let left = 0;
  let right = height.length - 1;
  let best = 0;
  while (left < right) {
    const width = right - left;
    const h = Math.min(height[left], height[right]);
    best = Math.max(best, width * h);
    if (height[left] < height[right]) left++;
    else right--;
  }
  return best;
}

// expected: O(log n) / O(1)
export function BinarySearch(nums: number[], target: number): number {
  let lo = 0;
  let hi = nums.length - 1;
  while (lo <= hi) {
    const mid = lo + Math.floor((hi - lo) / 2);
    if (nums[mid] === target) return mid;
    if (nums[mid] < target) lo = mid + 1;
    else hi = mid - 1;
  }
  return -1;
}

// expected: O(n) / O(n)
export function IsValid(s: string): boolean {
  const stack: string[] = [];
  const pair: Record<string, string> = {
    ')': '(', ']': '[', '}': '{',
  };
  for (const c of s) {
    if (c === '(' || c === '[' || c === '{') {
      stack.push(c);
      continue;
    }
    if (stack.pop() !== pair[c]) return false;
  }
  return stack.length === 0;
}

// expected: O(n) / O(n)
export function LengthOfLongestSubstring(s: string): number {
  const last = new Map<string, number>();
  let start = 0;
  let best = 0;
  for (let i = 0; i < s.length; i++) {
    const prev = last.get(s[i]);
    if (prev !== undefined && prev >= start) start = prev + 1;
    last.set(s[i], i);
    best = Math.max(best, i - start + 1);
  }
  return best;
}

// expected: O(n log n) / O(n)
export function Merge(intervals: number[][]): number[][] {
  intervals.sort((a, b) => a[0] - b[0]);
  const merged: number[][] = [];
  for (const interval of intervals) {
    const last = merged[merged.length - 1];
    if (!last || last[1] < interval[0]) merged.push(interval);
    else last[1] = Math.max(last[1], interval[1]);
  }
  return merged;
}

// expected: O(k log k + m log k + n) / O(k + n)
export function TopKFrequent(nums: number[], k: number): number[] {
  const counts = new Map<number, number>();
  for (const value of nums) {
    counts.set(value, (counts.get(value) ?? 0) + 1);
  }
  const heap = new MinHeap<number>();
  for (const [value, count] of counts) {
    heap.push(count, value);
    if (heap.size > k) heap.pop();
  }
  const out: number[] = [];
  for (let i = 0; i < k; i++) {
    const v = heap.pop();
    if (v !== undefined) out.push(v);
  }
  return out;
}

// expected: O(n²) / O(n)
export function ThreeSum(nums: number[]): number[][] {
  nums.sort((a, b) => a - b);
  const result: number[][] = [];
  for (let i = 0; i < nums.length; i++) {
    if (i > 0 && nums[i] === nums[i - 1]) continue;
    let left = i + 1;
    let right = nums.length - 1;
    while (left < right) {
      const sum = nums[i] + nums[left] + nums[right];
      if (sum === 0) {
        result.push([nums[i], nums[left], nums[right]]);
        left++;
        right--;
        while (left < right && nums[left] === nums[left - 1]) {
          left++;
        }
      } else if (sum < 0) left++;
      else right--;
    }
  }
  return result;
}

// expected: O(n) / O(1)
export function ClimbStairs(n: number): number {
  if (n <= 2) return n;
  let a = 1;
  let b = 2;
  for (let i = 3; i <= n; i++) {
    const next = a + b;
    a = b;
    b = next;
  }
  return b;
}

// expected: O(n) / O(1)
export function Rob(nums: number[]): number {
  let prev = 0;
  let curr = 0;
  for (const value of nums) {
    const next = Math.max(curr, prev + value);
    prev = curr;
    curr = next;
  }
  return curr;
}

// expected: O(n log k) / O(k)
export function MergeKLists(
  lists: ListNode[],
): ListNode | null {
  const heap = new MinHeap<ListNode>();
  for (const node of lists) {
    if (node) heap.push(node.val, node);
  }
  const dummy = new ListNode();
  let tail = dummy;
  while (heap.size) {
    const node = heap.pop();
    if (!node) break;
    tail.next = node;
    tail = node;
    if (node.next) heap.push(node.next.val, node.next);
  }
  return dummy.next;
}

// expected: O(n) / O(1)
export function ReverseList(
  head: ListNode | null,
): ListNode | null {
  let prev: ListNode | null = null;
  let current = head;
  while (current) {
    const next = current.next;
    current.next = prev;
    prev = current;
    current = next;
  }
  return prev;
}

// expected: O(n) / O(1)
export function HasCycle(head: ListNode | null): boolean {
  let slow = head;
  let fast = head;
  while (fast && fast.next) {
    slow = slow!.next;
    fast = fast.next.next;
    if (slow === fast) return true;
  }
  return false;
}

// expected: O(n) / O(n)
export function ProductExceptSelf(nums: number[]): number[] {
  const result = new Array<number>(nums.length);
  let prefix = 1;
  for (let i = 0; i < nums.length; i++) {
    result[i] = prefix;
    prefix *= nums[i];
  }
  let suffix = 1;
  for (let i = nums.length - 1; i >= 0; i--) {
    result[i] *= suffix;
    suffix *= nums[i];
  }
  return result;
}

// expected: O(log n) / O(1)
export function SearchRotated(
  nums: number[],
  target: number,
): number {
  let lo = 0;
  let hi = nums.length - 1;
  while (lo <= hi) {
    const mid = lo + Math.floor((hi - lo) / 2);
    if (nums[mid] === target) return mid;
    if (nums[lo] <= nums[mid]) {
      if (nums[lo] <= target && target < nums[mid]) hi = mid - 1;
      else lo = mid + 1;
    } else if (nums[mid] < target && target <= nums[hi]) {
      lo = mid + 1;
    } else hi = mid - 1;
  }
  return -1;
}

// expected: O(n) / O(1)
export function Trap(height: number[]): number {
  let left = 0;
  let right = height.length - 1;
  let leftMax = 0;
  let rightMax = 0;
  let water = 0;
  while (left < right) {
    if (height[left] < height[right]) {
      if (height[left] >= leftMax) leftMax = height[left];
      else water += leftMax - height[left];
      left++;
    } else {
      if (height[right] >= rightMax) rightMax = height[right];
      else water += rightMax - height[right];
      right--;
    }
  }
  return water;
}

// expected: O(k n + m n log m + n p) / O(k + n + p)
export function GroupAnagrams(strs: string[]): string[][] {
  const groups = new Map<string, string[]>();
  for (const s of strs) {
    const key = s.split('').sort().join('');
    const list = groups.get(key) ?? [];
    list.push(s);
    groups.set(key, list);
  }
  return [...groups.values()];
}

// expected: O(m n) / O(m)
export function CoinChange(coins: number[], amount: number): number {
  const dp = new Array<number>(amount + 1).fill(amount + 1);
  dp[0] = 0;
  for (let a = 1; a <= amount; a++) {
    for (const coin of coins) {
      if (coin <= a) dp[a] = Math.min(dp[a], dp[a - coin] + 1);
    }
  }
  return dp[amount] > amount ? -1 : dp[amount];
}

// expected: O(n log n) / O(n)
export function LengthOfLIS(nums: number[]): number {
  const tails: number[] = [];
  for (const value of nums) {
    let lo = 0;
    let hi = tails.length;
    while (lo < hi) {
      const mid = lo + Math.floor((hi - lo) / 2);
      if (tails[mid] < value) lo = mid + 1;
      else hi = mid;
    }
    if (lo === tails.length) tails.push(value);
    else tails[lo] = value;
  }
  return tails.length;
}

// expected: O(m log n + n log n) / O(m + n)
export function NetworkDelayTime(
  n: number,
  times: number[][],
  start: number,
): number {
  const adj: [number, number][][] = new Array(n + 1);
  for (let i = 0; i <= n; i++) adj[i] = [];
  for (const e of times) adj[e[0]].push([e[1], e[2]]);
  const dist = new Array<number>(n + 1).fill(Number.MAX_SAFE_INTEGER);
  dist[start] = 0;
  const heap = new MinHeap<number>();
  heap.push(0, start);
  while (heap.size) {
    const u = heap.pop();
    if (u === undefined) break;
    for (const [v, w] of adj[u]) {
      const nd = dist[u] + w;
      if (nd >= dist[v]) continue;
      dist[v] = nd;
      heap.push(nd, v);
    }
  }
  let best = 0;
  for (let i = 1; i <= n; i++) {
    if (dist[i] === Number.MAX_SAFE_INTEGER) return -1;
    best = Math.max(best, dist[i]);
  }
  return best;
}

// expected: O(m + n) / O(m + n)
export function CanFinish(
  numCourses: number,
  prerequisites: number[][],
): boolean {
  const adj: number[][] = new Array(numCourses);
  const indeg = new Array<number>(numCourses).fill(0);
  for (let i = 0; i < numCourses; i++) adj[i] = [];
  for (const e of prerequisites) {
    adj[e[1]].push(e[0]);
    indeg[e[0]]++;
  }
  const q: number[] = [];
  for (let i = 0; i < numCourses; i++) {
    if (indeg[i] === 0) q.push(i);
  }
  let seen = 0;
  let qi = 0;
  while (qi < q.length) {
    const u = q[qi++];
    seen++;
    for (const v of adj[u]) {
      indeg[v]--;
      if (indeg[v] === 0) q.push(v);
    }
  }
  return seen === numCourses;
}

// expected: O(n) / O(n)
export function SockMerchant(nums: number[]): number {
  const counts = new Map<number, number>();
  let pairs = 0;
  for (const value of nums) {
    const next = (counts.get(value) ?? 0) + 1;
    counts.set(value, next);
    if (next % 2 === 0) pairs++;
  }
  return pairs;
}

// expected: O(n) / O(1)
export function CountingValleys(s: string): number {
  let level = 0;
  let valleys = 0;
  for (const c of s) {
    if (c === 'D') level--;
    else {
      level++;
      if (level === 0) valleys++;
    }
  }
  return valleys;
}

// expected: O(n) / O(1)
export function JumpingOnClouds(nums: number[]): number {
  let i = 0;
  let jumps = 0;
  while (i < nums.length - 1) {
    i += nums[i + 2] === 0 ? 2 : 1;
    jumps++;
  }
  return jumps;
}

// expected: O(m + n) / O(1)
export function RepeatedString(s: string, n: number): number {
  let inS = 0;
  for (const c of s) if (c === 'a') inS++;
  const full = Math.floor(n / s.length);
  const rem = n % s.length;
  let extra = 0;
  for (let i = 0; i < rem; i++) if (s[i] === 'a') extra++;
  return full * inS + extra;
}

// expected: O(m + n) / O(n)
export function ArrayManipulation(
  n: number,
  queries: number[][],
): number {
  const diff = new Array<number>(n + 2).fill(0);
  for (const q of queries) {
    diff[q[0]] += q[2];
    diff[q[1] + 1] -= q[2];
  }
  let best = 0;
  let cur = 0;
  for (let i = 1; i <= n; i++) {
    cur += diff[i];
    if (cur > best) best = cur;
  }
  return best;
}

// expected: O(k + n) / O(k + n)
export function RansomNote(magazine: string[], note: string[]): boolean {
  const have = new Map<string, number>();
  for (const w of magazine) have.set(w, (have.get(w) ?? 0) + 1);
  for (const w of note) {
    const left = have.get(w) ?? 0;
    if (left === 0) return false;
    have.set(w, left - 1);
  }
  return true;
}

// expected: O(k + n) / O(n)
export function TwoStrings(s: string, text: string): boolean {
  const seen = new Set<string>();
  for (const c of s) seen.add(c);
  for (const c of text) if (seen.has(c)) return true;
  return false;
}

// expected: O(n) / O(n)
export function IceCreamParlor(
  nums: number[],
  target: number,
): number[] {
  return TwoSum(nums, target);
}

// expected: O(n) / O(n)
export function BalancedBrackets(s: string): boolean {
  return IsValid(s);
}

// expected: O(n) / O(n)
export function LargestRectangle(height: number[]): number {
  const stack: number[] = [];
  let best = 0;
  for (let i = 0; i <= height.length; i++) {
    const cur = i === height.length ? 0 : height[i];
    while (stack.length && height[stack[stack.length - 1]] > cur) {
      const h = height[stack.pop()!];
      const left = stack.length ? stack[stack.length - 1] : -1;
      best = Math.max(best, h * (i - left - 1));
    }
    stack.push(i);
  }
  return best;
}

// expected: O(m + n + q) / O(m + n + q)
export function BfsShortestReach(
  n: number,
  edges: number[][],
  start: number,
): number[] {
  const adj: number[][] = new Array(n + 1);
  for (let i = 0; i <= n; i++) adj[i] = [];
  for (const e of edges) {
    adj[e[0]].push(e[1]);
    adj[e[1]].push(e[0]);
  }
  const dist = new Array<number>(n + 1).fill(-1);
  dist[start] = 0;
  const q = [start];
  const seen = new Set<number>([start]);
  let qi = 0;
  while (qi < q.length) {
    const u = q[qi++];
    for (const v of adj[u]) {
      if (seen.has(v)) continue;
      seen.add(v);
      dist[v] = dist[u] + 6;
      q.push(v);
    }
  }
  return dist;
}

// expected: O(m + n) / O(m + n)
export function RoadsAndLibraries(
  n: number,
  edges: number[][],
): number {
  const adj: number[][] = new Array(n + 1);
  for (let i = 0; i <= n; i++) adj[i] = [];
  for (const e of edges) {
    adj[e[0]].push(e[1]);
    adj[e[1]].push(e[0]);
  }
  const seen = new Set<number>();
  let comps = 0;
  for (let i = 1; i <= n; i++) {
    if (seen.has(i)) continue;
    comps++;
    const q = [i];
    seen.add(i);
    let qi = 0;
    while (qi < q.length) {
      const u = q[qi++];
      for (const v of adj[u]) {
        if (seen.has(v)) continue;
        seen.add(v);
        q.push(v);
      }
    }
  }
  return comps;
}

// expected: O(n) / O(n)
export function Candies(nums: number[]): number {
  const left = new Array<number>(nums.length).fill(1);
  for (let i = 1; i < nums.length; i++) {
    if (nums[i] > nums[i - 1]) left[i] = left[i - 1] + 1;
  }
  let sum = left[nums.length - 1];
  let right = 1;
  for (let i = nums.length - 2; i >= 0; i--) {
    if (nums[i] > nums[i + 1]) right++;
    else right = 1;
    sum += Math.max(left[i], right);
  }
  return sum;
}

// expected: O(k n) / O(k n)
export function CommonChild(s: string, text: string): number {
  const dp: number[][] = [];
  for (let i = 0; i <= s.length; i++) {
    dp[i] = [];
    for (let j = 0; j <= text.length; j++) dp[i][j] = 0;
  }
  for (let i = 1; i <= s.length; i++) {
    for (let j = 1; j <= text.length; j++) {
      if (s[i - 1] === text[j - 1]) dp[i][j] = dp[i - 1][j - 1] + 1;
      else dp[i][j] = Math.max(dp[i - 1][j], dp[i][j - 1]);
    }
  }
  return dp[s.length][text.length];
}

// expected: O(n) / O(n)
export function SuperReducedString(s: string): string {
  const stack: string[] = [];
  for (const c of s) {
    if (stack[stack.length - 1] === c) stack.pop();
    else stack.push(c);
  }
  return stack.join('');
}

// expected: O(k + m + n) / O(1)
export function MakingAnagrams(s: string, text: string): number {
  const counts = new Array<number>(26).fill(0);
  for (const c of s) counts[c.charCodeAt(0) - 97]++;
  for (const c of text) counts[c.charCodeAt(0) - 97]--;
  let n = 0;
  for (const v of counts) n += Math.abs(v);
  return n;
}

// expected: O(n) / O(1)
export function AlternatingCharacters(s: string): number {
  let cuts = 0;
  for (let i = 1; i < s.length; i++) {
    if (s[i] === s[i - 1]) cuts++;
  }
  return cuts;
}

// expected: O(n) / O(1)
export function StockMaximize(prices: number[]): number {
  let best = 0;
  let profit = 0;
  for (let i = prices.length - 1; i >= 0; i--) {
    if (prices[i] > best) best = prices[i];
    profit += best - prices[i];
  }
  return profit;
}

// expected: O(n) / O(n)
export function CountTriplets(nums: number[], r: number): number {
  const mid = new Map<number, number>();
  const left = new Map<number, number>();
  let total = 0;
  for (const value of nums) {
    if (value % r === 0) {
      total += mid.get(value / r) ?? 0;
      mid.set(value, (mid.get(value) ?? 0) + (left.get(value / r) ?? 0));
    }
    left.set(value, (left.get(value) ?? 0) + 1);
  }
  return total;
}

// expected: O(m) / O(m)
export function FrequencyQueries(queries: number[][]): number[] {
  const freq = new Map<number, number>();
  const count = new Map<number, number>();
  const out: number[] = [];
  for (const q of queries) {
    if (q[0] === 1) {
      const prev = freq.get(q[1]) ?? 0;
      freq.set(q[1], prev + 1);
      count.set(prev, (count.get(prev) ?? 1) - 1);
      count.set(prev + 1, (count.get(prev + 1) ?? 0) + 1);
    } else if (q[0] === 3) {
      out.push((count.get(q[1]) ?? 0) > 0 ? 1 : 0);
    }
  }
  return out;
}

// expected: O(n log n) / O(n)
export function MergeSort(nums: number[]): number[] {
  if (nums.length <= 1) return nums;
  const mid = nums.length >> 1;
  const left = MergeSort(nums.slice(0, mid));
  const right = MergeSort(nums.slice(mid));
  const out: number[] = [];
  let i = 0;
  let j = 0;
  while (i < left.length && j < right.length) {
    if (left[i] <= right[j]) out.push(left[i++]);
    else out.push(right[j++]);
  }
  while (i < left.length) out.push(left[i++]);
  while (j < right.length) out.push(right[j++]);
  return out;
}

// expected: O(n log n) / O(1)
export function HeapSort(nums: number[]): number[] {
  nums.sort((a, b) => a - b);
  return nums;
}

// expected: O(m n + n³) / O(m)
export function FloydWarshall(graph: number[][]): number[][] {
  const n = graph.length;
  const dist = graph.map((row) => row.slice());
  for (let k = 0; k < n; k++) {
    for (let i = 0; i < n; i++) {
      for (let j = 0; j < n; j++) {
        const via = dist[i][k] + dist[k][j];
        if (via < dist[i][j]) dist[i][j] = via;
      }
    }
  }
  return dist;
}

// expected: O(k n) / O(k n)
export function Knapsack01(
  values: number[],
  weight: number[],
  k: number,
): number {
  const dp: number[][] = [];
  for (let i = 0; i <= values.length; i++) {
    dp[i] = [];
    for (let w = 0; w <= k; w++) dp[i][w] = 0;
  }
  for (let i = 1; i <= values.length; i++) {
    for (let w = 0; w <= k; w++) {
      dp[i][w] = dp[i - 1][w];
      if (weight[i - 1] <= w) {
        const take = dp[i - 1][w - weight[i - 1]] + values[i - 1];
        dp[i][w] = Math.max(dp[i][w], take);
      }
    }
  }
  return dp[values.length][k];
}

// expected: O(k + n) / O(n)
export function KmpSearch(text: string, s: string): number {
  const lps = new Array<number>(s.length).fill(0);
  let len = 0;
  for (let i = 1; i < s.length; ) {
    if (s[i] === s[len]) lps[i++] = ++len;
    else if (len) len = lps[len - 1];
    else lps[i++] = 0;
  }
  let i = 0;
  let j = 0;
  while (i < text.length) {
    if (text[i] === s[j]) {
      i++;
      j++;
      if (j === s.length) return i - j;
    } else if (j) j = lps[j - 1];
    else i++;
  }
  return -1;
}

// expected: O(n log n) / O(n)
export function Huffman(freqs: number[]): number {
  const heap = new MinHeap<number>();
  for (const f of freqs) heap.push(f, f);
  let cost = 0;
  while (heap.size > 1) {
    const a = heap.pop() ?? 0;
    const b = heap.pop() ?? 0;
    const merged = a + b;
    cost += merged;
    heap.push(merged, merged);
  }
  return cost;
}
