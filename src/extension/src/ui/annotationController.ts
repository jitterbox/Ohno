/**
 * Adapted from GitLens (MIT), src/annotations/lineAnnotationController.ts
 * Copyright (c) 2016-2021 Eric Amodio
 * Copyright (c) 2021-2026 Axosoft, LLC dba GitKraken
 */
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import * as vscode from 'vscode';
import { languageEnabled, readConfig, type OhnoConfig } from '../config';
import type { AnalyzerRegistry } from '../analysis/registry';
import type { EvidenceNode, FunctionComplexity } from '../analysis/types';
import {
  createDecorationSet,
  disposeDecorationSet,
  headlineText,
  lineRange,
  type DecorationSet,
} from './decorationFactory';
import { buildMarkdown } from './hoverProvider';
import type { ResultStore } from './resultStore';

export class AnnotationController implements vscode.Disposable {
  private decorations: DecorationSet;
  private timer: ReturnType<typeof setTimeout> | undefined;
  private cancellation: vscode.CancellationTokenSource | undefined;
  private version = 0;
  private readonly disposable: vscode.Disposable;

  constructor(
    private readonly registry: AnalyzerRegistry,
    private readonly store: ResultStore,
    private readonly extensionPath: string,
    private readonly output: vscode.OutputChannel,
  ) {
    this.decorations = createDecorationSet(extensionPath);
    this.disposable = vscode.Disposable.from(
      vscode.workspace.onDidChangeTextDocument((e) => this.onEdit(e)),
      vscode.window.onDidChangeActiveTextEditor(() => this.refresh()),
      vscode.workspace.onDidChangeConfiguration((e) => {
        if (e.affectsConfiguration('ohno')) this.refresh();
      }),
      vscode.window.onDidChangeActiveColorTheme(() => this.recreate()),
    );
  }

  dispose(): void {
    this.cancellation?.cancel();
    if (this.timer) clearTimeout(this.timer);
    disposeDecorationSet(this.decorations);
    this.disposable.dispose();
  }

  refresh(): void {
    const config = readConfig();
    const editor = vscode.window.activeTextEditor;
    if (!editor) return;
    this.schedule(editor, config);
  }

  async runDeep(uri?: vscode.Uri): Promise<void> {
    const editor = vscode.window.activeTextEditor;
    if (!editor) return;
    const target = uri ?? editor.document.uri;
    if (editor.document.uri.toString() !== target.toString()) return;
    const config = { ...readConfig(), tier: 'deep' as const };
    await this.analyze(editor, config);
  }

  snapshot() {
    return this.store.snapshot();
  }

  private onEdit(e: vscode.TextDocumentChangeEvent): void {
    const editor = vscode.window.activeTextEditor;
    if (!editor || editor.document !== e.document) return;
    this.clear(editor);
    this.refresh();
  }

  private schedule(editor: vscode.TextEditor, config: OhnoConfig): void {
    if (this.timer) clearTimeout(this.timer);
    this.timer = setTimeout(() => {
      void this.analyze(editor, config);
    }, config.debounceMs);
  }

  private async analyze(
    editor: vscode.TextEditor,
    config: OhnoConfig,
  ): Promise<void> {
    const doc = editor.document;
    if (!languageEnabled(doc.languageId, config)) {
      this.clear(editor);
      return;
    }
    if (config.maxFileSizeKb > 0
      && doc.getText().length > config.maxFileSizeKb * 1024) {
      this.output.appendLine(`skip ${doc.uri.fsPath}: file too large`);
      return;
    }
    const analyzer = this.registry.get(doc.languageId);
    if (!analyzer) return;

    this.cancellation?.cancel();
    this.cancellation = new vscode.CancellationTokenSource();
    const token = this.cancellation.token;
    const ticket = ++this.version;

    try {
      const response = await analyzer.analyze({
        uri: doc.uri.toString(),
        text: doc.getText(),
        version: doc.version,
        tier: config.tier,
      }, token);
      if (token.isCancellationRequested || ticket !== this.version) return;
      this.store.set(response);
      writeTestOutput(response);
      this.apply(editor, response.functions ?? [], config);
    } catch (error) {
      if (token.isCancellationRequested) return;
      this.output.appendLine(`analyze failed: ${String(error)}`);
    }
  }

  private apply(
    editor: vscode.TextEditor,
    functions: FunctionComplexity[],
    config: OhnoConfig,
  ): void {
    if (config.mode === 'off' || config.mode === 'codelens') {
      this.clear(editor);
      return;
    }

    const headlines: vscode.DecorationOptions[] = [];
    const nested: vscode.DecorationOptions[] = [];
    const gutters: Record<string, vscode.DecorationOptions[]> = {
      high: [], medium: [], low: [], unknown: [],
    };

    for (const fn of functions) {
      const line = fn.signatureRange.startLine;
      const hover = buildMarkdown(fn, editor.document.uri);
      headlines.push({
        range: lineRange(editor.document, line),
        renderOptions: {
          after: {
            contentText: headlineText(fn, config),
            color: new vscode.ThemeColor(
              `ohno.confidence${capitalize(fn.confidence)}`,
            ),
            textDecoration: 'none',
          },
        },
        hoverMessage: hover,
      });
      gutters[fn.confidence].push({
        range: new vscode.Range(line, 0, line, 0),
      });
      collectNested(fn.evidence, 1, config.nestingDepth, nested, editor);
    }

    editor.setDecorations(this.decorations.headline, headlines);
    editor.setDecorations(this.decorations.nested, nested);
    editor.setDecorations(this.decorations.gutters.high, gutters.high);
    editor.setDecorations(this.decorations.gutters.medium, gutters.medium);
    editor.setDecorations(this.decorations.gutters.low, gutters.low);
    editor.setDecorations(this.decorations.gutters.unknown, gutters.unknown);
  }

  private clear(editor: vscode.TextEditor): void {
    editor.setDecorations(this.decorations.headline, []);
    editor.setDecorations(this.decorations.nested, []);
    editor.setDecorations(this.decorations.gutters.high, []);
    editor.setDecorations(this.decorations.gutters.medium, []);
    editor.setDecorations(this.decorations.gutters.low, []);
    editor.setDecorations(this.decorations.gutters.unknown, []);
  }

  private recreate(): void {
    disposeDecorationSet(this.decorations);
    this.decorations = createDecorationSet(this.extensionPath);
    this.refresh();
  }
}

function collectNested(
  node: EvidenceNode,
  depth: number,
  maxDepth: number,
  sink: vscode.DecorationOptions[],
  editor: vscode.TextEditor,
): void {
  if (depth > maxDepth) return;
  if (node.range && (node.kind === 'loop' || node.kind === 'conditional')) {
    sink.push({
      range: lineRange(editor.document, node.range.startLine),
      renderOptions: {
        after: {
          contentText: `  ${node.label}: ${node.cost}`,
          color: new vscode.ThemeColor('ohno.nestedForeground'),
        },
      },
    });
  }
  for (const child of node.children ?? []) {
    collectNested(child, depth + 1, maxDepth, sink, editor);
  }
}

function writeTestOutput(response: unknown): void {
  if (!process.env.OHNO_TEST) return;
  const dest = process.env.OHNO_TEST_OUTPUT
    ?? path.join(os.tmpdir(), 'ohno-last-result.json');
  fs.writeFileSync(dest, JSON.stringify(response, null, 2));
}

function capitalize(value: string): string {
  return value.charAt(0).toUpperCase() + value.slice(1);
}
