/**
 * Remaining PatternRecognizer ids that have a JavaScript shape.
 * `// expected: TIME / SPACE` is asserted by the comment harness.
 */

// expected: O(n) / O(1)
export function* YieldRange(n: number): Generator<number> {
  for (let i = 0; i < n; i++) yield i;
}

// expected: O(1) / O(1)
export function CachedGet(
  map: Map<number, number>,
  key: number,
): number {
  if (map.has(key)) return map.get(key) ?? 0;
  const value = key * 2;
  map.set(key, value);
  return value;
}

// expected: O(C(get)) / O(1)
export function ReflectedGet(target: object, name: string): unknown {
  return Reflect.get(target, name);
}

// expected: O(C(call)) / O(1)
export function ComputedCall(
  obj: Record<string, () => number>,
  key: string,
): number {
  return obj[key]();
}

// expected: O(n) / O(1)
export function AllDone(
  items: Promise<void>[],
): Promise<void[]> {
  return Promise.all(items);
}

// expected: O(C(eval)) / O(1)
export function EvalBody(s: string): unknown {
  return eval(s);
}

// expected: O(n C(transform)) / O(1)
export function DelegateInsideLoop(
  values: number[],
  transform: (value: number) => number,
): number {
  let n = 0;
  for (const value of values) n += transform(value);
  return n;
}

// expected: O(n) / O(n)
export function ConsumeValues(groups: Map<string, string[]>): string[] {
  return [...groups.values()];
}
