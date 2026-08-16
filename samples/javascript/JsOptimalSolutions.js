/**
 * JSDoc-typed textbook solutions. Comments are
 * `// expected: TIME / SPACE`.
 */

/**
 * @template T
 */
export class MinHeap {
  constructor() {
    /** @type {{key: number, value: T}[]} */
    this.items = [];
  }
  /** @returns {number} */
  get size() {
    return this.items.length;
  }
  /**
   * @param {number} key
   * @param {T} value
   */
  push(key, value) {
    this.items.push({ key, value });
  }
  /** @returns {T | undefined} */
  pop() {
    return this.items.shift()?.value;
  }
}

/**
 * @param {number} val
 * @param {ListNode | null} [next]
 */
export function ListNode(val, next) {
  this.val = val;
  this.next = next ?? null;
}

// expected: O(n) / O(n)
/** @param {number[]} nums @param {number} target */
export function TwoSum(nums, target) {
  const map = new Map();
  for (let i = 0; i < nums.length; i++) {
    const need = target - nums[i];
    if (map.has(need)) return [map.get(need), i];
    map.set(nums[i], i);
  }
  return [];
}

// expected: O(n) / O(1)
/** @param {number[]} prices */
export function MaxProfit(prices) {
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
/** @param {number[]} nums */
export function ContainsDuplicate(nums) {
  const seen = new Set();
  for (const value of nums) {
    if (seen.has(value)) return true;
    seen.add(value);
  }
  return false;
}

// expected: O(n) / O(1)
/** @param {number[]} nums */
export function MaxSubArray(nums) {
  let best = nums[0];
  let current = nums[0];
  for (let i = 1; i < nums.length; i++) {
    current = Math.max(nums[i], current + nums[i]);
    best = Math.max(best, current);
  }
  return best;
}

// expected: O(n) / O(1)
/** @param {number[]} height */
export function MaxArea(height) {
  let left = 0;
  let right = height.length - 1;
  let best = 0;
  while (left < right) {
    const h = Math.min(height[left], height[right]);
    best = Math.max(best, (right - left) * h);
    if (height[left] < height[right]) left++;
    else right--;
  }
  return best;
}

// expected: O(log n) / O(1)
/** @param {number[]} nums @param {number} target */
export function BinarySearch(nums, target) {
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

// expected: O(k + n) / O(k + n)
/** @param {string} s */
export function IsValid(s) {
  const stack = [];
  const pair = new Map([
    [')', '('], [']', '['], ['}', '{'],
  ]);
  for (const c of s) {
    if (c === '(' || c === '[' || c === '{') stack.push(c);
    else if (stack.pop() !== pair.get(c)) return false;
  }
  return stack.length === 0;
}

// expected: O(n) / O(n)
/** @param {string} s */
export function LengthOfLongestSubstring(s) {
  const last = new Map();
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
/** @param {number[][]} intervals */
export function Merge(intervals) {
  intervals.sort((a, b) => a[0] - b[0]);
  const merged = [];
  for (const interval of intervals) {
    const last = merged[merged.length - 1];
    if (!last || last[1] < interval[0]) merged.push(interval);
    else last[1] = Math.max(last[1], interval[1]);
  }
  return merged;
}

// expected: O(m log k + n) / O(k + n)
/** @param {number[]} nums @param {number} k */
export function TopKFrequent(nums, k) {
  const counts = new Map();
  for (const value of nums) {
    counts.set(value, (counts.get(value) ?? 0) + 1);
  }
  const heap = new MinHeap();
  for (const [value, count] of counts) {
    heap.push(count, value);
    if (heap.size > k) heap.pop();
  }
  const out = [];
  for (let i = 0; i < k; i++) out.push(heap.pop());
  return out;
}

// expected: O(n²) / O(n)
/** @param {number[]} nums */
export function ThreeSum(nums) {
  nums.sort((a, b) => a - b);
  const result = [];
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
      } else if (sum < 0) left++;
      else right--;
    }
  }
  return result;
}

// expected: O(n) / O(1)
/** @param {number} n */
export function ClimbStairs(n) {
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
/** @param {number[]} nums */
export function Rob(nums) {
  let prev = 0;
  let curr = 0;
  for (const value of nums) {
    const next = Math.max(curr, prev + value);
    prev = curr;
    curr = next;
  }
  return curr;
}

// expected: O(n) / O(1)
/** @param {ListNode | null} head */
export function ReverseList(head) {
  let prev = null;
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
/** @param {ListNode | null} head */
export function HasCycle(head) {
  let slow = head;
  let fast = head;
  while (fast && fast.next) {
    slow = slow.next;
    fast = fast.next.next;
    if (slow === fast) return true;
  }
  return false;
}

// expected: O(n) / O(n)
/** @param {number[]} nums */
export function ProductExceptSelf(nums) {
  const result = new Array(nums.length);
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
/** @param {number[]} nums @param {number} target */
export function SearchRotated(nums, target) {
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
/** @param {number[]} height */
export function Trap(height) {
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

// expected: O(k n + m n log m + n C(push) + n p) / O(k + n + p)
/** @param {string[]} strs */
export function GroupAnagrams(strs) {
  const groups = new Map();
  for (const s of strs) {
    const key = s.split('').sort().join('');
    const list = groups.get(key) ?? [];
    list.push(s);
    groups.set(key, list);
  }
  return [...groups.values()];
}

// expected: O(m n) / O(m)
/** @param {number[]} coins @param {number} amount */
export function CoinChange(coins, amount) {
  const dp = new Array(amount + 1).fill(amount + 1);
  dp[0] = 0;
  for (let a = 1; a <= amount; a++) {
    for (const coin of coins) {
      if (coin <= a) dp[a] = Math.min(dp[a], dp[a - coin] + 1);
    }
  }
  return dp[amount] > amount ? -1 : dp[amount];
}

// expected: O(n log n) / O(n)
/** @param {number[]} nums */
export function LengthOfLIS(nums) {
  const tails = [];
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

// expected: O(m + n) / O(m + n)
/** @param {number} numCourses @param {number[][]} prerequisites */
export function CanFinish(numCourses, prerequisites) {
  /** @type {number[][]} */
  const adj = new Array(numCourses);
  const indeg = new Array(numCourses).fill(0);
  for (let i = 0; i < numCourses; i++) adj[i] = [];
  for (const e of prerequisites) {
    adj[e[1]].push(e[0]);
    indeg[e[0]]++;
  }
  const q = [];
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
/** @param {number[]} nums */
export function SockMerchant(nums) {
  const counts = new Map();
  let pairs = 0;
  for (const value of nums) {
    const next = (counts.get(value) ?? 0) + 1;
    counts.set(value, next);
    if (next % 2 === 0) pairs++;
  }
  return pairs;
}

// expected: O(n) / O(1)
/** @param {string} s */
export function CountingValleys(s) {
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
/** @param {number[]} nums */
export function JumpingOnClouds(nums) {
  let i = 0;
  let jumps = 0;
  while (i < nums.length - 1) {
    i += nums[i + 2] === 0 ? 2 : 1;
    jumps++;
  }
  return jumps;
}

// expected: O(n) / O(1)
/** @param {string} s */
export function AlternatingCharacters(s) {
  let cuts = 0;
  for (let i = 1; i < s.length; i++) {
    if (s[i] === s[i - 1]) cuts++;
  }
  return cuts;
}

// expected: O(n) / O(1)
/** @param {number[]} prices */
export function StockMaximize(prices) {
  let best = 0;
  let profit = 0;
  for (let i = prices.length - 1; i >= 0; i--) {
    if (prices[i] > best) best = prices[i];
    profit += best - prices[i];
  }
  return profit;
}

// expected: O(n log n) / O(n)
/** @param {number[]} freqs */
export function Huffman(freqs) {
  const heap = new MinHeap();
  for (const f of freqs) heap.push(f, f);
  let cost = 0;
  while (heap.size > 1) {
    const a = heap.pop() ?? 0;
    const b = heap.pop() ?? 0;
    cost += a + b;
    heap.push(a + b, a + b);
  }
  return cost;
}
