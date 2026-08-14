import { describe, expect, it } from 'vitest';
import { italicTooltip, toTreeItem } from '../../src/ui/complexityTree';
import type { ComplexityItem } from '../../src/ui/complexityModel';

const leaf = (partial: Partial<ComplexityItem>): ComplexityItem => ({
  id: 'explanation',
  kind: 'summary',
  label: 'plain',
  icon: 'comment',
  children: [],
  highlighted: false,
  ...partial,
});

describe('italicTooltip', () => {
  it('escapes markdown so labels cannot break the tree', () => {
    const md = italicTooltip('use *n* and `k` (heap)');
    expect(md.value).toBe('*use \\*n\\* and \\`k\\` \\(heap\\)*');
  });
});

describe('toTreeItem', () => {
  it('marks italic rows with escaped markdown tooltips', () => {
    const item = toTreeItem(leaf({
      label: 'O(n*)',
      tooltip: 'assumes *n* items',
      italic: true,
    }));
    expect(item.tooltip).toMatchObject({
      value: '*assumes \\*n\\* items*',
    });
  });
});
