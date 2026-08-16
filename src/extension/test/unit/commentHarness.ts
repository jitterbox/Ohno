import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { analyzeDocument } from '../../src/analysis/typescript/analyze';
import type {
  AnalysisTier,
  FunctionComplexity,
} from '../../src/analysis/types';

const root = resolve(
  dirname(fileURLToPath(import.meta.url)),
  '../../../..',
);

const Expected = /\/\/ expected:\s*(O\(.+?\)|C\(.+?\))\s*\/\s*(O\(.+?\)|C\(.+?\))\s*$/;

export interface ExpectedCase {
  name: string;
  time: string;
  space: string;
}

export function repoPath(rel: string): string {
  return resolve(root, rel);
}

export function parseExpected(text: string): ExpectedCase[] {
  const lines = text.split(/\r?\n/);
  const cases: ExpectedCase[] = [];
  for (let i = 0; i < lines.length; i++) {
    const match = Expected.exec(lines[i]);
    if (!match) continue;
    const name = nextExportName(lines, i + 1);
    if (!name) continue;
    cases.push({
      name,
      time: match[1].trim(),
      space: match[2].trim(),
    });
  }
  return cases;
}

export function analyzeFile(
  rel: string,
  uri: string,
  tier: AnalysisTier = 'fast',
): { text: string; functions: FunctionComplexity[] } {
  const text = readFileSync(repoPath(rel), 'utf8');
  const result = analyzeDocument({
    uri,
    text,
    version: 1,
    tier,
  });
  return { text, functions: result.functions };
}

export function byName(
  functions: FunctionComplexity[],
  name: string,
): FunctionComplexity | undefined {
  return functions.find((item) => item.name === name);
}

function nextExportName(
  lines: string[],
  start: number,
): string | undefined {
  for (let i = start; i < lines.length && i < start + 6; i++) {
    const fn = /export\s+function\s*\*?\s*([A-Za-z_][A-Za-z0-9_]*)/
      .exec(lines[i]);
    if (fn) return fn[1];
    const cnst = /export\s+const\s+([A-Za-z_][A-Za-z0-9_]*)\s*=/
      .exec(lines[i]);
    if (cnst) return cnst[1];
  }
  return undefined;
}
