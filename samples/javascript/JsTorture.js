/**
 * JavaScript-only hazards. Comments are `// expected: TIME / SPACE`.
 */

// expected: O(C(sort)) / O(1)
export function UntypedSort(arr) {
  return arr.sort();
}

// expected: O(n log n) / O(1)
/** @param {number[]} nums */
export function TypedSort(nums) {
  return nums.sort((a, b) => a - b);
}

// expected: O(C(get)) / O(1)
export function UntypedLookup(map, key) {
  return map.get(key);
}

// expected: O(1) / O(1)
/** @param {Map<any, any>} map @param {any} key */
export function TypedMapGet(map, key) {
  return map.get(key);
}

// expected: O(C(eval)) / O(1)
export function EvalJson(s) {
  return eval('(' + s + ')');
}

// expected: O(C(Function)) / O(1)
export function FunctionFactory(body) {
  return Function('n', body);
}

// expected: O(unknown) / O(1)
export async function ForAwaitDrain(items) {
  let n = 0;
  for await (const x of items) n += x;
  return n;
}

// expected: O(n²) / O(1)
/** @param {string[]} values */
export function JsStringGrow(values) {
  let text = '';
  for (const part of values) text += part;
  return text;
}

// expected: O(unknown) / O(1)
/** @param {string} s */
export function JsBacktrack(s) {
  return /(a+)+b/.test(s);
}

// expected: O(n) / O(1)
/** @param {string} s */
export function JsLinearRegex(s) {
  return /abc/.test(s);
}

// expected: O(C(iterate)) / O(1)
export function UntypedLoop(items) {
  let n = 0;
  for (const x of items) n += x;
  return n;
}

// expected: O(n) / O(1)
/** @param {number[]} items */
export function TypedLoop(items) {
  let n = 0;
  for (const x of items) n += x;
  return n;
}

// expected: O(n) / O(n)
/** @param {number[]} values */
export function ArgumentsLike(values) {
  const out = [];
  for (let i = 0; i < values.length; i++) out.push(values[i]);
  return out;
}

// expected: O(n) / O(1)
export function ArgumentsObject() {
  let sum = 0;
  for (let i = 0; i < arguments.length; i++) sum += arguments[i];
  return sum;
}

// expected: O(C(call) C(iterate)) / O(1)
/** @param {object} obj */
export function ForInOwn(obj) {
  const keys = [];
  for (const key in obj) {
    if (Object.prototype.hasOwnProperty.call(obj, key)) keys.push(key);
  }
  return keys;
}

// expected: O(C(hasOwnProperty) C(iterate)) / O(1)
export function ForInUntyped(obj) {
  const keys = [];
  for (const key in obj) {
    if (obj.hasOwnProperty(key)) keys.push(key);
  }
  return keys;
}

// expected: O(n C(get)) / O(1)
/** @param {number[]} values */
export function ProtoWalkSafe(values) {
  const out = Object.create(null);
  for (const n of values) out[n] = n;
  return out;
}

// expected: O(n) / O(1)
/** @param {number[]} values */
export function SparseFill(values) {
  const out = [];
  out.length = values.length;
  for (let i = 0; i < values.length; i++) out[i] = values[i];
  return out;
}

// expected: O(1) / O(1)
/** @param {ArrayLike<number>} values */
export function ArrayLikeScan(values) {
  let sum = 0;
  for (let i = 0; i < values.length; i++) sum += values[i];
  return sum;
}

// expected: O(n) / O(1)
/** @param {number[]} values */
export function ArrayFromMap(values) {
  return Array.from(values, (n) => n * 2);
}

// expected: O(n) / O(n)
/** @param {number[]} values */
export function FlatOne(values) {
  return [values].flat();
}

// expected: O(n) / O(n)
/** @param {number[]} values */
export function ToSplicedCopy(values) {
  return values.toSpliced(0, 0);
}

// expected: O(C(Promise)) / O(1)
export function WithTimeout(n) {
  return new Promise((resolve) => {
    setTimeout(() => resolve(n), 0);
  });
}

// expected: O(n) / O(n)
/** @param {number[]} values */
export function CloneGraph(values) {
  return structuredClone(values);
}

// expected: O(n) / O(n)
/** @param {string} s */
export function BtoaLoop(s) {
  const parts = [];
  for (const c of s) parts.push(c);
  return parts.join('');
}

// expected: O(C(iterate) + n) / O(n)
/** @param {number[]} values */
export function DeleteInLoop(values) {
  const obj = { ...values };
  for (const key in obj) delete obj[key];
  return obj;
}

// expected: O(n) / O(1)
/** @param {number[]} values */
export function InOperator(values) {
  let hits = 0;
  for (let i = 0; i < values.length; i++) {
    if (i in values) hits++;
  }
  return hits;
}

// expected: O(n) / O(1)
/** @param {number[]} values */
export function TypeofGuard(values) {
  let sum = 0;
  for (const n of values) if (typeof n === 'number') sum += n;
  return sum;
}

// expected: O(n) / O(1)
/** @param {number[]} values */
export function InstanceofGuard(values) {
  let n = 0;
  for (const v of values) if (v instanceof Number) n++;
  return n;
}

// expected: O(n) / O(n)
/** @param {number[]} values */
export function WeakSetMark(values) {
  const seen = new Set();
  for (const n of values) seen.add(n);
  return seen.size;
}

// expected: O(n log n) / O(1)
/** @param {string[]} values */
export function LocaleCompareSort(values) {
  return values.sort((a, b) => a.localeCompare(b));
}

// expected: O(n) / O(1)
/** @param {number} n */
export function NumericParam(n) {
  let sum = 0;
  for (let i = 0; i < n; i++) sum += i;
  return sum;
}

// expected: O(log n) / O(1)
/** @param {number[]} nums @param {number} target */
export function JsBinarySearch(nums, target) {
  let lo = 0;
  let hi = nums.length - 1;
  while (lo <= hi) {
    const mid = lo + ((hi - lo) >> 1);
    if (nums[mid] === target) return mid;
    if (nums[mid] < target) lo = mid + 1;
    else hi = mid - 1;
  }
  return -1;
}

// expected: O(n) / O(1)
/** @param {number[]} height */
export function JsTwoPointer(height) {
  let left = 0;
  let right = height.length - 1;
  let best = 0;
  while (left < right) {
    best = Math.max(best, right - left);
    if (height[left] < height[right]) left++;
    else right--;
  }
  return best;
}

// expected: O(unknown) / O(unknown)
/** @param {number[][]} graph @param {number} start */
export function JsBfsNoVisited(graph, start) {
  const q = [start];
  let count = 0;
  while (q.length) {
    const node = q.shift();
    count++;
    for (const next of graph[node]) q.push(next);
  }
  return count;
}

// expected: O(C(iterate)) / O(1)
export function JsWhileUntyped(n) {
  let i = 0;
  while (i < n) i++;
  return i;
}

// expected: O(n) / O(1)
/** @param {number} n */
export function JsWhileTyped(n) {
  let i = 0;
  while (i < n) i++;
  return i;
}

// expected: O(log n) / O(1)
/** @param {number} n */
export function JsWhileDoubleUp(n) {
  let i = 1;
  let steps = 0;
  while (i < n) {
    i *= 2;
    steps++;
  }
  return steps;
}

// expected: O(C(iterate)) / O(1)
export function JsWhileWrongWay(n) {
  let i = 0;
  while (i < n) i--;
  return i;
}
