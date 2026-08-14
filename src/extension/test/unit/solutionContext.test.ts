import { describe, expect, it } from 'vitest';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { projectNear, SolutionBinder } from '../../src/analysis/solutionContext';

describe('projectNear', () => {
  it('walks up to the nearest csproj', () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), 'ohno-proj-'));
    try {
      const src = path.join(root, 'src');
      fs.mkdirSync(src);
      fs.writeFileSync(path.join(root, 'Lib.csproj'), '<Project />');
      const file = path.join(src, 'Use.cs');
      fs.writeFileSync(file, '');
      expect(projectNear(file)).toBe(path.join(root, 'Lib.csproj'));
    } finally {
      fs.rmSync(root, { recursive: true, force: true });
    }
  });

  it('prefers a sln in the same directory', () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), 'ohno-sln-'));
    try {
      fs.writeFileSync(path.join(root, 'App.csproj'), '<Project />');
      fs.writeFileSync(path.join(root, 'App.sln'), '');
      const file = path.join(root, 'Use.cs');
      fs.writeFileSync(file, '');
      expect(projectNear(file)).toBe(path.join(root, 'App.sln'));
    } finally {
      fs.rmSync(root, { recursive: true, force: true });
    }
  });

  it('prefers a sln above the nearest csproj', () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), 'ohno-above-'));
    try {
      const lib = path.join(root, 'Lib');
      fs.mkdirSync(lib);
      fs.writeFileSync(path.join(root, 'App.sln'), '');
      fs.writeFileSync(path.join(lib, 'Lib.csproj'), '<Project />');
      const file = path.join(lib, 'Use.cs');
      fs.writeFileSync(file, '');
      expect(projectNear(file)).toBe(path.join(root, 'App.sln'));
    } finally {
      fs.rmSync(root, { recursive: true, force: true });
    }
  });
});

describe('SolutionBinder', () => {
  it('does not replace a sln with a csproj', async () => {
    const calls: string[] = [];
    const binder = new SolutionBinder(async (p) => {
      calls.push(p);
    });
    await binder.bind('/tmp/App.sln');
    await binder.bind('/tmp/Lib.csproj');
    expect(calls).toEqual(['/tmp/App.sln']);
  });

  it('retries a failed bind', async () => {
    const calls: string[] = [];
    let fail = true;
    const binder = new SolutionBinder(async (p) => {
      calls.push(p);
      if (fail) throw new Error('open failed');
    });
    await expect(binder.bind('/tmp/Lib.csproj')).rejects.toThrow();
    fail = false;
    await binder.bind('/tmp/Lib.csproj');
    expect(calls).toEqual(['/tmp/Lib.csproj', '/tmp/Lib.csproj']);
  });
});
