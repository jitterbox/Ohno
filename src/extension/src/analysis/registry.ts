import type { IComplexityAnalyzer } from './analyzer';

export class AnalyzerRegistry {
  private readonly analyzers = new Map<string, IComplexityAnalyzer>();

  register(analyzer: IComplexityAnalyzer): void {
    for (const id of analyzer.languageIds) {
      this.analyzers.set(id, analyzer);
    }
  }

  get(languageId: string): IComplexityAnalyzer | undefined {
    return this.analyzers.get(languageId);
  }

  dispose(): void {
    for (const analyzer of new Set(this.analyzers.values())) {
      analyzer.dispose?.();
    }
    this.analyzers.clear();
  }
}
