export class Uri {
  constructor(public readonly fsPath: string) {}
  toString(): string {
    return this.fsPath;
  }
  static file(p: string): Uri {
    return new Uri(p);
  }
  static parse(value: string): Uri {
    return new Uri(value);
  }
}

export class Range {
  constructor(
    public startLine: number,
    public startCharacter: number,
    public endLine: number,
    public endCharacter: number,
  ) {}
}

export class Position {
  constructor(public line: number, public character: number) {}
}

export class ThemeColor {
  constructor(public id: string) {}
}

export class MarkdownString {
  value = '';
  isTrusted = false;
  constructor(_value?: string, _support?: boolean) {}
  appendMarkdown(text: string): this {
    this.value += text;
    return this;
  }
}

export class EventEmitter<T> {
  event = (): void => undefined;
  fire(_value?: T): void {}
}

export const window = {
  createTextEditorDecorationType: () => ({ dispose() {} }),
  createOutputChannel: () => ({
    appendLine() {},
    dispose() {},
  }),
  activeTextEditor: undefined,
  onDidChangeActiveTextEditor: () => ({ dispose() {} }),
  onDidChangeActiveColorTheme: () => ({ dispose() {} }),
  createWebviewPanel: () => ({ webview: { html: '' } }),
};

export const workspace = {
  getConfiguration: () => ({
    get: (_key: string, fallback: unknown) => fallback,
    update: async () => undefined,
  }),
  onDidChangeTextDocument: () => ({ dispose() {} }),
  onDidChangeConfiguration: () => ({ dispose() {} }),
  workspaceFolders: [],
  findFiles: async () => [],
};

export const languages = {
  registerHoverProvider: () => ({ dispose() {} }),
  registerCodeLensProvider: () => ({ dispose() {} }),
};

export const commands = {
  registerCommand: () => ({ dispose() {} }),
};

export const env = {
  clipboard: { writeText: async () => undefined },
};

export class Disposable {
  static from(...items: { dispose(): void }[]): Disposable {
    return new Disposable(() => items.forEach((i) => i.dispose()));
  }
  constructor(private readonly fn?: () => void) {}
  dispose(): void {
    this.fn?.();
  }
}

export const ViewColumn = { Beside: 1 };
