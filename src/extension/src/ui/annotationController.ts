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
import type {
  AnalyzeResponse,
  EvidenceNode,
  FunctionComplexity,
} from '../analysis/types';
import { normalizeRange } from './ranges';
import {
  createDecorationSet,
  disposeDecorationSet,
  headlineRender,
  lineRange,
  nestedRender,
  type DecorationSet,
} from './decorationFactory';
// import { buildMarkdown } from './hoverProvider';
import {
  diffResponses,
  failedDeepRun,
  runningDeepRun,
} from './deepDiff';
import type { ResultStore } from './resultStore';

type AnalysisOutcome = 'ok' | 'cancelled' | 'error' | 'skipped';

export class AnnotationController implements vscode.Disposable {
  private decorations: DecorationSet;
  private timer: ReturnType<typeof setTimeout> | undefined;
  private selectionTimer: ReturnType<typeof setTimeout> | undefined;
  private cancellation: vscode.CancellationTokenSource | undefined;
  private selectionCancel: vscode.CancellationTokenSource | undefined;
  private selectionVersion = 0;
  private lastSelection = '';
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
      vscode.workspace.onDidCloseTextDocument((doc) => {
        this.store.clear(doc.uri);
      }),
      vscode.window.onDidChangeActiveTextEditor(() => this.refresh()),
      vscode.window.onDidChangeTextEditorSelection(() =>
        this.scheduleSelection()),
      vscode.workspace.onDidChangeConfiguration((e) => {
        if (e.affectsConfiguration('ohno')) this.refresh();
      }),
      vscode.window.onDidChangeActiveColorTheme(() => this.recreate()),
    );
  }

  dispose(): void {
    this.cancellation?.cancel();
    this.selectionCancel?.cancel();
    if (this.timer) clearTimeout(this.timer);
    if (this.selectionTimer) clearTimeout(this.selectionTimer);
    disposeDecorationSet(this.decorations);
    this.disposable.dispose();
  }

  refresh(): void {
    const editor = vscode.window.activeTextEditor;
    if (!editor) {
      if (this.timer) clearTimeout(this.timer);
      return;
    }
    this.schedule(readConfig());
    this.scheduleSelection();
  }

  async runDeep(uri?: vscode.Uri): Promise<void> {
    const editor = vscode.window.activeTextEditor;
    if (!editor) return;
    const target = uri ?? editor.document.uri;
    if (editor.document.uri.toString() !== target.toString()) return;
    const before = this.store.get(editor.document.uri);
    const current = this.store.functionAt(
      editor.document.uri,
      editor.selection.active,
    );
    if (current) {
      this.store.setDeepRun(
        editor.document.uri.toString(),
        runningDeepRun(current.id),
      );
    }
    const config = { ...readConfig(), tier: 'deep' as const };
    const outcome = await vscode.window.withProgress({
      location: vscode.ProgressLocation.Notification,
      title: 'Ohno: Deep analysis',
      cancellable: true,
    }, async (progress, token) => {
      progress.report({ message: 'Analyzing…' });
      return this.analyze(editor, config, token);
    });
    this.finishDeep(editor, before, current?.id, outcome);
  }

  snapshot() {
    return this.store.snapshot();
  }

  private onEdit(e: vscode.TextDocumentChangeEvent): void {
    const editor = vscode.window.activeTextEditor;
    if (!editor || editor.document !== e.document) return;
    this.refresh();
  }

  private schedule(config: OhnoConfig): void {
    if (this.timer) clearTimeout(this.timer);
    this.timer = setTimeout(() => {
      const editor = vscode.window.activeTextEditor;
      if (!editor) return;
      // Automatic analysis is always fast; deep runs are on demand only.
      void this.analyze(editor, { ...readConfig(), tier: 'fast' });
    }, config.debounceMs);
  }

  private scheduleSelection(): void {
    const editor = vscode.window.activeTextEditor;
    if (!editor) return;
    const uri = editor.document.uri;
    if (editor.selection.isEmpty) {
      this.lastSelection = '';
      this.store.clearSelection(uri);
      return;
    }
    const sel = editor.selection;
    const key = [uri.toString(), editor.document.version,
      sel.start.line, sel.start.character,
      sel.end.line, sel.end.character].join(':');
    if (key === this.lastSelection) return;
    this.lastSelection = key;
    if (this.selectionTimer) clearTimeout(this.selectionTimer);
    const wait = Math.min(readConfig().debounceMs, 200);
    this.selectionTimer = setTimeout(() => {
      const current = vscode.window.activeTextEditor;
      if (!current || current.selection.isEmpty) return;
      void this.analyzeSelection(current);
    }, wait);
  }

  private async analyzeSelection(
    editor: vscode.TextEditor,
  ): Promise<void> {
    if (editor.selection.isEmpty) {
      this.store.clearSelection(editor.document.uri);
      return;
    }
    const config = readConfig();
    const doc = editor.document;
    if (!languageEnabled(doc.languageId, config)) return;
    const analyzer = this.registry.get(doc.languageId);
    if (!analyzer) return;
    this.selectionCancel?.cancel();
    this.selectionCancel?.dispose();
    this.selectionCancel = new vscode.CancellationTokenSource();
    const token = this.selectionCancel.token;
    const ticket = ++this.selectionVersion;
    const sel = editor.selection;
    try {
      const response = await analyzer.analyze({
        uri: doc.uri.toString(),
        text: doc.getText(),
        version: doc.version,
        tier: 'fast',
        selection: normalizeRange(
          sel.start.line,
          sel.start.character,
          sel.end.line,
          sel.end.character,
        ),
      }, token);
      if (token.isCancellationRequested) return;
      if (ticket !== this.selectionVersion) return;
      if (editor.document.version !== response.version) return;
      const fn = response.functions[0];
      if (fn) {
        this.store.setSelection(doc.uri.toString(), doc.version, fn);
      }
    } catch {
      if (!token.isCancellationRequested) {
        this.output.appendLine('selection analyze failed');
      }
    }
  }

  private async analyze(
    editor: vscode.TextEditor,
    config: OhnoConfig,
    extra?: vscode.CancellationToken,
  ): Promise<AnalysisOutcome> {
    const doc = editor.document;
    if (!languageEnabled(doc.languageId, config)) {
      this.store.clear(doc.uri);
      this.clearDocument(doc.uri);
      return 'skipped';
    }
    const text = doc.getText();
    if (config.maxFileSizeKb > 0
      && text.length > config.maxFileSizeKb * 1024) {
      this.output.appendLine(`skip ${doc.uri.fsPath}: file too large`);
      this.store.clear(doc.uri);
      this.clearDocument(doc.uri);
      return 'skipped';
    }
    const analyzer = this.registry.get(doc.languageId);
    if (!analyzer) return 'skipped';

    this.cancellation?.cancel();
    this.cancellation = new vscode.CancellationTokenSource();
    if (extra?.isCancellationRequested) this.cancellation.cancel();
    const extraSub = extra?.onCancellationRequested(() => {
      this.cancellation?.cancel();
    });
    const token = this.cancellation.token;
    const ticket = ++this.version;

    try {
      const response = await analyzer.analyze({
        uri: doc.uri.toString(),
        text,
        version: doc.version,
        tier: config.tier,
      }, token);
      if (token.isCancellationRequested || ticket !== this.version) {
        return 'cancelled';
      }
      if (editor.document.version !== response.version) return 'cancelled';
      if (config.tier !== 'deep') this.store.clearDeepRuns(doc.uri);
      this.store.set(response);
      writeTestOutput(response);
      this.apply(editor, response.functions ?? [], config);
      return 'ok';
    } catch (error) {
      if (token.isCancellationRequested) return 'cancelled';
      this.output.appendLine(`analyze failed: ${String(error)}`);
      return 'error';
    } finally {
      extraSub?.dispose();
    }
  }

  private finishDeep(
    editor: vscode.TextEditor,
    before: AnalyzeResponse | undefined,
    functionId: string | undefined,
    outcome: AnalysisOutcome,
  ): void {
    const uri = editor.document.uri.toString();
    if (outcome === 'cancelled') {
      if (functionId) this.store.clearDeepRun(uri, functionId);
      return;
    }
    if (outcome !== 'ok') {
      this.store.setDeepRun(
        uri,
        failedDeepRun(functionId ?? 'unknown', 'See the Ohno output channel.'),
      );
      vscode.window.setStatusBarMessage('Ohno: Deep analysis failed', 6000);
      return;
    }
    const after = this.store.get(editor.document.uri);
    if (after) this.store.setDeepRuns(uri, diffResponses(before, after));
    const run = functionId
      ? this.store.deepRunFor(editor.document.uri, functionId)
      : undefined;
    vscode.window.setStatusBarMessage(
      `Ohno: Deep analysis complete — ${run?.summary ?? 'done'}`,
      6000,
    );
  }

  private apply(
    editor: vscode.TextEditor,
    functions: FunctionComplexity[],
    config: OhnoConfig,
  ): void {
    const uri = editor.document.uri;
    if (!config.showInline
      || config.mode === 'off'
      || config.mode === 'codelens') {
      this.clearDocument(uri);
      return;
    }

    const headlines: vscode.DecorationOptions[] = [];
    const nested: vscode.DecorationOptions[] = [];
    const gutters: Record<string, vscode.DecorationOptions[]> = {
      high: [], medium: [], low: [], unknown: [],
    };

    for (const fn of functions) {
      const line = fn.signatureRange.startLine;
      // const hover = buildMarkdown(fn, editor.document.uri);
      headlines.push({
        range: lineRange(editor.document, line),
        renderOptions: { after: headlineRender(fn, config) },
      });
      const gutter = gutters[fn.confidence] ?? gutters.unknown;
      gutter.push({
        range: new vscode.Range(line, 0, line, 0),
      });
      collectNested(fn.evidence, 1, config.nestingDepth, nested, editor);
    }

    for (const target of this.visibleEditorsOf(uri)) {
      target.setDecorations(this.decorations.headline, headlines);
      target.setDecorations(this.decorations.nested, nested);
      target.setDecorations(this.decorations.gutters.high, gutters.high);
      target.setDecorations(this.decorations.gutters.medium, gutters.medium);
      target.setDecorations(this.decorations.gutters.low, gutters.low);
      target.setDecorations(this.decorations.gutters.unknown, gutters.unknown);
    }
  }

  private clearDocument(uri: vscode.Uri): void {
    for (const editor of this.visibleEditorsOf(uri)) {
      this.clear(editor);
    }
  }

  private visibleEditorsOf(uri: vscode.Uri): vscode.TextEditor[] {
    const key = uri.toString();
    return vscode.window.visibleTextEditors.filter(
      (editor) => editor.document.uri.toString() === key,
    );
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
        after: nestedRender(node.label, node.cost),
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
  fs.promises.writeFile(dest, JSON.stringify(response, null, 2))
    .catch(() => undefined);
}
