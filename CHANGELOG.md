# Changelog

All notable changes to Ohno are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions
follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Entries for 0.1.0 through 0.1.2 were reconstructed from the commit
history after the fact — this file did not exist while those releases
were made, so they summarize what shipped rather than what was written
down at the time.

## [Unreleased]

TypeScript and JavaScript analysis is on by default. C# bounds are
unchanged. Typed TS follows the same honesty rule as C#; untyped JS
stays `C(name)` / Unknown.

### Added

- `ohno.languages.typescript` / `javascript` / `typescriptreact` /
  `javascriptreact` (default on). A Node worker uses
  `ts.createProgram` and the same `AnalyzeResponse` as C#.
- Shared contracts in `src/shared/`: protocol schema assertions,
  BCL catalog snapshot, and algebra golden vectors.
- Samples under `samples/typescript` and `samples/javascript`,
  including cardinality, space, closures, `this`, ranking, and a
  two-file `tsconfig` interop fixture.
- TypeScript patterns: string concat, trivial vs backtracking
  regex, visited/unbounded worklists, sliding-window heap cap,
  linear and branching recurrence, cache-history, yield, approaches.
- Cheap ranking for counted `while` / `for` loops (`i < n` +
  increment, `while (true)` + break, `Math.floor(n / 2)`, `n >>= 1`,
  `i *= 2`). Collatz stays unknown.

### Changed

- The unreachable string-algebra TypeScript stub is gone. Unknown
  receivers are `C(name)` at Low; `for await` is Unknown; cataloged
  `Array.sort` / `toSorted` is O(n log n).
- TypeScript **fast** analysis is ad-hoc (buffer + default lib).
  **Deep** (`Ohno: Run Deep Analysis`) builds a `tsconfig` /
  `jsconfig` `Program` and can inline same-program helpers.
- Marketplace README, welcome views, and keywords cover TypeScript
  and JavaScript. The VSIX ships `ohno-ts-worker.js`, the
  `typescript` package (for `lib.*.d.ts`), and this changelog.

### Fixed

- A second call to the same helper is no longer treated as
  recursion (that under-counted `foo(); foo();` as O(1)).
- Cancellation and a stale buffer version no longer keep mutating
  the `Program` cache. Worker `error` clears the worker handle.
- Overlay paths use `realpath`, so a symlink / URI matches the
  `tsconfig` file and the unsaved buffer.
- `rangeOf` uses the callee `SourceFile`. Cards, heaps, and
  worklists key on checker symbols, not identifier text.
- Recurrence overwrites time only and keeps walked space / allocs.
- Nameless `export default function` and object-literal methods
  are collected. Pattern hits keep a second range instead of
  collapsing by id.
- Log-shaped `for` increments use the condition's real bound, not
  an invented `n`. Nested pointer advance is an AST check, not a
  `left|right|i|j` regex.

## [0.1.6] — 2026-08-15

Settings cleanup and a larger adversarial fixture. Analyzer bounds
are unchanged from 0.1.5.

### Changed

- `ohno.annotations.mode` is the only editor-display switch
  (`inline`, `codelens`, `off`). `ohno.annotations.showInline` is
  deprecated; a leftover `false` with the default `inline` mode is
  treated as `off`.

### Added

- `samples/roslyn/SessionLedger.cs` now has 55 adversarial members
  with comments that match what 0.1.5/0.1.6 actually report,
  including documented undercounts (`SumRange`, `Drain`, `GotoSum`).

### Documentation

- README and developer docs state that the BCL catalog is not
  versioned by TFM: the same source gets the same bound; historical
  class changes (`List.Sort` worst-case, hash flooding) are reported
  as the modern cost.

## [0.1.5] — 2026-08-15

Dictionaries and read-only indexers now resolve to real costs instead
of dangling as `C(name)` at Low confidence.

### Fixed

- `ConcurrentDictionary` reads are O(1) expected; its `Count` is O(n)
  amortized. `ImmutableDictionary` is an AVL tree, so its indexer and
  `TryGetValue` are O(log n). Both were uncataloged.
- A compound assignment (`s += d[k]`) was mistaken for a write target,
  so the indexer never reached the catalog. Only a plain write
  (`d[k] = v`) skips the read cost now.
- A `static readonly` collection field kept its size after the
  constant-scalar fix collapsed it to 1, so `LogReceiver` bound to
  `Log(1)`. Only non-collection scalar constants collapse;
  `Enumerable.Repeat(int.MaxValue, n)` still sizes by `n` while a
  collection field keeps its dimension.
- The read-only list/dictionary interface indexers
  (`IReadOnlyList`, `IList`, `IReadOnlyDictionary`, `IDictionary`)
  the compound-assignment fix exposed are now cataloged.

### Added

- `samples/roslyn/SessionLedger.cs` adversarial fixture and a
  `DictionaryIndexers_ResolveWithoutOpaqueCall` regression test.

## [0.1.4] — 2026-08-15

Closes the remaining places 0.1.3 still invented a tight bound, and
tightens the selection and release races found in the same pass.

### Fixed

- A user getter named `Count`, `Length`, or `Chars` is walked instead
  of treated as free. The name check ran before the body walk, so a
  `Count` that scanned an array reported O(1) High.
- `for (j = i - 1; j >= 0; j--)` is O(n²). A literal *floor* is not a
  fixed iteration count; only a literal *ceiling* (`j < 8`) is. The
  while-shaped insertion sort already worked; the for spelling did not.
- `get_Item` is no longer allowlisted for every BCL type. List stays
  O(1); `SortedList` / `SortedDictionary` are O(log n);
  `ImmutableList` is O(n). Catalog costs now bind on property reads
  instead of treating a catalog hit as free.
- `string.Concat(a, b, c)` is |a| + |b| + |c|. Concat was treated as
  two-source by name, so the third operand disappeared.
- `new Regex(..., NonBacktracking).IsMatch(t)` is linear. Only
  assigned constructions and static overloads were recorded before.
- Clearing the editor selection cancels in-flight work and bumps the
  ticket, so a late response cannot restore the panel.
- A disposed analyzer client will not spawn a new server if
  `setSolution` lands after deactivate.
- Workspace bind uses the active file and prefers `.slnx` over `.sln`
  when both exist; one solution can replace another.
- Manual release checks out `inputs.target` (or the tag SHA), so the
  packaged VSIX matches the tagged commit.
- Selection analysis honors `ohno.performance.maxFileSizeKb`, matching
  the document path.

### Changed

- Expected and amortized catalog constructors report Medium
  confidence, the same as cataloged calls.

## [0.1.3] — 2026-08-15

The theme is honesty about what is and is not known: constant time now
has to be established rather than assumed, and the analyzer stops
guessing in the places it used to.

### Changed

- **O(1) is never a fallback.** An unresolved executable operation is
  reported as `C(name)` at Low confidence, naming the member with no
  cost summary. Constant time requires a catalog entry or an entry in
  the new `ConstantTimePrimitives` allowlist, which is keyed by
  containing type rather than member name — `int.GetHashCode()` is
  Θ(1) and `string.GetHashCode()` is Θ(length). The rule covers calls,
  constructors, metadata property reads, and bodyless method symbols.
  Previously any `System.*` member whose containing type was not itself
  a collection was costed as free, so `OrderBy(keySelector, comparer)`,
  `Sum(selector)`, `Reverse`, `MinBy`, and `File.ReadAllLines` all
  reported O(1) — a sort could disappear from the headline.
- **Toolchain moved to .NET 10.** All projects target `net10.0`, CI
  declares `10.0.x`, and `src/analyzer/global.json` pins the SDK. The
  analyzer solution is `Ohno.Complexity.slnx`, which the .NET 8 SDK
  cannot parse; CI passed only because hosted runners carry newer SDKs
  than the workflow declared. The Roslyn package reference is
  unchanged — 5.6.0 already ships `net10.0` assets.
- `SlidingWindow` and `ComboWindowAndUnique` report `O(k + n)` rather
  than `O(n)`. `new Queue<int>(k)` reserves k slots, and k is an
  independent dimension; the old result assumed k ≤ n.
- Insertion-sort-shaped loops report `O(n²)`. The amortized-pointer
  rule holds only while the inner counter keeps its position between
  outer iterations; a counter re-seeded inside the outer body (`j = i - 1`)
  breaks the argument. Two-pointer scans are unaffected.
- A literal loop ceiling on a constant-stepping counter is a fixed
  iteration count, so `for (j = 0; j < 8; j++)` inside a loop over n is
  O(n) rather than O(n²). A literal ceiling on a variable that halves
  stays logarithmic.
- Dropped the blanket *"Worst-case analysis used for branches."*
  warning. Taking the worst branch is how the model is defined, not a
  caveat about a particular result.

### Added

- **BCL catalog coverage** for the surface that made the old O(1)
  fallback load-bearing: `System.String` members, array and span
  helpers, `System.Collections.Frozen`, `SearchValues`, capacity
  constructors, the missing LINQ overload arities, and the .NET 9
  `Order` / `OrderDescending` / `CountBy` / `AggregateBy` operators.
- **Two-source sizing.** `a.Concat(b)` is |a| + |b|; folding the second
  source into the receiver would collapse an independent dimension.
  Static helpers take their size from the first non-literal collection
  argument, so `string.Join(", ", names)` is no longer sized by its
  separator.
- **Accessors, indexers, and operators are analyzed** and appear in the
  Complexity view, named as a reader expects (`Total.get`,
  `this[].get`). An expensive getter is one of the easiest places for
  an O(n) to hide. Auto-implemented accessors have no body and produce
  no result.
- `ohno.annotations.accessors` (`nontrivial` | `always` | `off`)
  controls the inline decoration only; analysis is unconditional.
- **`.slnx` solutions are discovered and loaded.** `MSBuildWorkspace`
  has supported the XML solution format since Roslyn 5.0, but the
  extension only looked for `.sln`, so a migrated repository silently
  fell back to ad-hoc compilation.
- **Non-backtracking regex earns a real bound.** A regex built with
  `RegexOptions.NonBacktracking` — provable at the construction site,
  on the static overload, or on `[GeneratedRegex]` — is linear in the
  subject, because that engine never revisits a character. The default
  backtracking engine stays opaque.
- Operation-tree depth guard. Generated code can nest far enough to
  overflow the stack, and the stack belongs to a server process shared
  by the whole editor session. Past the cap the result is an honest
  unknown with a reason, never a constant.
- Cancellation reaches inside a single method, so a superseded
  analysis stops instead of finishing work nobody is waiting for.
- `AnalyzerBenchmarkTests`, `BclCatalogTests`, `BoundaryBenchTests`,
  `MemberSurfaceTests`, `RegexEngineTests`, and `RobustnessTests`.
  Test count went from 201 to 311 on the analyzer and 44 to 54 on the
  extension.
- `docs/RESEARCH-2026-08.md` and `docs/PLAN-2026-08.md` — the audit
  behind this release and the plan it followed.

### Fixed

- **Selection and document analysis no longer cancel each other.**
  Both are Fast and both arrive on `ohno/analyze`, and a single shared
  cancellation slot meant an edit with an active selection had the
  document request cancel the selection every time. Cancellation is now
  keyed by request kind.
- Selection analysis no longer recomputes file-level bind warnings,
  which forced a full bind of every method body to score a two-line
  span.
- Seven duplicated operation walkers collapsed into one shared
  traversal, and several passes that asked the same sub-tree the same
  question repeatedly now ask once. `UnboundedWorklist` alone walked
  its loop body up to seven times.
- Disposing the analyzer client no longer leaves an unhandled promise
  rejection. Announcing shutdown races the pipe closing — expected,
  since the process is killed either way — but `sendNotification`
  returns a promise, so the synchronous `catch` never saw the `EPIPE`
  it rejected with.

## [0.1.2] — 2026-08

### Added

- **Approaches.** Up to three named readings of the same function
  (dominant, nested, sequential, alternative), so a function that
  combines several algorithms is not flattened to one number.
- **Selection-scoped analysis.** Selecting a statement or loop
  re-analyzes that span alone; the panel title becomes
  `Name (selection)`, with a hint to narrow further when more than one
  approach remains.
- `PatternRefiner`, which merges recurrence classifications into the
  pattern list and softens incidental opacity.

### Changed

- Incidental `await` or `IQueryable` beside a resolved loop is named
  rather than wiping the local bound. Hard opacity — `dynamic`,
  reflection, regex, `await foreach` — still reports `O(unknown)`.
- Deferred in-memory LINQ is distinguished from EF / `IQueryable`.

## [0.1.1] — 2026-08

### Added

- Bounding suggestions, confidence reasons, and the derivation tree in
  the Complexity view.
- Project binding: a loaded `.sln` or the `.csproj` found by walking up
  from the file, with entry-point analysis for top-level statements.

### Changed

- Inline annotation rendering, so a long bound stays readable.
- The Oʰ(Nᵒ) wordmark and a vector activity-bar icon.

## [0.1.0] — 2026-08

### Added

- Initial release: a VS Code extension estimating Big-O time and
  auxiliary-space complexity for C# functions, backed by a bundled
  Roslyn analyzer server over JSON-RPC.
- Symbolic complexity expressions with Big-O simplification, a BCL and
  LINQ cost catalog, recurrence classification, and hazard patterns.
- Inline end-of-line annotations, the Complexity activity-bar view, and
  on-demand deep analysis.
- A GitHub Actions workflow packaging per-platform VSIX artifacts.

[Unreleased]: https://github.com/jitterbox/Ohno/compare/v0.1.6...HEAD
[0.1.6]: https://github.com/jitterbox/Ohno/releases/tag/v0.1.6
[0.1.5]: https://github.com/jitterbox/Ohno/releases/tag/v0.1.5
[0.1.4]: https://github.com/jitterbox/Ohno/releases/tag/v0.1.4
[0.1.3]: https://github.com/jitterbox/Ohno/releases/tag/v0.1.3
[0.1.2]: https://github.com/jitterbox/Ohno/releases/tag/v0.1.2
[0.1.1]: https://github.com/jitterbox/Ohno/releases/tag/v0.1.1
[0.1.0]: https://github.com/jitterbox/Ohno/releases/tag/v0.1.0
