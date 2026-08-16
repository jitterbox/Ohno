import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { existsSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import {
  pingInline,
  TypeScriptAnalyzer,
  TS_LANGUAGE_IDS,
} from '../../src/analysis/typescript/facade';
import { dispatch } from '../../src/analysis/typescript/messages';

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

  it('covers the four opt-in language ids', () => {
    const analyzer = new TypeScriptAnalyzer();
    expect([...analyzer.languageIds]).toEqual([...TS_LANGUAGE_IDS]);
    analyzer.dispose();
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
