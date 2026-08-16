import { describe, expect, it } from 'vitest';
import { analyzeDocument } from '../../src/analysis/typescript/analyze';
import { collectFunctions } from '../../src/analysis/typescript/walk/functions';
import { getProgram } from '../../src/analysis/typescript/program';

function analyze(text: string, uri = 'file:///review.ts') {
  return analyzeDocument({
    uri, text, version: 1, tier: 'fast',
  });
}

describe('review findings', () => {
  it('collects a named default export', () => {
    const result = analyze(
      'export default function scan(xs: number[]) {\n'
      + '  let n = 0;\n'
      + '  for (const x of xs) n += x;\n'
      + '  return n;\n'
      + '}\n',
      'file:///default-named.ts',
    );
    const found = result.functions.find((f) => f.name === 'scan');
    expect(found, 'scan').toBeDefined();
    expect(found!.time).toBe('O(n)');
  });

  it('names an anonymous default export default', () => {
    const result = analyze(
      'export default function(xs: number[]) {\n'
      + '  let n = 0;\n'
      + '  for (const x of xs) n += x;\n'
      + '  return n;\n'
      + '}\n',
      'file:///default-anon.ts',
    );
    expect(result.functions[0]?.name).toBe('default');
  });

  it('collects object-literal and string method names', () => {
    const text = [
      'const obj = {',
      '  scan(xs: number[]) {',
      '    let n = 0;',
      '    for (const x of xs) n += x;',
      '    return n;',
      '  },',
      "  'walk'(xs: number[]) {",
      '    let n = 0;',
      '    for (const x of xs) n += x;',
      '    return n;',
      '  },',
      "  ['run'](xs: number[]) {",
      '    let n = 0;',
      '    for (const x of xs) n += x;',
      '    return n;',
      '  },',
      '};',
    ].join('\n');
    const loaded = getProgram('file:///methods.ts', text, false);
    const names = collectFunctions(loaded.source).map((f) => f.name);
    expect(names).toEqual(expect.arrayContaining(['scan', 'walk', 'run']));
  });

  it('counts two helper calls as O(n)+O(n)=O(n)', () => {
    const result = analyze(
      [
        'function scan(xs: number[]) {',
        '  let n = 0;',
        '  for (const x of xs) n += x;',
        '  return n;',
        '}',
        'export function twice(xs: number[]) {',
        '  return scan(xs) + scan(xs);',
        '}',
      ].join('\n'),
      'file:///twice.ts',
    );
    const found = result.functions.find((f) => f.name === 'twice');
    expect(found, 'twice').toBeDefined();
    expect(found!.time).toBe('O(n)');
  });

  it('throws cancelled between functions when aborted', () => {
    let n = 0;
    expect(() => analyzeDocument({
      uri: 'file:///multi.ts',
      text: 'export function a() { return 1; }\n'
        + 'export function b() { return 2; }\n',
      version: 1,
      tier: 'fast',
    }, () => ++n > 1)).toThrow('cancelled');
  });
});
