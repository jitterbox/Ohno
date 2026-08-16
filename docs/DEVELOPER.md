# Ohno developer guide

This document is the theoretical and operational map of the analyzer.
The [README](../README.md) is the product overview. This file is for
people changing the engine, adding languages, or evaluating whether a
bound is justified.

## 1. What problem Ohno solves

Ohno answers: **as a function of the input sizes visible in this
method, how does local work and peak extra memory grow?**

That is *algorithmic complexity* (Big-O / Θ-style bounds), not
*software complexity* (how hard the source is to read or test).

Microsoft already ships the latter:

| Microsoft metric | What it measures | Official docs |
|---|---|---|
| Cyclomatic complexity (CA1502) | Number of independent paths through a control-flow graph: *E − N + 1* | [CA1502](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1502) |
| Code metrics (VS) | Maintainability index, cyclomatic complexity, depth of inheritance, class coupling, lines of source/executable | [Code metrics values](https://learn.microsoft.com/visualstudio/code-quality/code-metrics-values) |
| CA1501 / CA1505 / CA1506 | Inheritance depth, maintainability, class coupling | same family as CA1502 |

A method with one `Array.Sort` has cyclomatic complexity 1 and time
O(n log n). A method with twenty `if` statements and no loops has high
cyclomatic complexity and time O(1). Ohno reports the first kind of
number. CA1502 reports the second.

Ohno's optional `ComplexityDiagnosticAnalyzer` (AL0001–AL0003) is a
convenience diagnostic for the Big-O estimate. It is **not** a
reimplementation of CA1502. See
[DiagnosticAnalyzer](https://learn.microsoft.com/dotnet/api/microsoft.codeanalysis.diagnostics.diagnosticanalyzer).

## 2. Theoretical model

### 2.1 Expressions, not strings

Bounds are immutable expression trees in `ComplexityAnalyzer.Core`:

| Node | Meaning | Example |
|---|---|---|
| `ConstantExpression` | Θ(1) | `1` |
| `VariableExpression` | An input dimension | `n`, `m`, `k` |
| `LogExpression` | Log of a size | `log n` |
| `PowerExpression` | Polynomial or exponential | `n²`, `2^n` |
| `FactorialExpression` | `n!` | permutations |
| `BinomialExpression` | `C(n, k)` | k-combinations |
| `ProductExpression` / `SumExpression` | Compose | `n log k`, `n + m` |
| `FunctionCostExpression` | Opaque named call | `C(Process)` |
| `UnknownExpression` | No honest bound | `unknown` |

`Cx` normalizes on construction (flatten, drop 1, combine `n * n` →
`n²`). `ComplexitySimplifier` then applies Big-O rules: drop dominated
terms, distribute products over sums, never collapse independent
dimensions.

Dominance order on a single variable: **factorial > exponential >
polynomial > log**. `n * n!` dominates `n`. `n + m` stays `n + m`
unless a relationship is known. An extra `C(name)` is never absorbed
by a term that lacks that call.

### 2.2 Time composition

| Construct | Time |
|---|---|
| Sequence | Sum (then dominate) |
| Loop | Bound × body |
| `if` / `switch` | Condition + worst branch (not the sum of exclusive arms) |
| Call | Catalog bind, walked body, `C(name)`, or unknown |

Loop bounds come from `Length` / `Count`, a compared integral
parameter, a recognized log update (`*= 2`, `/= 2`, `>>= 1`), a
null-terminated walk, a visited-queue frontier, or a refill
worklist (iterations are **not** `Count`). A literal ceiling on a
counter that steps by a constant is a fixed count — `j < 8` does not
inherit the enclosing loop's bound — but a literal ceiling on a
variable that halves stays logarithmic. See
[iteration statements](https://learn.microsoft.com/dotnet/csharp/language-reference/statements/iteration-statements).

`CardinalityAnalyzer` walks
[`ControlFlowGraph`](https://learn.microsoft.com/dotnet/api/microsoft.codeanalysis.flowanalysis.controlflowgraph)
for reachability and SizeDelta (seed / current / max). Unreachable
`Enqueue` does not grow space.
[`AnalyzeDataFlow`](https://learn.microsoft.com/dotnet/api/microsoft.codeanalysis.dataflowanalysis)
marks increment locals so they never become Big-O names.

### 2.3 Space is peak retained memory

This is the rule that most “allocation counters” get wrong.

- `new int[n]` inside a loop, reference dropped each iteration →
  **peak Θ(n)**, time Θ(n²) (zero-init × iterations).
- `buffers.Add(new int[n])` for n iterations → **retained Θ(n²)**.
- `new int[n, n]` → Θ(n²) cells
  ([arrays](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/arrays)).
- Fibonacci recursion: Θ(2^n) *time*, Θ(n) *stack*. The recursion
  tree is not live stack.

`CostComposer.Loop` multiplies time and **does not** multiply space.
`NoteGrowth` multiplies only when an allocation is stored into a
collection that outlives the iteration.

Output that the method returns (adjacency list, all subsets) is
counted as retained if it is live at the return.

### 2.4 Recurrences

`RecurrenceAnalyzer` does **not** solve general recurrences. It
classifies a handful of source idioms:

| Idiom | Time | Space |
|---|---|---|
| `T(n)=T(n-1)+O(1)` | O(n) | O(n) stack |
| `T(n)=2T(n/2)+O(n)` | O(n log n) | O(n) |
| Exclusive mid-split (`?:` or `if` / trailing `return`) | O(log n) | O(log n) |
| Sequential `n-1` and `n-2` | O(2^n) | O(n) |
| 2D memo table write | size of table | table (+ stack) |
| Two `index+1` calls + copy | O(n 2^n) | O(n 2^n) |
| Loop + recurse + clone of n | O(n n!) | O(n n!) |
| Loop + recurse + clone of k | O(k C(n, k)) | O(k C(n, k)) |
| Recurse on neighbors + `bool[]` | O(k n) | O(n) |

The **amortized pointer step** — an inner `while` that only advances a
pointer costs O(1) per outer iteration — applies only while the
pointer keeps its position between outer steps. If the counter is
re-seeded inside the outer body (assigned, or declared there, as
insertion sort's `j = i - 1` does), the amortization does not hold and
the inner loop is charged its own bound. Two-pointer scans are
unaffected: their pointer never rewinds.

Local-function **declarations** are not walked as executable
statements (`ILocalFunctionOperation`). Cost is paid at the call.
Otherwise subset/permutation/DFS bodies would be counted twice.
1D memo tables and named algorithms (Dijkstra, Kahn, two-pointer)
are not first-class recurrence ids; those appear only if the
walk/catalog already produces the bound.

### 2.5 Patterns and honesty

`PatternRecognizer` names hazards that do not require solving math:
`dynamic`, reflection, interface dispatch, regex, streams, parallel,
`IQueryable` / EF, expression compile, await / `await foreach`,
unproven loops, null-terminated walks, numeric countdown, locks,
yield, deferred in-memory LINQ, cache hit/miss, data-dependent
recursion.

`RecurrenceAnalyzer` classifications (binary search, memo, subset /
perm generation, visited graph walk, linear / D&C / branching
recursion) are merged into the same list. A second integral
parameter that is not decreased in the recursive calls is a
**bounded recursion** alternative, not a rewrite of the headline.

Regex is two patterns, not one. The default engine backtracks, so it
stays hard-opaque. `RegexOptions.NonBacktracking` carries a documented
linear-time guarantee from the runtime, so `RegexFacts` gives it a real
bound in the subject's length and the `regex-linear` id annotates
rather than wipes. The option has to be provable at the construction
site, on the static overload, or on `[GeneratedRegex]`; a `Regex`
arriving as a parameter keeps the opaque treatment.

`PatternRefiner` then:

- drops `data-dependent-recursion` when a recurrence already solved
- **softens** incidental opacity (`await`, `IQueryable`, stream,
  interface, delegate, thread wait) to Annotate when a structural
  loop or recurrence bound exists — the local bound is kept
- leaves **hard** opacity (dynamic, reflection, regex, expression
  compile, unproven loop, unbounded worklist, `Parallel`,
  `await foreach`) as a wipe

Effects:

- **Unknown** — replace a lying O(1) / bare `C(name)` with
  `O(unknown)` and a reason.
- **Range** — keep a bound when we have one; explain best/worst
  (cache, branching recursion).
- **Annotate** — keep the bound; state the assumption.

`ApproachSummarizer` returns at most three readings (`dominant`,
`nested`, `sequential`, `alternative`) plus a hint to select a
smaller region when more than one applies. Selection-scoped
analysis is described in 2.6. Deferred LINQ is not EF;
EF/`IQueryable` is a
separate dominant + “if the provider scans” alternative. Nested
or sequential patterns are listed, not flattened to the first
opaque id.

`O(n C(Process))` is kept: that *is* a stated bound. A bare
`external.Run()` becomes Unknown.

### 2.6 Selection analysis

A non-empty editor selection is a second analyze request with
`AnalyzeRequest.selection`. Inline decorations still come from the
full-document result.

`SelectionFragment` maps the span to the tightest statement-level
`IOperation`s in the enclosing method (including local functions
and top-level `Main`). Columns are clamped to the line. Nested
loops tighten into the body when the span is fully inside it.

`CSharpMethodAnalyzer.AnalyzeSelection` reuses the same dimensions,
catalog, and pattern pipeline on that fragment. Recurrence
classification runs on a merged root (parent climb capped at 16
hops / 64 children) so a huge multi-statement selection stays
Unknown rather than walking an unbounded tree.

The result is named `Method (selection)`. `ApproachSummarizer`
uses the selection hint: *Narrow the selection for a tighter
per-algorithm bound.* The panel shows this result only when the
document version matches and the caret/start of the selection
falls inside the analyzed span.

### 2.7 Confidence

High is reserved for work that does not depend on an idiom matcher.
Everything below High must list `ConfidenceReasons`:

- recurrence classified as X; another control-flow shape may miss it
- `Count > k` + `Dequeue` assumed
- retained allocation assumed stored in a live collection
- log bound assumed from doubling/halving
- frontier assumed from `visited[]` + `queue.Count`
- inner collection size is a fresh dimension, not proven |E|
- catalog cost is amortized or expected
- `C(name)` / unknown cost in the expression
- named hazard reasons (dynamic, regex, …)

The panel shows these under **Confidence**. They are assumptions, not
a formal unsoundness proof. Recurrence and soft-hazard ids also
cap confidence at Medium when they only annotate.

## 3. Roslyn pipeline

### 3.1 Fast tier

When a `.sln` or `.slnx` is found, or a `.csproj` is found by
walking up from the file, and that graph is ready, fast uses the
same project
`SemanticModel` as deep (buffer overlaid). It does **not** wait on
an in-progress solution open. It does wait for the workspace gate
once the graph is ready, so a deep run cannot silently force ad-hoc.

Otherwise `CompilationFactory` builds a
[`CSharpCompilation`](https://learn.microsoft.com/dotnet/api/microsoft.codeanalysis.csharp.csharpcompilation)
from the buffer plus trusted platform assemblies and SDK implicit
usings (`System.Collections.Generic`, `System.Linq`, …) so a
LeetCode-style file still binds `PriorityQueue`.

Top-level statements compile as an exe so `<Main>$` exists and is
annotated as `Main`. Primary-constructor parameters are dimensions
when a method reads them. Local functions are paid at the call and
are not listed as top-level results.

Unresolved types (`CS0246` / `CS0234`) and file-based `#:package`
directives become file warnings. `#if` follows *this* compilation's
symbols (project defines when the workspace is ready; none on
ad-hoc).

### 3.2 Deep tier

[`MSBuildWorkspace`](https://learn.microsoft.com/dotnet/api/microsoft.codeanalysis.msbuild.msbuildworkspace)
loads the solution
([workspaces](https://learn.microsoft.com/dotnet/csharp/roslyn-sdk/work-with-workspace)).
The same method walker runs on the project's `SemanticModel`. Deep
**waits** for that load. If MSBuild cannot be located or a project
fails to load, Ohno falls back to the ad-hoc compilation and records
a warning. Deep must not invent a tighter bound that the fast tier
would call Unknown (interface / BCL-virtual / opaque System APIs).

### 3.3 Operation kinds we treat specially

| Operation | Docs / language | Ohno behavior |
|---|---|---|
| `IArrayCreationOperation` | [Arrays](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/arrays) | Product of dimension sizes |
| `IForEachLoopOperation` | [foreach](https://learn.microsoft.com/dotnet/csharp/language-reference/statements/iteration-statements#the-foreach-statement) | Bound = collection size; `await foreach` is hard-opaque (`await-foreach`) |
| `IAwaitOperation` | [async](https://learn.microsoft.com/dotnet/csharp/asynchronous-programming/) | Soft: wipe only when there is no structural bound; otherwise Annotate |
| `ILockOperation` | [lock](https://learn.microsoft.com/dotnet/csharp/language-reference/statements/lock) | Annotate; local work O(1), wait is external |
| yield (`OperationKind.YieldReturn`) | [yield](https://learn.microsoft.com/dotnet/csharp/language-reference/statements/yield) | Annotate; cost depends on consumption |
| `IDynamicInvocationOperation` | [dynamic](https://learn.microsoft.com/dotnet/csharp/advanced-topics/interop/using-type-dynamic) | Unknown |
| `ILocalFunctionOperation` | local functions | Declaration is free; call is paid |
| `IForToLoopOperation` | VB `For…To` | Bound not inferred |
| `ICollectionExpressionOperation` | collection expressions / spreads | Sum of element and spread sizes |
| `IInterpolatedStringOperation` | interpolated strings | Not treated as O(1) when holes have size |

C# indexers are `this[]` in metadata; writes use
`IPropertyReferenceOperation.Property.IsIndexer`. Array elements are
`IArrayElementReferenceOperation` — `graph[i].Add` must resolve the
array symbol, not the indexer property.

### 3.4 Catalog

`OperationCatalog` keys `ContainingType#Name#Arity`. Time/space
templates bind to the receiver (or LINQ source) size. Deferred LINQ
is O(1) to build; materializing operators (`ToArray`, `string.Concat`)
pay the source size. `Repeat` + `Concat` is element length × count.

Two-source operators (`Concat`, `Union`, `Intersect`, `Except`, `Zip`,
`SequenceEqual`, and the `…By` variants) are sized by **both** sides:
`a.Concat(b)` is |a| + |b|. Folding the second source into the
receiver would collapse an independent dimension, which §2.1 forbids.

For a static helper, the size comes from the first non-literal
collection argument, not from argument zero — otherwise
`string.Join(", ", names)` would be sized by the separator and report
constant time.

### 3.5 Limits that keep the server alive

The analyzer is a long-lived stdio process shared by the whole editor
session, so a crash costs the loaded workspace, not just one result.

`AnalysisState.MaxDepth` (8) bounds how many **methods** deep the walk
follows calls. `AnalysisState.MaxOperationDepth` (400) bounds how deep
a **single body** may nest, and guards the cost walk, the pattern walk,
and the cardinality walk. Generated code — a long chained expression, a
deeply nested initializer — otherwise recurses far enough to overflow
the stack.

Past the operation cap the result is `O(unknown)` with a reason, never
a constant: work that was not examined has not been shown to be free.
The cap is a floor against machine-written code, not a budget ordinary
code approaches, and `OrdinaryNesting_IsStillAnalyzedNormally` pins
that distance.

`AnalysisState.Token` is checked inside the walk, so a superseded
analysis stops instead of finishing work nobody is waiting for.

### 3.6 One in-flight request per kind

Document and selection analysis are both Fast and both arrive on
`ohno/analyze`. `AnalyzerService` keeps a **separate `CancelSlot` per
kind**, so a newer document analysis supersedes only the previous
document analysis. A single shared slot made them cancel each other:
an edit with an active selection schedules both, the document request
lands second, and the selection result was dropped every time. Deep
runs are user-initiated and are never superseded.

### 3.7 O(1) is never a fallback

**An unresolved executable operation costs `C(name)` at Low
confidence.** Constant time has to be positively known, which means a
catalog entry or an entry in `ConstantTimePrimitives`. There is no
"it looked like a primitive" path, because that is precisely how an
`OrderBy(keySelector, comparer)` used to report O(1).

`ConstantTimePrimitives` is keyed by containing type, not by member
name: `int.GetHashCode()` is Θ(1) and `string.GetHashCode()` is
Θ(length). It holds whole constant types (`System.Math`, `System.GC`,
`System.Console` — Ohno makes no I/O claim), fixed-width scalar
members, cached-singleton accessors (`StringComparer.Ordinal`), and
individually listed members.

The rule applies at every site that could invent a constant:

| Site | Constant only when |
|---|---|
| Call | catalog hit, or `ConstantTimePrimitives.IsConstant` |
| Constructor | catalog hit, collection copy, declared in this compilation, or a parameterless BCL ctor |
| Property read | getter walked, catalog hit, declaring type is in this compilation (auto-properties), or a listed accessor |
| Method with no body | auto-implemented accessor. Abstract / extern / partial are calls |

Genuinely free operations stay free and are enumerated so the audit
is reviewable: unreachable code, a declarator with no initializer, an
empty switch, and an `ILocalFunctionOperation` **declaration** (cost is
paid at the call — §2.4).

## 4. Test fixtures

All live under `samples/` and are asserted in
`src/analyzer/ComplexityAnalyzer.Tests`.

### 4.1 LeetCode bench

`samples/leetcode/OptimalSolutions.cs` — known-optimal solutions.
`LeetCodeBenchTests` locks time and space.

Coverage includes hash maps, two pointers, binary search, heap top-k,
interval merge, 3-sum, house robber, linked-list reverse/cycle,
prefix products, rotated search, trapping rain, group anagrams, coin
change, LIS, Dijkstra (`NetworkDelayTime`), and Kahn (`CanFinish`).
This is the regression net against “we made TwoSum O(n²)” or
“TopK space collapsed k into n”.

### 4.2 Torture / edge cases

`samples/roslyn/RoslynComplexityEdgeCases.cs` — adversarial
methods including unbounded BFS. Comments use:

| Tag | Meaning |
|---|---|
| INCONCLUSIVE | Source alone cannot justify a bound |
| CONTEXT_DEPENDENT | A bound needs an explicit assumption |
| RANGE | Best/worst differ |
| DERIVABLE_WITH_SUMMARIES | Possible if callees are walked or cataloged |
| NON_TERMINATION_RISK | May not halt for all values of the static types |

`EdgeCaseTortureTests` requires fast ≡ deep on the headline bound,
and inconclusive cases to be `O(unknown)` with a pattern id
(dynamic, reflection, interface, regex, stream, queryable,
`await-opaque`, `await-foreach`, Collatz). Deep must not “fix”
these into High O(1). An `await` beside a resolved loop is
Annotate + the loop bound, not a wipe.

Local bodies that *are* in compilation (expensive property, indexer,
custom `MoveNext`, user-defined `+`) must be walked.

### 4.3 Space patterns and combinations

`RoslynSpaceComplexityPatterns.cs` — 24 peak-space idioms (constant,
linear, m+n, mn, n², n³, peak vs retain, top-k, window, unique set,
adjacency list/matrix, binary-search stack, linear/fib stack, 2D
memo, n log n retain, subsets / perms / combinations, concat,
BFS/DFS).

`RoslynSpaceComplexityCombinations.cs` — matrix+buffer, peak-then-
retain, window+set, buffer+linear recursion. Peak and independent
dimensions must compose.

`SpacePatternTests` also asserts High has no confidence reasons and
that window/Fibonacci are Medium with a matching reason.

### 4.4 Other tests

| Suite | Role |
|---|---|
| `CardinalityGapTests` | Worklists, SizeDelta, heapify, SortedSet, Span, CFG |
| `BclCatalogTests` | Everyday BCL: comparer/selector overloads, string members, spans, frozen sets, two-source LINQ. Asserts no bound collapses to a constant and no ordinary call leaves a `C(name)` |
| `AnalyzerBenchmarkTests` | Wall-clock ceilings for the debounce path; prints per-fixture and per-function timings. Numbers are indicative — the same fixture has varied 2x run to run — so the ceiling is the contract, not the printed figure |
| `RegexEngineTests` | Backtracking stays opaque; `NonBacktracking` earns a linear bound, including combined options, the static overload, and a materializing `Replace` |
| `BoundaryBenchTests` | The adjacent-class boundaries learned predictors slide between: linear vs linearithmic, linearithmic vs quadratic, log vs linear, and shapes that look heavier or lighter than they are |
| `MemberSurfaceTests` | Which members become results: scanning accessors and operators appear, auto-properties do not |
| `RobustnessTests` | Generated-code shapes that must degrade rather than crash: 5,000-term expressions, 20,000-element initializers, 600-deep nesting, plus cancellation reaching inside a single method |
| `AcceptanceTests` | Linear/nested/triangular loops, dictionary writes, literals |
| `RecursionAndLinqTests` | Linear recurrence, merge-sort shape, `IQueryable` unknown |
| `AlgebraTests` | Simplifier / dominance |
| `ExplanationFormatterTests` | Gloss phrases |
| `ServerProtocolTests` | JSON-RPC initialize, analyze, deferred-LINQ approaches, selection |
| `PatternApproachTests` | Soft await, binary search vs data-dependent, bounded recursion, deferred LINQ alternatives |
| `SelectionAnalysisTests` | Inner-loop drop of outer bound, multi-loop hint, span outside a method |
| `CompilationContextTests` | Top-level Main, primary ctor, local fn, bind warnings |
| `ProjectWorkspaceTests` | Fast uses project `#define`s when the workspace is ready |
| Extension Vitest | Normalize, panel (approaches + hint), decorations, selection store, RPC round-trip |

## 5. Extension

- `src/extension` — VS Code/Cursor extension (`ohno`).
- `src/extension/src/ui/complexityModel.ts` — summary tree: gloss,
  approaches, patterns (with source range), confidence reasons,
  dimensions, warnings.
- Selection analysis is a second `ohno/analyze` with `selection`,
  debounced (≤ 200 ms), ticketed so a stale response cannot land,
  and stored separately from document functions.
- Editor annotations are gated by `ohno.annotations.mode`
  (`inline`, `codelens`, or `off`). The old
  `ohno.annotations.showInline` boolean is still read: `false` with
  the default `inline` mode is treated as `off`.
- Accessors, indexers, and operators are **analyzed** like any other
  member and always appear in the panel; `ohno.annotations.accessors`
  controls only whether they get an inline decoration, defaulting to
  `nontrivial` so a class of plain properties does not line the margin
  with `O(1)`. Auto-implemented accessors (`get;`) have no body and
  produce no result at all.
- Hover markdown is implemented but not registered by default; the
  panel is the primary UI.
- Protocol field names are camelCase in TypeScript and PascalCase on
  the .NET DTO; `normalize.ts` accepts both. Required function
  fields include `approaches` and `selectionHint`.

## 6. How to extend

### Add a BCL summary

Register in `OperationCatalog` with type, name, arity, `SizeKind`,
and `CostKind`. Use `Expected` / `Amortized` when the textbook bound
is not worst-case (hash table, `List.Add`). Add an acceptance test.
Refresh `src/shared/catalog.json` with `OHNO_WRITE_SHARED=1` so the
TypeScript port keeps the same table.

**Register every arity.** The catalog is keyed by arity, so
`OrderBy#2` does not cover `OrderBy(keySelector, comparer)`. A missing
overload is no longer silently constant — it becomes `C(name)` at Low
confidence (§3.7) — but that is a visible defect, not a correct
answer. Add the case to `samples/roslyn/RoslynBclCatalog.cs`, which
exists to use everyday APIs rather than only the ones already known.

Only put a member in `ConstantTimePrimitives` when its cost is fixed
regardless of any input size. Anything size-dependent belongs in the
catalog with a real template.

**Do not version catalog entries by TFM.** The table is the current
BCL. The same source is meant to get the same bound on every
supported runtime. Constant-factor changes (SIMD `IndexOf`,
`SearchValues`, culture vs ordinal `string.IndexOf`) stay the same
`SizeKind`. The few historical class changes — `List.Sort` /
`Array.Sort` worst-case O(n²) before Framework 4.5 (now introsort),
hash-flooding `Dictionary`/`HashSet` string keys on Framework
without randomized hashing — are recorded as the modern cost
(`sorts: true`, `CostKind.Expected`). Fast analysis sees the
server's net10 platform assemblies; deep analysis uses
`MSBuildWorkspace` so a `net8.0` project binds the APIs that
exist there, then looks those members up in this same table. A
member that does not exist on the user's TFM simply never appears.

### Add a hazard pattern

Add a detector in `PatternRecognizer.Match`. Return `Unknown`,
`Range`, or `Annotate`. Put **hard** opaque ids in
`PatternApplicator.IsOpaque` if the whole method should become
`O(unknown)` even when a loop bound exists. Soft hazards belong in
`PatternRefiner.IsSoft`. Add a torture case and, when the shape is
a named algorithm, an `ApproachSummarizer` entry.

### Add a recurrence idiom

Extend `RecurrenceAnalyzer` only when the shape is recognizable
without solving math. Cap confidence at Medium and `state.Note` why.
Add a fixture method with a known closed form. Surface the form id
on `AnalysisState.RecurrenceId` so `PatternRefiner` can merge it.

### Add a language

Implement the `IOperation`-equivalent walk (or a compiler API walk),
emit `ComplexityResult`, and keep the Core algebra unchanged. Update
`src/shared/protocol.schema.json` (then `protocol.ts` and
`Contracts.cs`) only if the wire shape changes. Algebra goldens live
in `src/shared/algebra-vectors.json`.

## 7. Limits (do not paper over these)

Ohno **will miss** equivalent algorithms that use:

- `goto` / `do` / `for (;;)` without a readable condition
- recursion behind a delegate, interface, or helper
- LINQ where a loop idiom was expected
- custom `IEnumerable` without a walked `MoveNext`
- BFS without a visit mark (reported `O(unknown)`, not a fake `O(n)`)
- subset/permutation copies via `AddRange` / builders / `yield`
- 1D memo (`dp[i] = …`) — only 2D indexers classify as memo
- a depth cap on a recursive helper that hides the recursive call

When that happens, prefer **Unknown + reason** or a looser composed
form (`O(n C(f))`) over a tight High bound.

### 7.1 Known unsupported (will not be built)

| Case | Status |
|---|---|
| `#:package` / `#:sdk` NuGet restore | Detect + warn only. No restore on the debounce path. |
| Hosting source generators on ad-hoc fast | Loose files do not run generators. A loaded project compilation may. |
| Every `#if` configuration | One compilation (project or ad-hoc). Other arms are invisible. |
| `.razor` / `.cshtml` / `.csx` | Not hosted. `languageId` is not `csharp`. |
| Tight bounds for `IQueryable`, `dynamic`, expression trees | Opaque / unknown. Do not invent High O(1). |

## 8. Commands

Requires the **.NET 10 SDK**. `src/analyzer/global.json` pins it, and the
solution is `Ohno.Complexity.slnx` — the XML solution format, which
older SDKs cannot parse.

```bash
dotnet test src/analyzer            # builds the whole .slnx
dotnet test src/analyzer/ComplexityAnalyzer.Tests -c Release
cd src/extension && npm test

dotnet publish src/analyzer/ComplexityAnalyzer.Server \
  -c Release -r linux-x64 --self-contained \
  -o src/extension/server
# Windows: -r win-x64 (produces ComplexityAnalyzer.Server.exe)
```

Do not commit unless asked. Do not skip hooks. Do not force-push
`main`.
