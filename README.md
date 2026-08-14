# Ohno — Algorithmic Complexity

Ohno is a VS Code extension that estimates **Big-O time and auxiliary-space
complexity** for functions in the focused editor. Results appear as inline
end-of-line annotations (GitLens-style), with confidence, a derivation tree
on hover, and optional on-demand deep analysis.

C# is analyzed by a bundled Roslyn server. TypeScript is analyzed in-process
via the TypeScript compiler API.

## What you see

```
Time O(n log k) · Space O(k) · high
```

Hover a function for:

- input dimensions (`n = values.Length`, `k = parameter k`)
- a nested derivation that rolls up like spreadsheet subtotals
- warnings when the result is only an estimate
- bounding suggestions (e.g. cap a priority queue at `k`)
- **Run deep analysis** / **Show full derivation**

## Settings

| Setting | Default | Purpose |
|---|---|---|
| `ohno.enabled` | `true` | Master switch |
| `ohno.analysis.tier` | `fast` | `fast` (automatic) or `deep` (solution-wide) |
| `ohno.annotations.mode` | `inline` | `inline`, `codelens`, or `off` |
| `ohno.annotations.nestingDepth` | `2` | Nested subtotal depth |
| `ohno.csharp.analyzerPath` | `""` | Override bundled Roslyn server |

Deep analysis is **not** automatic unless you set the tier to `deep`. Use the
hover link or `Ohno: Run Deep Analysis`.

## Architecture

```
VS Code extension (TypeScript)
  ├─ Analyzer registry (per-language)
  ├─ C# adapter ── JSON-RPC stdio ── ComplexityAnalyzer.Server (Roslyn)
  └─ TypeScript analyzer (compiler API)
```

The symbolic engine (`ComplexityAnalyzer.Core`) is language-neutral. C#
front-end code lives in `ComplexityAnalyzer.CSharp` + a BCL/LINQ catalog.

## Development

```bash
# Analyzer
cd src/analyzer
dotnet test

# Icons + fixtures
node scripts/build-icons.mjs
node scripts/generate-fixtures.mjs

# Extension
cd src/extension
npm install
npm run compile
npm test                 # Vitest unit tests
OHNO_E2E=1 npm run test:e2e   # Playwright + real VS Code via CDP
```

Launch the extension with **Run Extension** from `src/extension` (F5) after
`dotnet build` on `ComplexityAnalyzer.Server`.

### Packaging

```bash
dotnet publish src/analyzer/ComplexityAnalyzer.Server \
  -c Release -r linux-x64 --self-contained \
  -o src/extension/server
cd src/extension && npx vsce package --target linux-x64
```

Repeat with `win-x64` / `osx-arm64` for other platforms.

## Attribution

- **GitLens** (MIT) — inline decoration techniques adapted from
  `src/annotations/` (non-`plus/` code only). See [NOTICE](NOTICE).
- **Material Symbols** (Apache 2.0) — icon path data. See [NOTICE](NOTICE).

## License

MIT. See [LICENSE](LICENSE).
