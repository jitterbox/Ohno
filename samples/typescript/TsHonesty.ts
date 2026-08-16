export function userGet(
  obj: { get(key: string): number },
  key: string,
): number {
  return obj.get(key);
}

export function anyCall(value: any): unknown {
  return value.sort();
}

export function evalName(code: string): unknown {
  return eval(code);
}

export async function drain(stream: AsyncIterable<number>): Promise<number> {
  let total = 0;
  for await (const n of stream) {
    total += n;
  }
  return total;
}
