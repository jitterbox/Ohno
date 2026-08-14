import * as fs from 'node:fs';
import * as path from 'node:path';
import { fileURLToPath } from 'node:url';

export class SolutionBinder {
  private bound?: string;

  constructor(
    private readonly set: (path: string) => Promise<void>,
  ) {}

  async bind(projectPath: string | undefined): Promise<void> {
    if (!projectPath || projectPath === this.bound) return;
    if (isSln(this.bound) && !isSln(projectPath)) return;
    try {
      await this.set(projectPath);
    } catch (error) {
      this.bound = undefined;
      throw error;
    }
    this.bound = projectPath;
  }

  async bindFile(uri: string): Promise<void> {
    if (isSln(this.bound)) return;
    await this.bind(projectNear(filePathOf(uri)));
  }
}

export function projectNear(filePath: string): string | undefined {
  let dir = path.dirname(filePath);
  let csproj: string | undefined;
  while (true) {
    const sln = firstWithExt(dir, '.sln');
    if (sln) return sln;
    csproj ??= firstWithExt(dir, '.csproj');
    const parent = path.dirname(dir);
    if (parent === dir) return csproj;
    dir = parent;
  }
}

function firstWithExt(dir: string, ext: string): string | undefined {
  try {
    return fs.readdirSync(dir)
      .filter((name) => name.toLowerCase().endsWith(ext))
      .sort()
      .map((name) => path.join(dir, name))[0];
  } catch {
    return undefined;
  }
}

function filePathOf(uri: string): string {
  try {
    return fileURLToPath(uri);
  } catch {
    return uri;
  }
}

function isSln(value?: string): boolean {
  return !!value?.toLowerCase().endsWith('.sln');
}
