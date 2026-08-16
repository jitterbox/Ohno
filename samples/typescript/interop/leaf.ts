export function scan(values: number[]): number {
  let n = 0;
  for (const v of values) n += v;
  return n;
}
