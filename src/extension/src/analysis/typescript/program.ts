import * as fs from 'node:fs';
import * as path from 'node:path';
import { fileURLToPath } from 'node:url';
import ts from 'typescript';

const programs = new Map<string, ts.Program>();

export function scriptKindOf(uri: string): ts.ScriptKind {
  if (uri.endsWith('.tsx')) return ts.ScriptKind.TSX;
  if (uri.endsWith('.jsx')) return ts.ScriptKind.JSX;
  if (uri.endsWith('.js')) return ts.ScriptKind.JS;
  return ts.ScriptKind.TS;
}

export function fileNameOf(uri: string): string {
  try {
    return fileURLToPath(uri);
  } catch {
    return uri.replace(/^file:\/\//, '');
  }
}

export function findConfig(filePath: string): string | undefined {
  let dir = path.dirname(filePath);
  while (true) {
    for (const name of ['tsconfig.json', 'jsconfig.json']) {
      const candidate = path.join(dir, name);
      if (fs.existsSync(candidate)) return candidate;
    }
    const parent = path.dirname(dir);
    if (parent === dir) return undefined;
    dir = parent;
  }
}

export function createAdHocProgram(
  fileName: string,
  text: string,
  kind: ts.ScriptKind,
): ts.Program {
  const options: ts.CompilerOptions = {
    target: ts.ScriptTarget.ESNext,
    module: ts.ModuleKind.ESNext,
    lib: ['lib.esnext.d.ts'],
    allowJs: true,
    noEmit: true,
    skipLibCheck: true,
  };
  const source = ts.createSourceFile(
    fileName, text, ts.ScriptTarget.ESNext, true, kind,
  );
  return createOverlayProgram([fileName], options, fileName, source);
}

export function createProjectProgram(
  configPath: string,
  fileName: string,
  text: string,
  kind: ts.ScriptKind,
): ts.Program | undefined {
  const read = ts.readConfigFile(configPath, (p) => ts.sys.readFile(p));
  if (read.error) return undefined;
  const parsed = ts.parseJsonConfigFileContent(
    read.config,
    ts.sys,
    path.dirname(configPath),
  );
  parsed.options.noEmit = true;
  parsed.options.allowJs = true;
  const source = ts.createSourceFile(
    fileName, text, ts.ScriptTarget.ESNext, true, kind,
  );
  const roots = parsed.fileNames.includes(fileName)
    ? parsed.fileNames
    : [...parsed.fileNames, fileName];
  const old = programs.get(configPath);
  const program = createOverlayProgram(
    roots, parsed.options, fileName, source, old,
  );
  programs.set(configPath, program);
  return program;
}

export function getProgram(
  uri: string,
  text: string,
  preferProject: boolean,
): { program: ts.Program; source: ts.SourceFile; fallback: boolean } {
  const fileName = fileNameOf(uri);
  const kind = scriptKindOf(uri);
  const config = preferProject ? findConfig(fileName) : findConfig(fileName);
  if (config) {
    const project = createProjectProgram(config, fileName, text, kind);
    const source = project?.getSourceFile(fileName);
    if (project && source) {
      return { program: project, source, fallback: false };
    }
  }
  const program = createAdHocProgram(fileName, text, kind);
  const source = program.getSourceFile(fileName)
    ?? program.getSourceFiles()[0];
  return { program, source, fallback: !!config };
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

function sameFile(left: string, right: string): boolean {
  return path.normalize(left) === path.normalize(right);
}
