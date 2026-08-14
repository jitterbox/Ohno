/**
 * Adapted from GitLens (MIT), src/annotations/annotations.ts
 * Copyright (c) 2016-2021 Eric Amodio
 * Copyright (c) 2021-2026 Axosoft, LLC dba GitKraken
 */
import * as path from 'node:path';
import * as vscode from 'vscode';
import type { OhnoConfig } from '../config';
import type { Confidence, FunctionComplexity } from '../analysis/types';
import { toCssInjection } from './cssInjection';

export interface DecorationSet {
  headline: vscode.TextEditorDecorationType;
  nested: vscode.TextEditorDecorationType;
  gutters: Record<Confidence, vscode.TextEditorDecorationType>;
}

export function createDecorationSet(
  extensionPath: string,
): DecorationSet {
  const headline = vscode.window.createTextEditorDecorationType({
    after: {
      margin: '0 0 0 3em',
      textDecoration: 'none',
    },
  });
  const nested = vscode.window.createTextEditorDecorationType({
    after: {
      margin: '0 0 0 2em',
      textDecoration: 'none',
    },
  });
  const gutters = {
    high: gutter(extensionPath, 'confidence-high'),
    medium: gutter(extensionPath, 'confidence-medium'),
    low: gutter(extensionPath, 'confidence-low'),
    unknown: gutter(extensionPath, 'confidence-unknown'),
  };
  return { headline, nested, gutters };
}

function gutter(
  extensionPath: string,
  name: string,
): vscode.TextEditorDecorationType {
  const light = vscode.Uri.file(
    path.join(extensionPath, 'media', 'icons', 'light', `${name}.svg`),
  );
  const dark = vscode.Uri.file(
    path.join(extensionPath, 'media', 'icons', 'dark', `${name}.svg`),
  );
  return vscode.window.createTextEditorDecorationType({
    light: { gutterIconPath: light, gutterIconSize: 'contain' },
    dark: { gutterIconPath: dark, gutterIconSize: 'contain' },
  });
}

export function disposeDecorationSet(set: DecorationSet): void {
  set.headline.dispose();
  set.nested.dispose();
  for (const g of Object.values(set.gutters)) g.dispose();
}

export function headlineText(
  fn: FunctionComplexity,
  config: OhnoConfig,
): string {
  const parts = [fn.time];
  if (config.showSpace) parts.push(fn.space);
  if (config.showConfidence) parts.push(fn.confidence);
  return ` ${parts.join(' · ')} `;
}

export function headlineRender(
  fn: FunctionComplexity,
): vscode.ThemableDecorationAttachmentRenderOptions {
  return {
    contentText: '',
    color: new vscode.ThemeColor(confidenceColor(fn.confidence)),
    backgroundColor: new vscode.ThemeColor('ohno.inlineBackground'),
    textDecoration: toCssInjection({
      'white-space': 'pre',
      'font-variant-numeric': 'tabular-nums',
    }),
  };
}

export function confidenceColor(confidence: Confidence): string {
  switch (confidence) {
    case 'high':
      return 'ohno.confidenceHigh';
    case 'medium':
      return 'ohno.confidenceMedium';
    case 'low':
      return 'ohno.confidenceLow';
    default:
      return 'ohno.confidenceUnknown';
  }
}

export function lineRange(
  document: vscode.TextDocument,
  line: number,
): vscode.Range {
  const max = 2 ** 30 - 1;
  return document.validateRange(new vscode.Range(line, max, line, max));
}
