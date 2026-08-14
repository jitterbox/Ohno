import { describe, expect, it } from 'vitest';
import { serverExecutableNames } from '../../src/analysis/rpcClient';

describe('serverExecutableNames', () => {
  it('prefers the native binary name first', () => {
    const names = serverExecutableNames();
    expect(names).toHaveLength(2);
    if (process.platform === 'win32') {
      expect(names[0]).toBe('ComplexityAnalyzer.Server.exe');
    } else {
      expect(names[0]).toBe('ComplexityAnalyzer.Server');
    }
    expect(names).toContain('ComplexityAnalyzer.Server');
    expect(names).toContain('ComplexityAnalyzer.Server.exe');
  });
});
