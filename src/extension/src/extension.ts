import * as vscode from 'vscode';
import { readConfig } from './config';
import { AnalyzerRegistry } from './analysis/registry';
import { CSharpAnalyzer } from './analysis/csharpAnalyzer';
import { TypeScriptAnalyzer } from './analysis/typescriptAnalyzer';
import { AnalyzerRpcClient, resolveServerPath } from './analysis/rpcClient';
import { AnnotationController } from './ui/annotationController';
import { ComplexityHoverProvider } from './ui/hoverProvider';
import { ComplexityCodeLensProvider } from './ui/codeLensProvider';
import { ResultStore } from './ui/resultStore';
import { showDerivation } from './ui/derivationPanel';

export function activate(context: vscode.ExtensionContext): void {
  const output = vscode.window.createOutputChannel('Ohno');
  const store = new ResultStore();
  const registry = new AnalyzerRegistry();
  const config = readConfig();
  const serverPath = resolveServerPath(
    context.extensionPath,
    config.analyzerPath,
  );
  const client = new AnalyzerRpcClient(serverPath, (m) => output.appendLine(m));
  registry.register(new CSharpAnalyzer(client));
  registry.register(new TypeScriptAnalyzer());

  const annotations = new AnnotationController(
    registry,
    store,
    context.extensionPath,
    output,
  );
  const lenses = new ComplexityCodeLensProvider(store);

  context.subscriptions.push(
    output,
    registry,
    annotations,
    vscode.languages.registerHoverProvider(
      [{ language: 'csharp' }, { language: 'typescript' }],
      new ComplexityHoverProvider(store),
    ),
    vscode.languages.registerCodeLensProvider(
      [{ language: 'csharp' }, { language: 'typescript' }],
      lenses,
    ),
    vscode.commands.registerCommand('ohno.runDeepAnalysis', async (uri?: string) => {
      await annotations.runDeep(uri ? vscode.Uri.parse(uri) : undefined);
      lenses.refresh();
    }),
    vscode.commands.registerCommand(
      'ohno.showDerivation',
      (uri?: string, id?: string) => {
        const editor = vscode.window.activeTextEditor;
        if (!editor) return;
        const target = uri ? vscode.Uri.parse(uri) : editor.document.uri;
        const fn = id
          ? store.functionById(target, id)
          : store.functionAt(target, editor.selection.active);
        if (fn) showDerivation(fn);
      },
    ),
    vscode.commands.registerCommand('ohno.toggleAnnotations', async () => {
      const cfg = vscode.workspace.getConfiguration('ohno');
      await cfg.update('enabled', !cfg.get('enabled', true), true);
      annotations.refresh();
    }),
    vscode.commands.registerCommand('ohno.copyComplexity', async () => {
      const editor = vscode.window.activeTextEditor;
      if (!editor) return;
      const fn = store.functionAt(editor.document.uri, editor.selection.active);
      if (!fn) return;
      await vscode.env.clipboard.writeText(`${fn.time} · ${fn.space}`);
    }),
  );

  if (process.env.OHNO_TEST) {
    context.subscriptions.push(
      vscode.commands.registerCommand('ohno._getAnnotationState', () =>
        annotations.snapshot(),
      ),
    );
  }

  const folder = vscode.workspace.workspaceFolders?.[0];
  if (folder) {
    const sln = vscode.workspace.findFiles('*.sln', undefined, 1).then((files) => {
      if (files[0]) {
        void client.setSolution(files[0].fsPath).catch((error) => {
          output.appendLine(`setSolution failed: ${String(error)}`);
        });
      }
    });
    context.subscriptions.push({ dispose: () => void sln });
  }

  annotations.refresh();
}

export function deactivate(): void {
  // Subscriptions dispose the registry and RPC client.
}
