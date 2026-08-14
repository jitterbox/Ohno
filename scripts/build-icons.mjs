#!/usr/bin/env node
/**
 * Generates lightly stylized flat-3D icons from Material Symbols-inspired
 * path data (Apache 2.0). Emits dark/ and light/ variants.
 */
import * as fs from 'node:fs';
import * as path from 'node:path';

const root = path.resolve(import.meta.dirname, '..');
const targets = [
  path.join(root, 'media', 'icons'),
  path.join(root, 'src', 'extension', 'media', 'icons'),
];

const icons = {
  'confidence-high': {
    // check_circle
    path: 'M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm-1.2 14.2-4.2-4.2 1.4-1.4 2.8 2.8 6-6 1.4 1.4-7.4 7.4z',
  },
  'confidence-medium': {
    // help
    path: 'M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm.8 15.2h-1.6v-1.6h1.6v1.6zm1.5-6.1-.8.8c-.7.7-1.1 1.3-1.1 2.7h-1.6v-.4c0-1 .4-1.9 1.1-2.6l1.1-1.1c.4-.4.6-.9.6-1.5 0-1.1-.9-2-2-2s-2 .9-2 2H8.9c0-2 1.6-3.6 3.6-3.6s3.6 1.6 3.6 3.6c0 .8-.3 1.5-.8 2.1z',
  },
  'confidence-low': {
    // error / warning diamond-ish
    path: 'M1 21h22L12 2 1 21zm12-3h-2v-2h2v2zm0-4h-2v-4h2v4z',
  },
  'confidence-unknown': {
    // question_mark
    path: 'M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm1 15h-2v-2h2v2zm1.1-6.5-.9.9C12.5 12 12 12.5 12 14h-2v-.5c0-1.1.4-2.1 1.2-2.8l1.2-1.2c.4-.4.6-.9.6-1.5 0-1.1-.9-2-2-2s-2 .9-2 2H7.5C7.5 6.6 9.6 4.5 12 4.5S16.5 6.6 16.5 9c0 .9-.4 1.7-1.4 2.5z',
  },
  time: {
    // schedule
    path: 'M11.99 2C6.47 2 2 6.48 2 12s4.47 10 9.99 10C17.52 22 22 17.52 22 12S17.52 2 11.99 2zM12 20c-4.42 0-8-3.58-8-8s3.58-8 8-8 8 3.58 8 8-3.58 8-8 8zm.5-13H11v6l5.25 3.15.75-1.23-4.5-2.67z',
  },
  space: {
    // memory
    path: 'M15 9H9v6h6V9zm-2 4h-2v-2h2v2zm8-2V9h-2V7c0-1.1-.9-2-2-2h-2V3h-2v2h-2V3H9v2H7c-1.1 0-2 .9-2 2v2H3v2h2v2H3v2h2v2c0 1.1.9 2 2 2h2v2h2v-2h2v2h2v-2h2c1.1 0 2-.9 2-2v-2h2v-2h-2v-2h2zm-4 6H7V7h10v10z',
  },
  functions: {
    // functions
    path: 'M18 4H6v2l6.5 6L6 18v2h12v-3h-7.8l5.3-5.3L10.2 7H18z',
  },
  derivation: {
    // account_tree
    path: 'M22 11V3h-7v3H9V3H2v8h7V8h2v10h4v3h7v-8h-7v3h-2V8h2v3z',
  },
  bolt: {
    // bolt
    path: 'M11 21h-1l1-7H7.5c-.58 0-.57-.32-.38-.66.19-.34.05-.08.07-.12C8.48 10.94 10.42 7.54 13.89 3c.24-.31.55-.55.94-.55.58 0 .95.47.83 1.05L14 11h3.56c.61 0 .62.33.42.68-.2.35-.06.08-.08.13C15.9 14.86 13.31 19.04 11 21z',
  },
};

const palettes = {
  light: { fill: '#444444', extrude: '#bbbbbb' },
  dark: { fill: '#d0d0d0', extrude: '#555555' },
};

function svg(d, palette) {
  return `<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" width="20" height="20">
  <path d="${d}" fill="${palette.extrude}" transform="translate(1 1)"/>
  <path d="${d}" fill="${palette.fill}"/>
</svg>
`;
}

for (const target of targets) {
  for (const theme of ['light', 'dark']) {
    const dir = path.join(target, theme);
    fs.mkdirSync(dir, { recursive: true });
    for (const [name, spec] of Object.entries(icons)) {
      fs.writeFileSync(path.join(dir, `${name}.svg`), svg(spec.path, palettes[theme]));
    }
  }
}

console.log(`Wrote ${Object.keys(icons).length} icons × 2 themes to ${targets.length} locations`);
