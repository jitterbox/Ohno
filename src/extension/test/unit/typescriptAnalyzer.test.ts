import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { existsSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { analyzeDocument } from '../../src/analysis/typescript/analyze';
import {
  pingInline,
  TypeScriptAnalyzer,
  TS_LANGUAGE_IDS,
} from '../../src/analysis/typescript/facade';
import {
  bindAbort,
  dispatch,
  requestAbort,
} from '../../src/analysis/typescript/messages';

const workerFile = resolve(
  dirname(fileURLToPath(import.meta.url)),
  '../../dist/ohno-ts-worker.js',
);

describe('TypeScript worker bootstrap', () => {
  it('answers ping without starting Roslyn', () => {
    expect(pingInline()).toEqual({ ok: true });
    expect(dispatch({ id: 1, method: 'ping' })).toEqual({
      id: 1,
      result: { ok: true },
    });
  });

  it('covers the four TypeScript and JavaScript ids', () => {
    const analyzer = new TypeScriptAnalyzer();
    expect([...analyzer.languageIds]).toEqual([...TS_LANGUAGE_IDS]);
    analyzer.dispose();
  });

  it('skips a stale older version', () => {
    const params = {
      uri: 'file:///stale-review.ts',
      text: 'export function id(n: number) { return n; }',
      version: 2,
      tier: 'fast' as const,
    };
    expect(dispatch({ id: 1, method: 'analyze', params }).error)
      .toBeUndefined();
    const stale = dispatch({
      id: 2,
      method: 'analyze',
      params: { ...params, version: 1 },
    });
    expect(stale.error).toBe('cancelled');
  });

  it('cancels when the abort flag matches the request id', () => {
    const buf = new SharedArrayBuffer(4);
    bindAbort(buf);
    requestAbort(9);
    const result = dispatch({
      id: 9,
      method: 'analyze',
      params: {
        uri: 'file:///abort-review.ts',
        text: 'export function id(n: number) { return n; }',
        version: 1,
        tier: 'fast',
      },
    });
    expect(result.error).toBe('cancelled');
    requestAbort(0);
  });

  it('throws cancelled between functions', () => {
    let n = 0;
    expect(() => analyzeDocument({
      uri: 'file:///multi-cancel.ts',
      text: [
        'export function a() { return 1; }',
        'export function b() { return 2; }',
      ].join('\n'),
      version: 1,
      tier: 'fast',
    }, () => ++n > 1)).toThrow('cancelled');
  });

  it('pings through a worker thread', async () => {
    if (!existsSync(workerFile)) {
      return;
    }
    const analyzer = new TypeScriptAnalyzer(workerFile);
    try {
      await expect(analyzer.ping()).resolves.toEqual({ ok: true });
    } finally {
      analyzer.dispose();
    }
  });
});
