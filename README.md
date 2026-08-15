# Oʰ(Nᵒ) — Algorithmic Complexity

<p align="center">
  <img src="assets/icon.png" alt="Oʰ(Nᵒ)" width="128" height="128">
</p>

Ohno is a Visual Studio Code extension that estimates **Big-O time** and
**auxiliary-space** complexity for functions in the focused editor.

It is an *algorithmic* analyzer, not a maintainability metric. A one-branch
method that sorts is still O(n log n). A fifty-branch method that does
constant work is still O(1). That is the opposite of cyclomatic complexity
(Visual Studio / [CA1502](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1502)),
which counts independent paths and says nothing about input size.

C# is analyzed by a bundled [Roslyn](https://learn.microsoft.com/dotnet/csharp/roslyn-sdk/)
server. TypeScript is not a selectable language in this release.

## What you see

Inline, at the end of a function signature:

```
Time O(n log k) · Space O(k) · medium
```

The **Complexity** activity-bar view shows:

- a plain-language gloss (*Linearithmic time*, *Quadratic space*, or
  *Unknown: The complexity cannot be easily determined because …*)
- **Approaches** — up to three readings of the same function
  (dominant, nested, sequential, or alternative)
- **Recognized patterns** (null-terminated walk, deferred LINQ vs
  EF/`IQueryable`, regex, bounded recursion, …)
- **Confidence** and, when it is below high, *why* (the assumption that
  would fail if the source used a different loop, type, or store)
- input dimensions (`n = values.Length`, `k = parameter k`)
- a derivation tree that rolls up like spreadsheet subtotals
- bounding suggestions (for example, cap a priority queue at `k`)

Select a statement or loop inside a method to re-analyze **that
span only**. The panel title becomes `Name (selection)`. If more
than one approach remains, a hint asks you to narrow the selection.
Clear the selection to return to the whole function. Inline
annotations stay per-function; they do not follow the selection.

Automatic analysis is the **fast** tier: it uses a loaded `.sln`,
or the `.csproj` found by walking up from the file, when that
workspace is ready. Otherwise it uses an ad-hoc compilation of the
buffer. Deep analysis (`Ohno: Run Deep Analysis`) waits for the
project graph and records a warning if it has to fall back.

## What Ohno is not

- Not a profiler. It does not run your code or measure wall-clock time.
- Not CA1502 / maintainability index / class coupling.
- Not a proof. Idiom matchers can miss an equivalent algorithm written
  with a different loop, a helper, or a custom collection.
- Not a claim about I/O, locks, or thread scheduling unless a pattern
  explicitly says those are unknown. An incidental `await` or
  `IQueryable` next to a resolved loop is named and does **not**
  wipe the local bound; `await foreach`, `dynamic`, regex, and
  similar hard opacity still report `O(unknown)`.

When a conclusive bound cannot be justified from the source, Ohno reports
**O(unknown)** and a reason — it does not invent O(1).

## Supported languages

| Language | Default | Engine |
|---|---|---|
| C# | On | Roslyn `IOperation` + BCL/LINQ catalog |

TypeScript is not selectable.

## Commands

| Command | What it does |
|---|---|
| **Ohno: Run Deep Analysis** | Wait for the project graph and re-analyze |
| **Ohno: Show Complexity Derivation** | Focus the Complexity view |
| **Ohno: Focus Complexity Panel** | Open the activity-bar view |
| **Ohno: Toggle Complexity Annotations** | Hide or show end-of-line decorations |
| **Ohno: Copy Complexity Summary** | Copy `time · space` for the selection, if any, otherwise the function at the caret |

## Settings

| Setting | Default | Purpose |
|---|---|---|
| `ohno.enabled` | `true` | Master switch |
| `ohno.languages.csharp` | `true` | Analyze C# |
| `ohno.analysis.tier` | `fast` | Reserved; deep analysis is on demand (`Ohno: Run Deep Analysis`) |
| `ohno.annotations.showInline` | `true` | End-of-line annotations |
| `ohno.annotations.mode` | `inline` | `inline`, `codelens`, or `off` |
| `ohno.annotations.nestingDepth` | `2` | Nested subtotal depth |
| `ohno.annotations.showSpace` | `true` | Include auxiliary space |
| `ohno.annotations.showConfidence` | `true` | Include confidence in the annotation |
| `ohno.performance.debounceMs` | `250` | Re-analyze delay after edits |
| `ohno.performance.maxFileSizeKb` | `500` | Skip huge files (`0` = no limit) |
| `ohno.csharp.analyzerPath` | `""` | Override the bundled Roslyn server |
| `ohno.server.logLevel` | `warn` | Server log level |

## How to read a result

**Time** is a worst-case symbolic bound in the input dimensions Ohno
inferred. Independent sizes stay independent: O(n + m), not “O(n)” by
guessing m ≤ n.

**Space** is *peak simultaneously retained* auxiliary memory, not the
sum of every allocation. Allocating `int[n]` each iteration and dropping
it is Θ(n) space and Θ(n²) time. Storing those arrays in a list is Θ(n²)
space.

**Confidence**

| Level | Meaning |
|---|---|
| High | Structurally resolved (exact catalog, `Length`/`Count` loops, `new T[n]`) |
| Medium | Dominant bound is clear; an idiom or amortized/expected library cost was assumed |
| Low | One or more calls remain as `C(name)` |
| Unknown | No honest polynomial; see the reason |

Below high, the panel lists the specific assumptions (for example,
“Collection size is assumed bounded by a Count > k + Dequeue check”).

**Unknown** uses a fixed sentence:

*Unknown: The complexity cannot be easily determined because [reason].*

**Approaches** are competing or composed readings, not extra proofs:

| Role | Meaning |
|---|---|
| Dominant | The headline bound |
| Nested | Incidental work inside that bound (for example `await` beside a loop) |
| Sequential | Distinct steps in the same span (two loops, then a materialize) |
| Alternative | Another honest reading (cache hit vs miss, bounded vs unbounded recursion, enumerate a deferred LINQ query) |

Deferred `System.Linq.Enumerable` is in-memory query construction
(O(1) to build). EF / `IQueryable` is a different approach: the
provider runs the tree; Ohno does not invent a SQL bound.

## Architecture

```
VS Code / Cursor extension (TypeScript)
  ├─ Analyzer registry (per language)
  └─ C# adapter ── JSON-RPC stdio ── ComplexityAnalyzer.Server (Roslyn)

ComplexityAnalyzer.Server
  ├─ Fast: project SemanticModel when ready, else ad-hoc compilation
  └─ Deep: same walker; waits for MSBuildWorkspace / records fallback

ComplexityAnalyzer.Core     symbolic expressions + Big-O simplification
ComplexityAnalyzer.CSharp   IOperation walk, patterns, recurrences
ComplexityAnalyzer.DotNet   BCL / LINQ cost catalog
```

The wire contract is `src/shared/protocol.ts` and must stay in sync with
`src/analyzer/ComplexityAnalyzer.Server/Protocol/Contracts.cs` and
`src/shared/protocol.schema.json`. `AnalyzeRequest.selection` is the
optional span for selection-scoped analysis.

Roslyn entry points:

- [`IOperation`](https://learn.microsoft.com/dotnet/api/microsoft.codeanalysis.ioperation) — semantic graph
- [`SemanticModel.GetOperation`](https://learn.microsoft.com/dotnet/api/microsoft.codeanalysis.semanticmodel.getoperation)
- [`MSBuildWorkspace`](https://learn.microsoft.com/dotnet/api/microsoft.codeanalysis.msbuild.msbuildworkspace)
- [Work with a workspace](https://learn.microsoft.com/dotnet/csharp/roslyn-sdk/work-with-workspace)

## Install from source

```bash
# Linux
dotnet publish src/analyzer/ComplexityAnalyzer.Server \
  -c Release -r linux-x64 --self-contained \
  -o src/extension/server
cd src/extension && npm install && npx @vscode/vsce package --target linux-x64
code --install-extension ohno-linux-x64-0.1.1.vsix --force

# Windows (from PowerShell or cmd)
dotnet publish src/analyzer/ComplexityAnalyzer.Server `
  -c Release -r win-x64 --self-contained `
  -o src/extension/server
cd src/extension && npm install && npx @vscode/vsce package --target win-x64
code --install-extension ohno-win-x64-0.1.1.vsix --force
```

Use `osx-arm64` the same way. Reload the window after install. The
bundled server is `ComplexityAnalyzer.Server` on Unix and
`ComplexityAnalyzer.Server.exe` on Windows.

## Development

```bash
# Analyzer
dotnet test src/analyzer/ComplexityAnalyzer.Tests

# Extension
cd src/extension
npm install
npm run compile
npm test
```

Launch **Run Extension** from the repo (`src/extension/.vscode/launch.json`)
after a `dotnet build` of `ComplexityAnalyzer.Server`.

Fixtures used by the test suite:

| Fixture | Role |
|---|---|
| `samples/leetcode/OptimalSolutions.cs` | 21 known-optimal algorithms |
| `samples/roslyn/RoslynComplexityEdgeCases.cs` | Adversarial / inconclusive hazards |
| `samples/roslyn/RoslynSpaceComplexityPatterns.cs` | Peak-space idioms |
| `samples/roslyn/RoslynSpaceComplexityCombinations.cs` | Combined time + space |

See [docs/DEVELOPER.md](docs/DEVELOPER.md) for the theoretical model,
how Ohno differs from Microsoft code metrics, and how to extend the
catalog and pattern detectors.

## Known issues / unsupported

These are intentional limits, not missing tickets:

| Case | What happens |
|---|---|
| `#:package` / `#:sdk` / `dotnet run app.cs` restore | Directives are detected and warned. Packages are **not** restored on the analysis path. |
| Source generators on a loose file | Ad-hoc compilation does not run generators. A loaded project compilation may see them. |
| Every `#if` configuration | One compilation: the project's defines, or none on ad-hoc. Other `#if` bodies are invisible. |
| `.razor` / `.cshtml` / `.csx` | Not a C# document. Ohno does not run. |
| `IQueryable`, `dynamic`, expression trees | Reported as unknown / opaque. No invented tight bound. |

Loose or untitled `.cs` files always use the ad-hoc compilation
(SDK implicit usings only). Unresolved types produce a warning
instead of a silent O(1).

## Status

v0.1.1. C# is the only selectable language.
Estimates are for local computational work as written; they are not a
substitute for measurement on production data.

## Attribution

- **GitLens** (MIT) — inline decoration techniques adapted from
  `src/annotations/` (non-`plus/` code only). See [NOTICE](NOTICE).
- **Material Symbols** (Apache 2.0) — icon path data. See [NOTICE](NOTICE).
- **.NET compiler platform (Roslyn)** — [Microsoft documentation](https://learn.microsoft.com/dotnet/csharp/roslyn-sdk/).

## License

MIT. See [LICENSE](LICENSE).
