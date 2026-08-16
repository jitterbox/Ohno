/**
 * `this`, call/apply, computed keys, and Proxy.
 * `// expected: TIME / SPACE` is asserted by the comment harness.
 */

// expected: O(n log n) / O(1)
export function SortHost(host: { items: number[] }): void {
  host.items.sort();
}

// expected: O(C(call)) / O(1)
export function Rebound(
  fn: (value: number) => number,
  value: number,
): number {
  return fn.call(undefined, value);
}

// expected: O(1) / O(1)
export function ComputedKey(
  obj: Record<string, number>,
  key: string,
): number {
  return obj[key];
}

// expected: O(C(Proxy)) / O(1)
export function Proxied(target: object): object {
  return new Proxy(target, {});
}

// expected: O(n) / O(n)
export function StructuredCopy(values: number[]): number[] {
  return structuredClone(values);
}
