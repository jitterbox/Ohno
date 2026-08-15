# Roslyn & complexity-analysis research — August 2026

Audit target: `master` @ `6a8e397` (v0.1.2, "Return multiple algorithm
approaches and re-analyze the editor selection").

This document is the **findings** half. The **work plan** is
[PLAN-2026-08.md](PLAN-2026-08.md). Nothing in the codebase was changed
to produce this report.

Scope: (1) what the current Roslyn/.NET platform offers that Ohno does
not yet use, (2) what the static resource-analysis literature says about
the technique Ohno implements, (3) a line-level audit of accuracy,
algorithmic cost, and UX against both.

---

## 1. Platform research

### 1.1 Roslyn version state — package current, toolchain was not

`Microsoft.CodeAnalysis.CSharp` **5.6.0** (2026-07-02) is the latest
release, and it is what every project here references. Any claim that
Ohno is "behind" on the Roslyn *package* is wrong.

The **SDK** was another matter. Every project targeted `net8.0` and all
four CI jobs declared `dotnet-version: '8.0.x'`, while
`src/analyzer` contains only `Ohno.Complexity.slnx`. **The .NET 8 SDK
cannot parse `.slnx`** — SDK ≥ 9.0.200 is required. Reproduced here on a
clean 8.0.129 install:

```
$ dotnet restore                       # in src/analyzer
error MSB1003: Specify a project or solution file.
$ dotnet restore Ohno.Complexity.slnx
error MSB4068: The element <Solution> is unrecognized…
```

CI is nonetheless green (run #6 on `master`) because the hosted runner
image also carries newer SDKs and nothing pins the version, so the CLI
silently selects a higher one than the workflow declares. The declared
toolchain and the working toolchain were different — a contributor with
only the declared SDK could not build at all.

**Resolved in this branch**: the analyzer moved to the **.NET 10 SDK**
(10.0.110) and `net10.0`, with a `global.json` pinning it so the
mismatch cannot recur silently. Roslyn 5.6.0 ships `net10.0` assets, the
whole `.slnx` now builds from a bare `dotnet test`, and the CS9057
"analyzer references a newer compiler" warnings that the 8.0 SDK emitted
on every build are gone.

What *is* unused is a set of APIs that arrived across 4.x → 5.x:

| API | Status in Ohno | Value |
|---|---|---|
| `OperationExtensions.Descendants()` | **0 usages**; 7 hand-rolled walkers instead | Correctness parity, less code, no nested-iterator cost |
| `OperationWalker` / `OperationWalker<T>` | unused | Single-pass multi-detector traversal |
| `.slnx` in `MSBuildWorkspace` (5.0+) | server can load it, **extension never finds it** | This repo's own solution is `Ohno.Complexity.slnx` |
| `Microsoft.CodeAnalysis.AnalyzerUtilities` 5.6.0 (PointsTo / ValueContent / Copy analysis) | unused | Principled alias + capacity tracking (see §3.5) |

### 1.2 Language and BCL surface Ohno does not model

- **C# 14 extension members** (`extension` blocks) lower to ordinary
  static methods with the receiver as the first parameter, so
  `IsExtensionMethod`-based receiver resolution keeps working. Low risk,
  but worth a fixture: `ReceiverSize`
  (`CSharpMethodAnalyzer.Calls.cs:286`) has no test for the new syntax.
- **`RegexOptions.NonBacktracking`** (.NET 7+) switches to a
  DFA/NFA simulation with a **guaranteed O(n) scan** — no
  catastrophic backtracking. Ohno currently marks *all* of
  `System.Text.RegularExpressions.Regex` hard-opaque
  (`PatternApplicator.IsOpaque`), which is honest for the default
  engine but **needlessly pessimistic** when the source says
  `NonBacktracking` or `[GeneratedRegex(..., RegexOptions.NonBacktracking)]`.
- **Uncataloged modern BCL**: `System.Collections.Frozen`
  (build O(n), lookup O(1)), `SearchValues<T>`,
  `MemoryExtensions` (`Span` `IndexOf` / `Sort` / `BinarySearch`),
  and the .NET 9/10 LINQ additions (`Order`, `OrderDescending`,
  `CountBy`, `AggregateBy`, `Index`, `Shuffle`).

### 1.3 Prior art

There is no established Roslyn-based Big-O estimator to converge with;
the adjacent .NET tools (Roslynator, `Microsoft.CodeAnalysis.NetAnalyzers`
CA1502, VS code metrics) all measure *software* complexity, which
`docs/DEVELOPER.md` §1 already distinguishes correctly. Ohno's
positioning claim holds up.

---

## 2. Literature research

### 2.1 What real resource-bound analyzers do

The mature systems — **COSTA/PUBS**, **SPEED**, **Loopus**, **KoAT2**,
**CoFloCo**, **RAML** — all share a shape Ohno partly reinvents:

1. Abstract the program to an **integer transition system** / cost
   relations.
2. Find **ranking functions** per loop to get a *local* bound.
3. Compose local bounds along the call/loop nesting into a global bound.
4. Where composition would over-count, use **amortization** (potential
   functions) — this is exactly the trick Ohno hand-codes as
   "amortized pointer step" and the worklist detectors.

The transferable lesson is **not** "add an SMT solver" — the IDE latency
budget forbids it. It is the *layering*: local bound → composition →
amortization, each with an explicit justification. Ohno already does
this informally; the audit below shows where the layering leaks
(§3.2 loop bounds re-derived per node; §3.1 unknown members silently
costed as O(1)).

Two specifics worth stealing:

- **Ranking-function discipline for `while` loops.** Ohno infers a bound
  from the *condition shape* only (`LoopBoundInferrer.InferBinary`). The
  literature's minimum bar is a variable that provably decreases and is
  bounded below. Roslyn's `ControlFlowGraph` is already built in
  `CardinalityAnalyzer.MarkUnreachable` — the back-edge information is
  right there, unused.
- **Independent dimensions must stay independent** — Ohno gets this
  right and it is worth defending in tests; most ML-based predictors
  (below) cannot express it at all.

### 2.2 The ML/benchmark line of work

**BigO(Bench)** (Meta, 2025; 3,105 problems / 1.19M solutions with
inferred time *and* space labels), **CodeComplex** (4,900 Java + 4,900
Python, expert-labelled, 7 classes, EMNLP Findings 2025), and **TASTY**
are *classifiers*, not derivers: they emit a class label with no
derivation, no dimensions, and no honest "unknown". They are the wrong
engine for Ohno — but they are an excellent **validation corpus**, and
their published failure mode is instructive: models confuse
hierarchically adjacent classes (O(n log n) ↔ O(n) ↔ O(n²)). That is
precisely the boundary Ohno's `LeetCodeBenchTests` guards, which
suggests the bench should be *widened*, not replaced.

---

## 3. Audit findings

Severity: **S1** wrong output users would act on · **S2** materially
degraded output or responsiveness · **S3** polish.

### 3.1 [S1] An uncataloged `System.*` member is costed as O(1) — **fixed**

`CSharpMethodAnalyzer.Calls.cs:98-108`:

```csharp
private static bool IsSystemPrimitive(IMethodSymbol method)
{
    var ns = method.ContainingNamespace?.ToDisplayString() ?? "";
    if (!ns.StartsWith("System") && !ns.StartsWith("Microsoft")) return false;
    return !DimensionInferrer.IsCollection(method.ContainingType);
}
```

Any `System.*` method whose **containing type** is not itself a
collection is treated as a constant-time primitive
(`Calls.cs:45-65` → `Cx.One`, confidence Medium).
`System.Linq.Enumerable` is a static class, so it is not a collection —
therefore **every LINQ overload missing from the catalog is silently
O(1)**:

| Call | Reported | Actual |
|---|---|---|
| `.OrderBy(k, comparer)` (arity 3) | **O(1)** | O(n log n) |
| `.Sum(selector)` (arity 2) | **O(1)** | O(n) |
| `.Reverse()`, `.Concat`, `.Zip`, `.Union`, `.Except`, `.Intersect` | **O(1)** | O(n) / O(n+m) |
| `.MinBy`, `.MaxBy`, `.DistinctBy`, `.Chunk`, `.ToHashSet`, `.ElementAt`, `.Last`, `.Average` | **O(1)** | O(n) |
| `File.ReadAllLines(path)` | **O(1)** | O(file) |

The catalog registers `Sum`/`Min`/`Max` at arity 1 only and `OrderBy` at
arity 2 only, so the *selector* and *comparer* overloads — the common
ones in real code — all fall through. This directly contradicts the
product promise in `README.md` ("it does not invent O(1)").

Mitigating: confidence is capped at Medium with the reason *"A System
API was treated as a constant-time primitive without a catalog
summary."* So it is not silent — but the **headline number is wrong**,
and the headline is what the inline annotation shows.

**Root cause is the polarity of the default.** For an unresolved
`System.*` member the safe default is `C(name)` (visible, honest, Low
confidence), not `1`.

**Fixed in this branch (PLAN Phase 1).** `IsSystemPrimitive` is gone;
constant time now requires a catalog entry or a
`ConstantTimePrimitives` entry, and the same rule was applied to
constructors, property reads, and bodyless methods. `BclCatalogTests`
asserts that a comparer overload never collapses to a constant.

### 3.2 [S1] `System.String` members are the opposite inconsistency — **fixed**

`string` *is* a collection by `IsCollection`, so `IsSystemPrimitive`
returns false and every uncataloged string member falls to
`UnknownCall` (`Calls.cs:67-70`) → `C(Substring)`, confidence **Low**,
plus a warning. The catalog has exactly four `System.String` entries
(`get_Length`, `ToCharArray` ×2, `Concat#1`).

So `Substring`, `Split`, `IndexOf`, `Replace`, `Trim`, `StartsWith`,
`ToUpper`, `string.Join`, `string.Format` — the most common C# calls
there are — each drag a method to Low confidence with an opaque
`C(name)` in the bound. Two different wrong behaviours from the same
missing-catalog cause, in opposite directions, decided by an incidental
type test.

The fixture suite never catches this: `samples/` and `test/fixtures/`
contain **zero** uses of `Substring`, `Split`, `Take`, `Skip`, `Zip`,
`Concat`, `MinBy`, `ToHashSet`, `Order`, or `Array.Copy`. The corpus is
self-consistent with the catalog, so real-world code hits a cliff the
tests cannot see.

**Fixed in this branch.** The catalog gained the string, array, span,
frozen-collection, and LINQ-overload surface, and
`samples/roslyn/RoslynBclCatalog.cs` exists specifically to use
everyday APIs rather than only the ones already known — the blind spot
this finding is about.

### 3.3 [S2] Analyzer-internal algorithmic cost — **fixed (PLAN Phase 2)**

The tool that reports Big-O has several avoidable super-linear passes.
Let *N* = operations in a method, *D* = block nesting depth.

| Site | Current | Should be |
|---|---|---|
| 7 × hand-rolled `Walk` (`LoopBoundInferrer.cs:288`, `CardinalityAnalyzer.cs:345`, `RecurrenceAnalyzer.cs:421`, `PatternRecognizer.cs:531`, `HeapBoundDetector.cs:80`, `WorklistBoundDetector.cs:297`, `SelectionFragment.cs:200`) | nested `yield return` → **O(N·D)** enumerator hops + 7 duplicated implementations | `operation.Descendants()` (Roslyn, iterative) |
| `CardinalityAnalyzer.IsIncremented` (`:126`) | re-walks the **whole body per written symbol** → **O(N·W)** | build the increment set once → O(N) |
| `CardinalityAnalyzer.LoopBound` (`:173`) calls `LoopBoundInferrer.Infer` for every loop during `ApplyTree`, and the main walk calls it again; each `Infer` runs up to 4 full sub-tree walks (`IsLogarithmic`, `IsHalvingWhile`, `IsBinaryPartition` ×2, `TryFrontier`) | **O(N·D)** at best, repeated | memoize per `IOperation` |
| `PatternRecognizer` (`:34-46`) | ~17 detectors per node, several doing their own `WalkAll` of the same sub-tree; `UnboundedWorklist` alone does 5 sub-walks + `IsNetDecrease` 2 more | one pass, detectors as visitors |
| `RecurrenceAnalyzer.TrySolve` | ~10 independent full-body walks (`FindRecursive`, `IsBinarySearch`, `TryMemoized`, `HasMaterializedCopy` ×2, `CopiedSize`, `TryGraphWalk` ×2, `NoteBound`) | one materialized descendant list |
| `CostComposer.Sequential/Loop/Conditional` | `ComplexitySimplifier.Simplify` at **every** composition level → expression re-traversed O(D) times | simplify at boundaries |
| `EvidencePruner.Prune` | re-prunes already-pruned sub-trees at each level | prune once |

None of these is fatal on a 30-line method; together they are why a
large file feels heavy on the 250 ms debounce path.

**Fixed** except the last two rows. The seven walkers are now one
shared `OperationTree`; the increment scan, loop shapes, pattern loop
facts, and recurrence classifiers each walk once. `Simplify` and
`EvidencePruner` placement was left alone deliberately — it is the one
change that could alter output, and the benchmark on this machine is
too noisy to show whether it would help (PLAN "Why 2.6 is deferred").

### 3.4 [S2] Measured cost of a keystroke pass

Measured, not assumed — `AnalyzerBenchmarkTests` plus a throwaway
phase breakdown, .NET 10 Release, warm (best of 3).

**Read these as indicative, not precise.** Three identical runs of the
same fixture later produced 116 ms, 139 ms, and 238 ms on this
machine. The shape of the split (bind vs walk) is robust; individual
millisecond figures are not, and no optimization should be justified
by them alone.

| Fixture | Lines | Functions | Warm full pass | Per function |
|---|---|---|---|---|
| `RoslynComplexityEdgeCases.cs` | 734 | 49 | **199 ms** | 4.1 ms |
| `OptimalSolutions.cs` | 500 | 25 | **157 ms** | 6.3 ms |
| `RoslynSpaceComplexityPatterns.cs` | 609 | 25 | **105 ms** | 4.2 ms |

Phase split on the 734-line fixture (43 methods walked):

| Phase | Cost | Share |
|---|---|---|
| Parse + compilation + `GetSemanticModel` | 4–5 ms | ~3% |
| `BindWarnings` → `model.GetDiagnostics()` | 56–74 ms | **~35%** |
| Per-method `IOperation` walk | 100–138 ms | **~60%** |

Two corrections to what this audit assumed before measuring:

1. **`GetDiagnostics()` is expensive but not dominant.** It forces a
   full bind of every method body plus nullable/definite-assignment
   analysis Ohno never reads, to extract CS0246/CS0234 only — a third of
   every keystroke pass on the adversarial fixture, but only 1–3 ms on a
   clean 500-line file. It is worth caching (PLAN 2.7); it is not the
   headline.
2. **Selection analysis is cheap, not expensive.** It measures **4 ms
   against a 256 ms full pass** on the same file, because
   `AnalyzeSelection` walks one fragment rather than every method. The
   v0.1.2 selection feature did *not* create a hot path. The §3.5
   cancellation collision is a real defect on its own terms — it just
   is not a cost problem.

The dominant cost is the per-method walk, which is what §3.3 is about,
and 4–6 ms/function is the number Phase 2 has to move.

### 3.5 [S2] Selection and document analyses cancel each other — **fixed**

`AnalyzerService.LinkFastCancel` (`AnalyzerService.cs:238`) keeps **one**
`_fastCts` for the whole Fast tier. Selection analysis uses
`ohno/analyze` at tier `fast` too, so:

- `AnnotationController.refresh()` schedules the selection pass at
  ≤200 ms and the document pass at `debounceMs` (250 ms default);
- the document request lands second and **cancels the in-flight
  selection request**, so after any edit with an active selection the
  selection result is silently dropped;
- conversely, dragging a selection during a document pass kills the
  inline annotations mid-flight.

The client-side ticketing (`selectionVersion` / `version`) is correct;
the server-side single-slot cancellation is what collides. Cancellation
needs to be keyed by request kind.

**Fixed in this branch (PLAN 3.1).** `CancelSlot` holds one in-flight
request per kind. Verified against the bug: restoring the shared slot
makes the new test fail.

### 3.6 [S2] No cancellation or depth guard inside the method walker — **fixed**

`CSharpMethodAnalyzer` takes no `CancellationToken`. `CSharpFileAnalyzer`
checks the token *between* methods (`:54`), so a single pathological
method runs to completion even after the user typed again and the
request was cancelled — burning a core on a stale result.

`AnalysisState.MaxDepth = 8` bounds *interprocedural* depth only. The
`IOperation` recursion in `Analyze` (and the 7 recursive walkers) has
**no depth guard**: a machine-generated file with a deeply left-nested
expression or a giant initializer can overflow the stack and take the
server process down. The client recovers by respawning, but the
workspace/solution binding is lost.

### 3.7 [S2] `.slnx` is invisible to the extension

Roslyn 5.x `MSBuildWorkspace.OpenSolutionAsync` supports `.slnx`, and
`DeepWorkspace.OpenAfterLocatorAsync` (`:106`) routes anything not
ending in `.sln` to `OpenProjectAsync` — which will fail on a `.slnx`.
Upstream of that, the extension never even offers one:

- `extension.ts:120` — `findFiles('**/*.sln', …)`
- `solutionContext.ts:36` — `firstWithExt(dir, '.sln')`
- `solutionContext.ts:62` — `isSln()` tests `.sln` only

**This repository's own solution is `src/analyzer/Ohno.Complexity.slnx`.**
Ohno cannot do project-backed analysis of itself. Now that the analyzer
builds on the .NET 10 SDK (§1.1), the server half is ready and only the
extension-side discovery is missing — PLAN 4.1.

### 3.8 [S2] Whole classes of members are never annotated

`CSharpFileAnalyzer.TryGetMethod` (`:103-127`) matches only
`MethodDeclarationSyntax` and `ConstructorDeclarationSyntax`. So
property/indexer accessors, user-defined operators, conversion
operators, destructors, event accessors, and local functions get **no
inline annotation and no panel entry** — even though:

- the wire contract advertises `'property' | 'operator' | 'localFunction' | 'lambda'`
  (`protocol.ts:19-25`),
- `AnalyzerService.KindOf` maps `UserDefinedOperator` and
  `AnonymousFunction` (`:314-320`) — dead branches, nothing can reach them,
- `complexityModel.kindIcon` has icons for `property` and `operator`,
- `SelectionFragment.EnclosingMethod` (`:74`) *does* resolve
  `LocalFunctionStatementSyntax`, so selection analysis inside a local
  function works while the local function itself is never listed,
- `EdgeCaseTortureTests` asserts these bodies are *walked* when called —
  the cost model is right, only the surfacing is missing.

An expensive property getter is the single most common place a hidden
O(n) hides in C#. Not reporting it is a coverage gap, not a design
choice — nothing in the docs claims accessors are out of scope. (Local
functions *are* documented as deliberately not top-level results;
that one is intentional — see §4.)

### 3.9 [S3] Smaller items

| Item | Where | Note |
|---|---|---|
| Vestigial placeholder assignment | `CostComposer.cs:46-49` | `branch` assigned `Cx.Add(Cx.One)` then immediately overwritten in both arms |
| Duplicate `TryGetBody` | `CSharpMethodAnalyzer.cs:41-44` | body resolved twice per method (cheap, but noise) |
| `MergeUntil` uses `ChildOperations.Count()` | `CSharpMethodAnalyzer.cs` | full enumeration for a `> 64` test |
| Blanket branch warning | `CostComposer.cs:93` | *"Worst-case analysis used for branches."* is attached to nearly every method; it is a definition, not a caveat, and dilutes "Why this is an estimate" |
| `projectNear` sync FS walk | `solutionContext.ts:30` | `readdirSync` up the tree on the analyze path (short-circuited once a `.sln` binds) |
| Unwired scripts | `scripts/generate-fixtures.mjs`, `scripts/probe-rpc.mjs` | no npm script references them |
| Shipped-but-unreachable TS analyzer | `analysis/typescriptAnalyzer.ts` (382 lines) | not in `BUILTIN_LANGUAGES`; README says TypeScript is not selectable |
| Commented-out hover wiring | `extension.ts:42-45`, `annotationController.ts:25` | documented as intentional; the commented code is the smell, not the decision |

---

## 4. Intentional patterns — confirmed, do not "fix"

Verified against `docs/DEVELOPER.md` and the test suite. Each of these
looks like a bug at a glance and is not:

1. **`Cx.Var("n")` fallbacks** in `SizeResolver` — a documented guess,
   deliberately not an error.
2. **Hard-coded `"n"` / `"k"` in `Monomial.LogProductDominatesLogProduct`**
   — the k-way-merge absorption (`MergeKLists` → `O(n log k)`);
   documented, and locked by `LeetCodeBenchTests`.
3. **`ILocalFunctionOperation` declarations cost nothing** — cost is paid
   at the call; otherwise subset/permutation bodies double-count.
4. **`CostComposer.Loop` multiplies time but not space** — the peak-vs-retained
   rule, the core of `SpacePatternTests`.
5. **`MaxExpr` = simplified sum** — an upper bound on the worst branch;
   `n + m` for incomparable arms is deliberate.
6. **Soft vs hard opacity** (`PatternRefiner.IsSoft` vs
   `PatternApplicator.IsOpaque`) — new in v0.1.2 and fully documented:
   an incidental `await`/`IQueryable` next to a resolved loop annotates
   rather than wipes; `await foreach`/dynamic/regex still wipe.
7. **`ohno.analysis.tier` is inert** — deprecated in `package.json`,
   documented as reserved; automatic analysis is always Fast.
8. **Deep must never be tighter than Fast** — enforced by
   `EdgeCaseTortureTests`.
9. **Unknown gets a fixed sentence** — `ExplanationFormatter.UnknownText`.

The plan touches none of these except where a change is provably an
improvement *and* the existing assertions stay green.

---

## 5. Sources

- [NuGet — Microsoft.CodeAnalysis.CSharp 5.6.0](https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp/5.6.0)
- [OperationExtensions.Descendants](https://learn.microsoft.com/dotnet/api/microsoft.codeanalysis.operations.operationextensions.descendants)
- [OperationWalker](https://learn.microsoft.com/dotnet/api/microsoft.codeanalysis.operations.operationwalker)
- [Roslyn PR #77326 — SLNX support in MSBuildWorkspace](https://github.com/dotnet/roslyn/pull/77326)
- [Roslyn — Analyzer Actions Semantics](https://github.com/dotnet/roslyn/blob/main/docs/analyzers/Analyzer%20Actions%20Semantics.md)
- [roslyn-analyzers — Writing dataflow analysis based analyzers](https://github.com/dotnet/roslyn-analyzers/blob/main/docs/Writing%20dataflow%20analysis%20based%20analyzers.md)
- [NuGet — Microsoft.CodeAnalysis.AnalyzerUtilities 5.6.0](https://www.nuget.org/packages/microsoft.codeanalysis.analyzerutilities/)
- [C# 14 extension members — feature specification](https://learn.microsoft.com/dotnet/csharp/language-reference/proposals/csharp-14.0/extensions)
- [Regular expression improvements in .NET 7 (NonBacktracking)](https://devblogs.microsoft.com/dotnet/regular-expression-improvements-in-dotnet-7/)
- [Best practices for regular expressions in .NET](https://learn.microsoft.com/dotnet/standard/base-types/best-practices-regex)
- [BigO(Bench) — Can LLMs Generate Code with Controlled Time and Space Complexity?](https://arxiv.org/pdf/2503.15242) · [code](https://github.com/facebookresearch/BigOBench)
- [CodeComplex: Dataset for Worst-Case Time Complexity Prediction](https://arxiv.org/abs/2401.08719) · [EMNLP 2025 Findings](https://aclanthology.org/2025.findings-emnlp.1069.pdf)
- [TASTY: A Transformer based Approach to Space and Time complexity](https://arxiv.org/pdf/2305.05379)
- [KoAT: Automatic Complexity and Termination Analysis of Integer Programs](https://arxiv.org/html/2606.28542v1)
- [Automatic Complexity Analysis of Integer Programs via Triangular Weakly Non-Linear Loops (KoAT2/CoFloCo/Loopus comparison)](https://arxiv.org/pdf/2205.08869)
- [Upper and Lower Amortized Cost Bounds of Programs Expressed as Cost Relations (CoFloCo)](http://cofloco.se.informatik.tu-darmstadt.de/experiments/)
- [CA1502: Avoid excessive complexity](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1502)
