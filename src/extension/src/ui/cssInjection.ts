/**
 * Adapted from GitLens (MIT), src/annotations/annotations.ts
 * Copyright (c) 2016-2021 Eric Amodio
 * Copyright (c) 2021-2026 Axosoft, LLC dba GitKraken
 *
 * Builds a CSS-injection string for VS Code's `textDecoration` property.
 * VS Code emits `text-decoration: ` in the generated CSS rule, so the
 * first token is consumed as the text-decoration value.
 */
export function toCssInjection(
  styles: Record<string, string | number | undefined | null>,
): string {
  const td = styles['text-decoration'] ?? 'none';
  return `text-decoration:${td};${Object.entries(styles)
    .filter(([key, value]) => key !== 'text-decoration' && value != null && value !== '')
    .map(([key, value]) => `${key}:${value}`)
    .join(';')};`;
}
