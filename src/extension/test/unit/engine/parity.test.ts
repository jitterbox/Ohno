import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import {
  format,
  formatBigO,
  parseVector,
  simplify,
  type VectorExpr,
} from '../../../src/analysis/typescript/engine';

interface VectorFile {
  vectors: {
    id: string;
    expr: VectorExpr;
    simplified: string;
    bigO: string;
  }[];
}

const file = JSON.parse(
  readFileSync(
    resolve(
      dirname(fileURLToPath(import.meta.url)),
      '../../../../shared/algebra-vectors.json',
    ),
    'utf8',
  ),
) as VectorFile;

describe('algebra golden parity', () => {
  it('has unique vector ids', () => {
    const ids = file.vectors.map((v) => v.id);
    expect(new Set(ids).size).toBe(ids.length);
  });

  for (const vector of file.vectors) {
    it(vector.id, () => {
      const simplified = simplify(parseVector(vector.expr));
      expect(format(simplified)).toBe(vector.simplified);
      expect(formatBigO(simplified)).toBe(vector.bigO);
    });
  }
});
