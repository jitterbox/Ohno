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
  if (fn.explanation) {
    md.appendMarkdown(`*${fn.explanation}*\n\n`);
  }
  if (fn.approaches?.length) {
    md.appendMarkdown(`**Approaches**\n\n`);
    for (const a of fn.approaches) {
      const hint = a.timeHint ? ` — \`${a.timeHint}\`` : '';
      md.appendMarkdown(
        `- **${a.name}** (${a.role})${hint}: ${a.summary}\n`,
      );
    }
    md.appendMarkdown('\n');
    if (fn.selectionHint) {
      md.appendMarkdown(`*${fn.selectionHint}*\n\n`);
    }
  }
  if (fn.patterns?.length) {
    md.appendMarkdown(`**Patterns**\n\n`);
    for (const p of fn.patterns) {
      md.appendMarkdown(`- ${p.label}: ${p.reason}\n`);
    }
    md.appendMarkdown('\n');
  }
  md.appendMarkdown(`Confidence: **${fn.confidence}** (${fn.tier} tier)\n\n`);
  if (fn.confidenceReasons?.length) {
    md.appendMarkdown(`**Why this is not high**\n\n`);
    for (const reason of fn.confidenceReasons) {
      md.appendMarkdown(`- *${reason}*\n`);
    }
    md.appendMarkdown('\n');
  }

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
