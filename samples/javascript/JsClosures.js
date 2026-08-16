/**
 * Closure and capture hazards. JSDoc gives the checker evidence.
 * `// expected: TIME / SPACE` is asserted by the comment harness.
 */

/**
 * @param {number[]} values
 * @returns {number[]}
 */
// expected: O(n) / O(n)
export function ImmediateGrow(values) {
  const out = [];
  values.forEach((v) => out.push(v));
  return out;
}

/**
 * @param {number[]} values
 * @returns {() => void}
 */
// expected: O(C(mutate)) / O(1)
export function StoredGrow(values) {
  const out = [];
  return () => {
    for (const v of values) out.push(v);
  };
}

/**
 * @param {number[]} values
 * @returns {number}
 */
// expected: O(C(iterate)) / O(1)
export function UntypedForOf(values) {
  let n = 0;
  for (const v of /** @type {any} */ (values)) n += v;
  return n;
}
