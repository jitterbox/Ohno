import * as vscode from 'vscode';
import type { FunctionComplexity } from '../analysis/types';
import type { ResultStore } from './resultStore';

export class ComplexityHoverProvider implements vscode.HoverProvider {
  constructor(private readonly store: ResultStore) {}

  provideHover(
    document: vscode.TextDocument,
    position: vscode.Position,
  ): vscode.Hover | undefined {
    const fn = this.store.functionAt(document.uri, position);
    if (!fn) return undefined;
    return new vscode.Hover(buildMarkdown(fn, document.uri));
  }
}

export function buildMarkdown(
  fn: FunctionComplexity,
  uri: vscode.Uri,
): vscode.MarkdownString {
  const md = new vscode.MarkdownString(undefined, true);
  md.isTrusted = true;
  md.appendMarkdown(`**${fn.name}** — ${fn.time} · ${fn.space}\n\n`);
  md.appendMarkdown(`Confidence: **${fn.confidence}** (${fn.tier} tier)\n\n`);

  if (fn.dimensions.length) {
    md.appendMarkdown(`**Dimensions**\n\n`);
    for (const d of fn.dimensions) {
      md.appendMarkdown(`- \`${d.variable}\` = ${d.meaning}\n`);
    }
    md.appendMarkdown('\n');
  }

  md.appendMarkdown(`**Derivation**\n\n`);
  appendEvidence(md, fn.evidence, 0);

  if (fn.warnings.length) {
    md.appendMarkdown(`\n**Why this is an estimate**\n\n`);
    for (const w of fn.warnings) {
      md.appendMarkdown(`- ${w.message}\n`);
    }
  }

  if (fn.boundingSuggestions.length) {
    md.appendMarkdown(`\n**Bounding opportunities**\n\n`);
    for (const s of fn.boundingSuggestions) {
      md.appendMarkdown(
        `- ${s.description} Condition: \`${s.condition}\` → ${s.resultingTime} / ${s.resultingSpace}\n`,
      );
    }
  }

  const args = encodeURIComponent(JSON.stringify([uri.toString(), fn.id]));
  md.appendMarkdown(
    `\n[Run deep analysis](command:ohno.runDeepAnalysis?${args}) · ` +
    `[Show full derivation](command:ohno.showDerivation?${args}) · ` +
    `[Why is this an estimate?](command:ohno.showDerivation?${args})\n`,
  );
  return md;
}

function appendEvidence(
  md: vscode.MarkdownString,
  node: FunctionComplexity['evidence'],
  depth: number,
): void {
  const indent = '  '.repeat(depth);
  md.appendMarkdown(`${indent}- ${node.label}: \`${node.cost}\`\n`);
  for (const child of node.children) {
    appendEvidence(md, child, depth + 1);
  }
}
