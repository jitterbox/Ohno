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

export class ThemeIcon {
  constructor(public id: string) {}
}

export class TreeItem {
  id?: string;
  description?: string;
  tooltip?: string;
  iconPath?: ThemeIcon;
  contextValue?: string;
  command?: unknown;
  constructor(
    public label: unknown,
    public collapsibleState?: number,
  ) {}
}

export const TreeItemCollapsibleState = {
  None: 0,
  Collapsed: 1,
  Expanded: 2,
};

export class Selection {
  constructor(public start: Position, public end: Position) {}
}

export const TextEditorRevealType = {
  InCenterIfOutsideViewport: 2,
};

export class MarkdownString {
  value = '';
  isTrusted = false;
  constructor(value?: string, _support?: boolean) {
    this.value = value ?? '';
  }
  appendMarkdown(text: string): this {
    this.value += text;
    return this;
  }
}

export class EventEmitter<T> {
  event = (_listener?: unknown) => ({ dispose() {} });
  fire(_value?: T): void {}
  dispose(): void {}
}

export const window = {
  createTextEditorDecorationType: () => ({ dispose() {} }),
  createOutputChannel: () => ({
    appendLine() {},
    dispose() {},
  }),
  createTreeView: () => ({
    reveal: async () => undefined,
    dispose() {},
  }),
  activeTextEditor: undefined,
  visibleTextEditors: [],
  onDidChangeActiveTextEditor: () => ({ dispose() {} }),
  onDidChangeActiveColorTheme: () => ({ dispose() {} }),
  onDidChangeTextEditorSelection: () => ({ dispose() {} }),
  createWebviewPanel: () => ({ webview: { html: '' } }),
  showTextDocument: async () => ({}),
  withProgress: async (
    _opts: unknown,
    task: (progress: { report: (value: unknown) => void }) => unknown,
  ) => task({ report() {} }),
  setStatusBarMessage: () => ({ dispose() {} }),
};

export const workspace = {
  getConfiguration: () => ({
    get: (_key: string, fallback: unknown) => fallback,
    update: async () => undefined,
  }),
  onDidChangeTextDocument: () => ({ dispose() {} }),
  onDidCloseTextDocument: () => ({ dispose() {} }),
  onDidChangeConfiguration: () => ({ dispose() {} }),
  workspaceFolders: [],
  findFiles: async () => [],
  openTextDocument: async () => ({}),
};

export const languages = {
  registerHoverProvider: () => ({ dispose() {} }),
  registerCodeLensProvider: () => ({ dispose() {} }),
};

export const commands = {
  registerCommand: () => ({ dispose() {} }),
  executeCommand: async () => undefined,
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

export const ProgressLocation = { Notification: 15 };
