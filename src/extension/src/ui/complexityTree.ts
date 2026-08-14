import * as vscode from 'vscode';
import type { ComplexityItem } from './complexityModel';

export class ItemTreeProvider
  implements vscode.TreeDataProvider<ComplexityItem>, vscode.Disposable
{
  private readonly emitter = new vscode.EventEmitter<
    ComplexityItem | undefined
  >();
  readonly onDidChangeTreeData = this.emitter.event;
  private roots: ComplexityItem[] = [];
  private readonly byId = new Map<string, ComplexityItem>();

  setRoots(roots: ComplexityItem[]): void {
    this.roots = roots;
    this.byId.clear();
    indexItems(roots, this.byId);
    this.emitter.fire(undefined);
  }

  item(id: string): ComplexityItem | undefined {
    return this.byId.get(id);
  }

  getTreeItem(element: ComplexityItem): vscode.TreeItem {
    return toTreeItem(element);
  }

  getChildren(element?: ComplexityItem): ComplexityItem[] {
    return element ? element.children : this.roots;
  }

  getParent(element: ComplexityItem): ComplexityItem | undefined {
    return findParent(this.roots, element.id);
  }

  dispose(): void {
    this.emitter.dispose();
  }
}

export function toTreeItem(item: ComplexityItem): vscode.TreeItem {
  const state = item.children.length
    ? vscode.TreeItemCollapsibleState.Expanded
    : vscode.TreeItemCollapsibleState.None;
  const label = item.highlighted
    ? { label: item.label, highlights: [[0, item.label.length] as [number, number]] }
    : item.label;
  const tree = new vscode.TreeItem(label, state);
  tree.id = item.id;
  tree.description = item.description;
  tree.tooltip = item.italic
    ? italicTooltip(item.tooltip ?? item.label)
    : item.tooltip ?? item.label;
  tree.iconPath = new vscode.ThemeIcon(item.icon);
  tree.contextValue = item.kind;
  if (item.range && item.uri) {
    tree.command = {
      command: 'ohno.revealEvidence',
      title: 'Reveal in editor',
      arguments: [item.uri, item.range, item.id],
    };
  }
  return tree;
}

export function italicTooltip(text: string): vscode.MarkdownString {
  return new vscode.MarkdownString(`*${escapeMd(text)}*`);
}

function escapeMd(value: string): string {
  return value.replace(/[\\*_`[\]()]/g, '\\$&');
}

function indexItems(
  items: ComplexityItem[],
  byId: Map<string, ComplexityItem>,
): void {
  for (const item of items) {
    byId.set(item.id, item);
    indexItems(item.children, byId);
  }
}

function findParent(
  items: ComplexityItem[],
  id: string,
): ComplexityItem | undefined {
  for (const item of items) {
    if (item.children.some((child) => child.id === id)) return item;
    const nested = findParent(item.children, id);
    if (nested) return nested;
  }
  return undefined;
}
