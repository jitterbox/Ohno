import * as vscode from 'vscode';
import type { AnalyzeResponse, FunctionComplexity } from '../analysis/types';

export class ResultStore {
  private readonly byUri = new Map<string, AnalyzeResponse>();

  set(response: AnalyzeResponse): void {
    this.byUri.set(response.uri, response);
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
  }
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
