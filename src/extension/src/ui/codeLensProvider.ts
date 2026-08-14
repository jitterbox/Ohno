import * as vscode from 'vscode';
import { readConfig } from '../config';
import type { ResultStore } from './resultStore';
import { headlineText } from './decorationFactory';

export class ComplexityCodeLensProvider implements vscode.CodeLensProvider {
  private readonly emitter = new vscode.EventEmitter<void>();
  readonly onDidChangeCodeLenses = this.emitter.event;

  constructor(private readonly store: ResultStore) {}

  refresh(): void {
    this.emitter.fire();
  }

  provideCodeLenses(document: vscode.TextDocument): vscode.CodeLens[] {
    const config = readConfig();
    if (config.mode !== 'codelens') return [];
    const file = this.store.get(document.uri);
    if (!file) return [];
    return file.functions.map((fn) => {
      const range = new vscode.Range(
        fn.signatureRange.startLine,
        fn.signatureRange.startCharacter,
        fn.signatureRange.startLine,
        fn.signatureRange.startCharacter,
      );
      return new vscode.CodeLens(range, {
        title: headlineText(fn, config).trim(),
        command: 'ohno.showDerivation',
        arguments: [document.uri.toString(), fn.id],
      });
    });
  }
}
