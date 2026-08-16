import * as fs from 'node:fs';
import * as path from 'node:path';
import { fileURLToPath } from 'node:url';
import ts from 'typescript';

const MaxProjects = 8;
const MaxAdHoc = 4;

interface ProjectEntry {
  program: ts.Program;
  used: number;
}

interface AdHocEntry {
  text: string;
  program: ts.Program;
  source: ts.SourceFile;
  used: number;
}

interface ConfigEntry {
  mtime: number;
  fileNames: string[];
  options: ts.CompilerOptions;
}

const projects = new Map<string, ProjectEntry>();
const adHoc = new Map<string, AdHocEntry>();
const configs = new Map<string, ConfigEntry>();
const realpaths = new Map<string, string>();

export function scriptKindOf(uri: string): ts.ScriptKind {
  if (uri.endsWith('.tsx')) return ts.ScriptKind.TSX;
  if (uri.endsWith('.jsx')) return ts.ScriptKind.JSX;
  if (uri.endsWith('.js')) return ts.ScriptKind.JS;
  return ts.ScriptKind.TS;
}

export function fileNameOf(uri: string): string {
  try {
    return resolveFile(fileURLToPath(uri));
  } catch {
    return resolveFile(uri.replace(/^file:\/\//, ''));
  }
}

export function findConfig(filePath: string): string | undefined {
  let dir = path.dirname(filePath);
  while (true) {
    for (const name of ['tsconfig.json', 'jsconfig.json']) {
      const candidate = path.join(dir, name);
      if (fs.existsSync(candidate)) return resolveFile(candidate);
    }
    const parent = path.dirname(dir);
    if (parent === dir) return undefined;
    dir = parent;
  }
}

export function getProgram(
  uri: string,
  text: string,
  preferProject: boolean,
): { program: ts.Program; source: ts.SourceFile; fallback: boolean } {
  const fileName = fileNameOf(uri);
  const kind = scriptKindOf(uri);
  if (preferProject) {
    const config = findConfig(fileName);
    if (config) {
      const project = createProjectProgram(config, fileName, text, kind);
      const source = project ? sourceOf(project, fileName) : undefined;
      if (project && source) {
        return { program: project, source, fallback: false };
      }
      return adHocProgram(fileName, text, kind, true);
    }
  }
  return adHocProgram(fileName, text, kind, false);
}

export function evictPrograms(): void {
  projects.clear();
  adHoc.clear();
  configs.clear();
  realpaths.clear();
}

function adHocProgram(
  fileName: string,
  text: string,
  kind: ts.ScriptKind,
  fallback: boolean,
): { program: ts.Program; source: ts.SourceFile; fallback: boolean } {
  const hit = adHoc.get(fileName);
  if (hit && hit.text === text) {
    hit.used = Date.now();
    return { program: hit.program, source: hit.source, fallback };
  }
  const program = createAdHoc(fileName, text, kind);
  const source = sourceOf(program, fileName)
    ?? program.getSourceFiles()[0];
  adHoc.set(fileName, { text, program, source, used: Date.now() });
  evictLru(adHoc, MaxAdHoc);
  return { program, source, fallback };
}

function createAdHoc(
  fileName: string,
  text: string,
  kind: ts.ScriptKind,
): ts.Program {
  const options: ts.CompilerOptions = {
    target: ts.ScriptTarget.ESNext,
    module: ts.ModuleKind.ESNext,
    lib: ['lib.esnext.d.ts'],
    allowJs: true,
    checkJs: true,
    noEmit: true,
    skipLibCheck: true,
  };
  const source = ts.createSourceFile(
    fileName, text, ts.ScriptTarget.ESNext, true, kind,
  );
  return createOverlayProgram([fileName], options, fileName, source);
}

function createProjectProgram(
  configPath: string,
  fileName: string,
  text: string,
  kind: ts.ScriptKind,
): ts.Program | undefined {
  const parsed = readConfig(configPath);
  if (!parsed) return undefined;
  const source = ts.createSourceFile(
    fileName, text, ts.ScriptTarget.ESNext, true, kind,
  );
  const roots = parsed.fileNames.some((f) => sameFile(f, fileName))
    ? parsed.fileNames
    : [...parsed.fileNames, fileName];
  const old = projects.get(configPath)?.program;
  const program = createOverlayProgram(
    roots, parsed.options, fileName, source, old,
  );
  projects.set(configPath, { program, used: Date.now() });
  evictLru(projects, MaxProjects);
  return program;
}

function readConfig(configPath: string): ConfigEntry | undefined {
  let mtime = 0;
  try {
    mtime = fs.statSync(configPath).mtimeMs;
  } catch {
    return undefined;
  }
  const cached = configs.get(configPath);
  if (cached && cached.mtime === mtime) return cached;
  const read = ts.readConfigFile(configPath, (p) => ts.sys.readFile(p));
  if (read.error) return undefined;
  const parsed = ts.parseJsonConfigFileContent(
    read.config,
    ts.sys,
    path.dirname(configPath),
  );
  parsed.options.noEmit = true;
  parsed.options.allowJs = true;
  const entry: ConfigEntry = {
    mtime,
    fileNames: parsed.fileNames.map(resolveFile),
    options: parsed.options,
  };
  configs.set(configPath, entry);
  return entry;
}

function createOverlayProgram(
  rootNames: string[],
  options: ts.CompilerOptions,
  fileName: string,
  source: ts.SourceFile,
  oldProgram?: ts.Program,
): ts.Program {
  const host = ts.createCompilerHost(options);
  const orig = host.getSourceFile.bind(host);
  host.getSourceFile = (name, ...rest) => {
    if (sameFile(name, fileName)) return source;
    return orig(name, ...rest);
  };
  return ts.createProgram(rootNames, options, host, oldProgram);
}

function sourceOf(
  program: ts.Program,
  fileName: string,
): ts.SourceFile | undefined {
  return program.getSourceFile(fileName)
    ?? program.getSourceFiles().find((f) => sameFile(f.fileName, fileName));
}

function sameFile(left: string, right: string): boolean {
  return resolveFile(left) === resolveFile(right);
}

function resolveFile(file: string): string {
  const hit = realpaths.get(file);
  if (hit) return hit;
  const resolved = path.normalize(path.resolve(file));
  let real = resolved;
  try {
    real = fs.realpathSync(resolved);
  } catch {
    real = resolved;
  }
  realpaths.set(file, real);
  return real;
}

function evictLru<T extends { used: number }>(
  map: Map<string, T>,
  max: number,
): void {
  if (map.size <= max) return;
  const oldest = [...map.entries()].sort((a, b) => a[1].used - b[1].used);
  for (const [key] of oldest.slice(0, map.size - max)) map.delete(key);
}
