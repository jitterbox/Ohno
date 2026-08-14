import * as vscode from 'vscode';
import type { AnalyzeResponse, FunctionComplexity } from '../analysis/types';
import type { DeepRun } from './deepDiff';

export class ResultStore {
  private readonly byUri = new Map<string, AnalyzeResponse>();
  private readonly deepRuns = new Map<string, DeepRun>();
  private readonly emitter = new vscode.EventEmitter<string>();
  readonly onDidChange = this.emitter.event;

  set(response: AnalyzeResponse): void {
    this.byUri.set(response.uri, response);
    this.emitter.fire(response.uri);
  }

  setDeepRun(uri: string, run: DeepRun): void {
    this.deepRuns.set(deepKey(uri, run.functionId), run);
    this.emitter.fire(uri);
  }

  setDeepRuns(uri: string, runs: DeepRun[]): void {
    for (const run of runs) {
      this.deepRuns.set(deepKey(uri, run.functionId), run);
    }
    this.emitter.fire(uri);
  }

  clearDeepRun(uri: string, functionId: string): void {
    if (this.deepRuns.delete(deepKey(uri, functionId))) {
      this.emitter.fire(uri);
    }
  }

  deepRunFor(uri: vscode.Uri, functionId: string): DeepRun | undefined {
    return this.deepRuns.get(deepKey(uri.toString(), functionId));
  }

  clearDeepRuns(uri?: vscode.Uri): void {
    if (!uri) {
      this.deepRuns.clear();
      this.emitter.fire('');
      return;
    }
    const prefix = `${uri.toString()}::`;
    for (const key of [...this.deepRuns.keys()]) {
      if (key.startsWith(prefix)) this.deepRuns.delete(key);
    }
    this.emitter.fire(uri.toString());
  }

  get(uri: vscode.Uri): AnalyzeResponse | undefined {
    return this.byUri.get(uri.toString());
  }

  functionAt(
    uri: vscode.Uri,
    position: vscode.Position,
  ): FunctionComplexity | undefined {
    const file = this.get(uri);
    if (!file) return undefined;
    return file.functions.find((fn) => contains(fn.range, position));
  }

  functionById(
    uri: vscode.Uri,
    id: string,
  ): FunctionComplexity | undefined {
    return this.get(uri)?.functions.find((fn) => fn.id === id);
  }

  snapshot(): AnalyzeResponse[] {
    return [...this.byUri.values()];
  }

  clear(uri?: vscode.Uri): void {
    if (uri) this.byUri.delete(uri.toString());
    else this.byUri.clear();
    this.clearDeepRuns(uri);
  }
}

function deepKey(uri: string, functionId: string): string {
  return `${uri}::${functionId}`;
}

function contains(
  range: FunctionComplexity['range'],
  position: vscode.Position,
): boolean {
  if (position.line < range.startLine || position.line > range.endLine) {
    return false;
  }
  if (position.line === range.startLine
    && position.character < range.startCharacter) {
    return false;
  }
  if (position.line === range.endLine
    && position.character > range.endCharacter) {
    return false;
  }
  return true;
}
