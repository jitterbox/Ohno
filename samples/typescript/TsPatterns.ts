export function grow(parts: string[]): string {
  let text = '';
  for (const part of parts) {
    text += part;
  }
  return text;
}

export function templateGrow(parts: string[]): string {
  let text = '';
  for (const part of parts) {
    text = `${text}${part}`;
  }
  return text;
}

export function scan(text: string): boolean {
  return /abc/.test(text);
}

export function backtrack(text: string): boolean {
  return /(a+)+b/.test(text);
}

export function drain(queue: number[]): number | undefined {
  const seen = new Set<number>();
  while (queue.length) {
    const item = queue.shift();
    if (item === undefined || seen.has(item)) continue;
    seen.add(item);
    queue.push(item + 1);
  }
  return queue[0];
}

export function refill(queue: number[]): void {
  while (queue.length) {
    const item = queue.shift();
    if (item !== undefined) queue.push(item + 1);
  }
}

export function window(values: number[], k: number): number[] {
  const q: number[] = [];
  for (const value of values) {
    q.push(value);
    if (q.length > k) q.shift();
  }
  return q;
}

export function fib(n: number): number {
  if (n < 2) return n;
  return fib(n - 1) + fib(n - 2);
}

export function walkDown(n: number): number {
  if (n <= 0) return 0;
  return 1 + walkDown(n - 1);
}

export function List(items: number[]): unknown {
  return items.map((n) => n);
}
