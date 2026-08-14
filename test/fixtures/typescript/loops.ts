export function contains(items: number[], value: number): boolean {
  for (const n of items) {
    if (n === value) return true;
  }
  return false;
}

export function sortNums(nums: number[]): number[] {
  return nums.toSorted();
}
