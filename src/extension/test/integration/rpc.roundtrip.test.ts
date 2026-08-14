import * as fs from 'node:fs';
import * as path from 'node:path';
import { describe, expect, it } from 'vitest';
import { AnalyzerRpcClient } from '../../src/analysis/rpcClient';

const serverPath = [
  path.resolve(__dirname, '../../server/ComplexityAnalyzer.Server'),
  path.resolve(__dirname, '../../../analyzer/ComplexityAnalyzer.Server/bin/Debug/net8.0/ComplexityAnalyzer.Server'),
].find((candidate) => fs.existsSync(candidate));

describe('analyzer RPC round-trip', () => {
  it('initializes and analyzes TopK through vscode-jsonrpc', async () => {
    if (!serverPath) {
      throw new Error('Analyzer server binary not found. Build ComplexityAnalyzer.Server first.');
    }

    const client = new AnalyzerRpcClient(serverPath, () => undefined);
    try {
      const response = await client.analyze({
        uri: 'file:///tmp/TopK.cs',
        version: 1,
        tier: 'fast',
        text: `
using System.Collections.Generic;
using System.Linq;
public static class S {
  public static int[] TopK(int[] values, int k) {
    var pq = new PriorityQueue<int, int>();
    foreach (var value in values) {
      pq.Enqueue(value, value);
      if (pq.Count > k) pq.Dequeue();
    }
    return pq.UnorderedItems.Select(x => x.Element).ToArray();
  }
}
`,
      });

      expect(Array.isArray(response.functions)).toBe(true);
      const topk = response.functions.find((fn) => fn.name === 'TopK');
      expect(topk).toBeDefined();
      expect(topk!.time).toBe('O(n log k)');
      expect(topk!.space).toBe('O(k)');
      expect(Array.isArray(topk!.evidence.children)).toBe(true);

      const deep = await client.analyzeDeep({
        uri: 'file:///tmp/TopK.cs',
        version: 1,
        tier: 'deep',
        text: `
using System.Collections.Generic;
using System.Linq;
public static class S {
  public static int[] TopK(int[] values, int k) {
    var pq = new PriorityQueue<int, int>();
    foreach (var value in values) {
      pq.Enqueue(value, value);
      if (pq.Count > k) pq.Dequeue();
    }
    return pq.UnorderedItems.Select(x => x.Element).ToArray();
  }
}
`,
      });
      const deepTopk = deep.functions.find((fn) => fn.name === 'TopK');
      expect(deepTopk).toBeDefined();
      expect(deepTopk!.time).toBe('O(n log k)');
      expect(deepTopk!.tier).toBe('deep');
    } finally {
      client.dispose();
    }
  }, 30_000);
});
