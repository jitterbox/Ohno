export function contains(items: number[], value: number): boolean {
  for (const n of items) {
    if (n === value) return true;
  }
  return false;
}

export function counted(items: number[]): number {
  let sum = 0;
  for (let i = 0; i < items.length; i++) {
    sum += items[i];
  }
  return sum;
}

export function halved(n: number): number {
  let steps = 0;
  for (let i = n; i > 1; i = Math.floor(i / 2)) {
    steps++;
  }
  return steps;
}
