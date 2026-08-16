import { scan } from './leaf';

// expected: O(n) / O(1)
export function Twice(values: number[]): number {
  return scan(values) + scan(values);
}
