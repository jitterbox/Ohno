import * as vscode from 'vscode';
import { readConfig } from './config';
import { documentSelectors } from './analysis/languages';
import { AnalyzerRegistry } from './analysis/registry';
import { CSharpAnalyzer } from './analysis/csharpAnalyzer';
import { AnalyzerRpcClient, resolveServerPath } from './analysis/rpcClient';
import { SolutionBinder } from './analysis/solutionContext';
import { AnnotationController } from './ui/annotationController';
// import { ComplexityHoverProvider } from './ui/hoverProvider';
import { ComplexityCodeLensProvider } from './ui/codeLensProvider';
import { ResultStore } from './ui/resultStore';
import { ComplexityPanel } from './ui/complexityPanel';

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
  const binder = new SolutionBinder((p) => client.setSolution(p));
  registry.register(new CSharpAnalyzer(client, binder));

  const annotations = new AnnotationController(
    registry,
    store,
    context.extensionPath,
    output,
  );
  const lenses = new ComplexityCodeLensProvider(store);
  const panel = new ComplexityPanel(store);

  context.subscriptions.push(
    output,
    registry,
    annotations,
    lenses,
    panel,
    // vscode.languages.registerHoverProvider(
    //   documentSelectors(),
    //   new ComplexityHoverProvider(store),
    // ),
    vscode.languages.registerCodeLensProvider(
      documentSelectors(),
      lenses,
    ),
    vscode.commands.registerCommand('ohno.runDeepAnalysis', async (uri?: string) => {
      await vscode.commands.executeCommand(
        'workbench.view.extension.ohno',
      );
      await annotations.runDeep(uri ? vscode.Uri.parse(uri) : undefined);
      lenses.refresh();
    }),
    vscode.commands.registerCommand(
      'ohno.showDerivation',
      async (uri?: string, id?: string) => {
        await focusFunction(store, uri, id);
      },
    ),
    vscode.commands.registerCommand('ohno.focusComplexity', async () => {
      await vscode.commands.executeCommand(
        'workbench.view.extension.ohno',
      );
    }),
    vscode.commands.registerCommand('ohno.toggleAnnotations', async () => {
      const cfg = vscode.workspace.getConfiguration('ohno');
      await cfg.update('enabled', !cfg.get('enabled', true), true);
      annotations.refresh();
    }),
    vscode.commands.registerCommand('ohno.copyComplexity', async () => {
      const editor = vscode.window.activeTextEditor;
      if (!editor) return;
      const fn = store.selectionFor(editor.document.uri)?.function
        ?? store.functionAt(editor.document.uri, editor.selection.active);
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

  context.subscriptions.push(bindSolution(binder, output));

  annotations.refresh();
}

async function focusFunction(
  store: ResultStore,
  uri?: string,
  id?: string,
): Promise<void> {
  await vscode.commands.executeCommand('workbench.view.extension.ohno');
  const editor = vscode.window.activeTextEditor;
  if (!editor) return;
  const target = uri ? vscode.Uri.parse(uri) : editor.document.uri;
  const fn = id
    ? store.functionById(target, id)
    : store.functionAt(target, editor.selection.active);
  if (!fn) return;
  await vscode.commands.executeCommand(
    'ohno.revealEvidence',
    target.toString(),
    fn.signatureRange,
  );
}

function bindSolution(
  binder: SolutionBinder,
  output: vscode.OutputChannel,
): vscode.Disposable {
  const work = vscode.workspace.findFiles(
    '**/*.{sln,slnx}',
    '{**/node_modules/**,**/bin/**,**/obj/**}',
    1,
  ).then((files) => {
    if (!files[0]) return;
    void binder.bind(files[0].fsPath).catch((error) => {
      output.appendLine(`setSolution failed: ${String(error)}`);
    });
  });
  return { dispose: () => void work };
}

export function deactivate(): void {
  // Subscriptions dispose the registry and RPC client.
}
