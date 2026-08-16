export function sortNums(nums: number[]): number[] {
  return nums.toSorted();
}

export function sortInPlace(nums: number[]): void {
  nums.sort();
}

export function mapped(items: number[]): number[] {
  return items.map((n) => n + 1);
}

export function hasKey(map: Map<string, number>, key: string): boolean {
  return map.has(key);
}

export function mentions(text: string, needle: string): boolean {
  return text.includes(needle);
}
