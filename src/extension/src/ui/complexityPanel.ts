import * as vscode from 'vscode';
import type { FunctionComplexity, LineRange } from '../analysis/types';
import { buildPanelModel, type ComplexityItem } from './complexityModel';
import {
  matchEvidence,
  pickFunction,
  primaryHit,
  withFunctionRange,
} from './evidenceMatch';
import { ItemTreeProvider } from './complexityTree';
import { normalizeRange, rangeContains } from './ranges';
import type { ResultStore } from './resultStore';

export class ComplexityPanel implements vscode.Disposable {
  private readonly summary = new ItemTreeProvider();
  private readonly derivation = new ItemTreeProvider();
  private readonly treeView: vscode.TreeView<ComplexityItem>;
  private readonly disposable: vscode.Disposable;
  private timer: ReturnType<typeof setTimeout> | undefined;
  private revealing = false;
  private clickedId: string | undefined;
  private revealedKey: string | undefined;

  constructor(private readonly store: ResultStore) {
    const summaryView = vscode.window.createTreeView('ohno.summary', {
      treeDataProvider: this.summary,
    });
    this.treeView = vscode.window.createTreeView('ohno.derivation', {
      treeDataProvider: this.derivation,
      showCollapseAll: true,
    });
    this.disposable = vscode.Disposable.from(
      summaryView,
      this.treeView,
      this.summary,
      this.derivation,
      this.store.onDidChange(() => this.refresh()),
      vscode.window.onDidChangeActiveTextEditor(() => this.refresh()),
      vscode.window.onDidChangeTextEditorSelection(() =>
        this.onSelection()),
      vscode.commands.registerCommand(
        'ohno.revealEvidence',
        (uri: string, range: LineRange, itemId?: string) =>
          this.revealRange(uri, range, itemId),
      ),
    );
    this.refresh();
  }

  dispose(): void {
    if (this.timer) clearTimeout(this.timer);
    this.disposable.dispose();
  }

  refresh(): void {
    const editor = vscode.window.activeTextEditor;
    const fn = editor ? this.functionFor(editor) : undefined;
    if (!editor || !fn) {
      this.revealedKey = undefined;
      this.summary.setRoots([]);
      this.derivation.setRoots([]);
      return;
    }
    const hits = hitsForEditor(fn, editor);
    const highlighted = new Set(hits.map((h) => h.id));
    const model = buildPanelModel(
      fn,
      editor.document.uri.toString(),
      highlighted,
      this.store.deepRunFor(editor.document.uri, fn.id),
    );
    this.summary.setRoots(model.summary);
    this.derivation.setRoots(model.derivation);
    this.revealInTree(fn.id, this.clickedId ?? primaryHit(hits)?.id);
  }

  private functionFor(
    editor: vscode.TextEditor,
  ): FunctionComplexity | undefined {
    const file = this.store.get(editor.document.uri);
    if (!file) return undefined;
    const sel = editor.selection;
    const range = normalizeRange(
      sel.start.line,
      sel.start.character,
      sel.end.line,
      sel.end.character,
    );
    if (!sel.isEmpty) {
      const snap = this.store.selectionFor(editor.document.uri);
      if (snap
        && snap.version === editor.document.version
        && rangeContains(
          snap.function.range, range.startLine, range.startCharacter)) {
        return snap.function;
      }
    }
    return pickFunction(
      file.functions,
      range,
      { line: sel.active.line, character: sel.active.character },
    );
  }

  private onSelection(): void {
    if (this.revealing) return;
    if (this.timer) clearTimeout(this.timer);
    this.timer = setTimeout(() => this.refresh(), 50);
  }

  private revealInTree(functionId: string, id?: string): void {
    const target = id ?? 'root';
    const key = `${functionId}:${target}`;
    if (key === this.revealedKey) return;
    if (!this.treeView.visible) return;
    const item = this.derivation.item(target);
    if (!item) return;
    this.revealedKey = key;
    void this.treeView.reveal(item, {
      select: true,
      focus: false,
      expand: true,
    }).then(undefined, () => undefined);
  }

  private async revealRange(
    uri: string,
    range: LineRange,
    itemId?: string,
  ): Promise<void> {
    this.clickedId = itemId;
    const editor = await showUri(uri);
    const start = new vscode.Position(range.startLine, range.startCharacter);
    const end = new vscode.Position(range.endLine, range.endCharacter);
    this.revealing = true;
    editor.selection = new vscode.Selection(start, end);
    editor.revealRange(
      new vscode.Range(start, end),
      vscode.TextEditorRevealType.InCenterIfOutsideViewport,
    );
    this.revealing = false;
    this.refresh();
    this.clickedId = undefined;
  }
}

function hitsForEditor(
  fn: FunctionComplexity,
  editor: vscode.TextEditor,
) {
  const root = withFunctionRange(fn);
  return editor.selections.flatMap((sel) => matchEvidence(
    root,
    normalizeRange(
      sel.start.line,
      sel.start.character,
      sel.end.line,
      sel.end.character,
    ),
  ));
}

async function showUri(uri: string): Promise<vscode.TextEditor> {
  const target = vscode.Uri.parse(uri);
  const visible = vscode.window.visibleTextEditors.find(
    (e) => e.document.uri.toString() === target.toString(),
  );
  if (visible) return visible;
  const doc = await vscode.workspace.openTextDocument(target);
  return vscode.window.showTextDocument(doc);
}
