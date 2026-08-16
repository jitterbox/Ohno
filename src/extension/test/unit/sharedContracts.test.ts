import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { ProtocolMethods } from '../../../shared/protocol';

const sharedDir = resolve(
  dirname(fileURLToPath(import.meta.url)),
  '../../../shared',
);

function readJson(name: string): unknown {
  return JSON.parse(readFileSync(resolve(sharedDir, name), 'utf8'));
}

describe('shared protocol schema', () => {
  it('lists the same methods as ProtocolMethods', () => {
    const schema = readJson('protocol.schema.json') as {
      methods: Record<string, unknown>;
    };
    expect(Object.keys(schema.methods).sort()).toEqual(
      Object.values(ProtocolMethods).slice().sort(),
    );
  });

  it('requires the FunctionComplexity fields the UI reads', () => {
    const schema = readJson('protocol.schema.json') as {
      definitions: { FunctionComplexity: { required: string[] } };
    };
    const required = schema.definitions.FunctionComplexity.required;
    for (const field of [
      'time',
      'space',
      'confidence',
      'approaches',
      'selectionHint',
      'confidenceReasons',
    ]) {
      expect(required).toContain(field);
    }
  });
});

describe('shared catalog snapshot', () => {
  it('has versioned entries keyed by type#member#arity', () => {
    const catalog = readJson('catalog.json') as {
      version: number;
      entries: { key: string; time: { size: string } }[];
    };
    expect(catalog.version).toBe(1);
    expect(catalog.entries.length).toBeGreaterThan(100);
    for (const entry of catalog.entries) {
      expect(entry.key).toMatch(/^.+#[^#]+#[0-9]+$/);
      expect(['constant', 'receiver', 'logReceiver']).toContain(
        entry.time.size,
      );
    }
  });
});

describe('shared algebra vectors', () => {
  it('has unique ids and O(...) headlines', () => {
    const file = readJson('algebra-vectors.json') as {
      vectors: { id: string; bigO: string; simplified: string }[];
    };
    const ids = file.vectors.map((v) => v.id);
    expect(new Set(ids).size).toBe(ids.length);
    for (const vector of file.vectors) {
      expect(vector.bigO.startsWith('O(')).toBe(true);
      expect(vector.simplified.length).toBeGreaterThan(0);
    }
  });
});
