import * as vscode from 'vscode';
import type { FunctionComplexity } from '../analysis/types';

export function showDerivation(fn: FunctionComplexity): void {
  const panel = vscode.window.createWebviewPanel(
    'ohno.derivation',
    `Ohno: ${fn.name}`,
    vscode.ViewColumn.Beside,
    { enableFindWidget: true },
  );
  panel.webview.html = render(fn);
}

function render(fn: FunctionComplexity): string {
  const dims = fn.dimensions
    .map((d) => `<li><code>${esc(d.variable)}</code> = ${esc(d.meaning)}</li>`)
    .join('');
  const warnings = fn.warnings
    .map((w) => `<li>${esc(w.message)}</li>`)
    .join('');
  const bounds = fn.boundingSuggestions
    .map((s) =>
      `<li>${esc(s.description)} <code>${esc(s.condition)}</code> → ${esc(s.resultingTime)}</li>`)
    .join('');
  return `<!DOCTYPE html>
<html>
<head>
<style>
  body { font-family: var(--vscode-font-family); padding: 1rem; }
  code { font-family: var(--vscode-editor-font-family); }
  ul { padding-left: 1.2rem; }
</style>
</head>
<body>
  <h1>${esc(fn.name)}</h1>
  <p>${esc(fn.time)} · ${esc(fn.space)} · ${esc(fn.confidence)} (${esc(fn.tier)})</p>
  <h2>Dimensions</h2>
  <ul>${dims || '<li>none</li>'}</ul>
  <h2>Derivation</h2>
  ${renderNode(fn.evidence)}
  <h2>Why this is an estimate</h2>
  <ul>${warnings || '<li>No unresolved operations.</li>'}</ul>
  <h2>Bounding opportunities</h2>
  <ul>${bounds || '<li>None deduced.</li>'}</ul>
</body>
</html>`;
}

function renderNode(node: FunctionComplexity['evidence']): string {
  const kids = node.children.map(renderNode).join('');
  return `<ul><li>${esc(node.label)}: <code>${esc(node.cost)}</code>${kids}</li></ul>`;
}

function esc(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;');
}
