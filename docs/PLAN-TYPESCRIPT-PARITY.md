# Plan — TypeScript / JavaScript parity with C#

Companion to [PLAN-TYPESCRIPT.md](PLAN-TYPESCRIPT.md) (v1, **locked**),
[RESEARCH-2026-08.md](RESEARCH-2026-08.md), and
[DEVELOPER.md](DEVELOPER.md) §6–7. Written 2026-08-16 after v1
Phases 0–5 and the adversarial fixture pass.

v1 shipped a real `Program` + `TypeChecker` walker and closed the
honesty-violating stub. This document is the **next** plan: take every
special case and gap the Roslyn frontend already accounts for, research
the TypeScript compiler API and JS static-analysis literature, and
close the capability gap **without pretending JavaScript is C#**.

C# Core stays unchanged. Algebra stays shared only through
`src/shared/algebra-vectors.json`. Phases 6–13 are implemented;
TypeScript and JavaScript default **on**.

## Decisions that stay locked

| Rule | Why it still holds |
|---|---|
| `ohno.languages.typescript` / `javascript` default on | Phase 13 gate passed; untyped JS stays `C(name)` / Unknown |
| Node worker + `ts.createProgram` / `getTypeChecker()` | Editor language service is the wrong API |
| No Roslyn process for a TS-only workspace | Product promise |
| No C# Core edits for TS work | Dual-runtime algebra via goldens only |
| Honesty: O(1) only from catalog or constant-primitive allowlist | SessionLedger / RESEARCH S1 |
| Catalog not versioned by `target` | Same as BCL |
| **No public CFG** — do not poke `node.flowNode` | TS PR [#58036](https://github.com/microsoft/TypeScript/pull/58036) removed `FlowNode` / `FlowFlags` from the public API; they are `@internal`. ts-morph does not expose them. A product feature on internals will break on the next `typescript` patch |
| Stay on the **JavaScript** `typescript` package | TypeScript 7 (Go) ships **no** stable programmatic API. Microsoft’s 7.0 announcement: keep `@typescript/typescript6` / the JS package for tools until 7.1. Do not migrate the worker to `tsgo` |
| Checker types are **evidence, not proof** | TypeScript is intentionally unsound (annotations, `any`, excess property, unchecked index). Safe TypeScript / Flow papers exist because of this |

v1 “Later” items that this plan **does not** pull forward: Angular
templates, Vue/Svelte SFCs, Prisma/Knex as a first-class queryable
(still opaque `C(name)`), Open VSX copy. Phase 13 default-on is
done.

## 1. What C# already accounts for

The Roslyn frontend is the spec. Every item below is either a
detector, a cardinality fact, or a fixture vocabulary entry. The TS
column is the current worker as of the 2026-08-16 fixture pass.

### 1.1 PatternRecognizer IDs

C# source: `PatternRecognizer.cs`. TS source:
`patterns/recognize.ts`. Effects: **Unknown** wipes a constant,
**Annotate** keeps the structural bound, **Range** is best/worst.

| C# id | C# effect | TS analog | Status |
|---|---|---|---|
| `dynamic-dispatch` | Unknown | `any` / `unknown` call | Partial — folded into `interface-dispatch` on `any` receiver. Missing: computed `obj[k]()`, `this` rebound, `new Proxy` |
| `reflection-dispatch` | Unknown | `Reflect.apply` / `obj[name]()` | Missing as a named pattern. Calls already `C(name)` if unresolved |
| `interface-dispatch` | Unknown | interface / abstract / `any` | Partial — `any`/`unknown` only. Typed interface methods that resolve to a declaration are walked; that is **stricter** than C# (C# refuses all interface calls). Keep C#’s conservatism for *unresolved* interfaces; walking a same-file impl is fine |
| `delegate-invoke` | Unknown | callback param, unbound function value | Partial — `C(transform)` when the callee is a parameter. Missing: multicast analog (`listeners.forEach(fn)`), `fn.call` / `fn.apply` / `fn.bind` |
| `regex` | Unknown | `RegExp` / `String#match` | Done. JS has no `NonBacktracking`; default is backtracking |
| `regex-linear` | Annotate | trivial literal | Done for trivial literals. No DFA engine to detect |
| `stream-io` | Unknown | `fs`, `Readable`, `fetch` body | Missing. Node streams and `Blob`/`Response` stay `C(name)` only if unresolved |
| `parallel-loop` | Unknown | `Promise.all`, `worker_threads`, `Atomics` | Missing as a pattern. `Promise.all` is not cataloged |
| `expression-compile` | Unknown | `new Function`, `eval` | Honesty done (`C(eval)` / `C(Function)`). No named pattern |
| `thread-block` | Unknown | `Atomics.wait`, `SharedArrayBuffer` | Missing. Rare in typical editor files |
| `queryable` | Unknown | Prisma / Knex / TypeORM / Drizzle | Still deferred — opaque call, do not invent SQL |
| `await-opaque` | Annotate (soft) | `await` | Done |
| `await-foreach` | Unknown | `for await` | Done |
| `deferred-linq` | Annotate | lazy iterators / generator pipelines | Missing. `arr.map.filter` is eager in JS (good). `iterable` helpers that return iterators are not named |
| `lock-wait` | Annotate | `Atomics` / mutex libs | Missing. No `lock` statement |
| `iterator-yield` | Annotate | `function*` / `yield` | Missing. Generators are walked as ordinary bodies today |
| `string-concat-loop` | Annotate | `s += x` / template grow | Done |
| `unproven-loop` | Unknown | Collatz / `n = 3n+1` | Done |
| `null-terminated-walk` | Annotate | `while (p) p = p.next` | Done (shape + worklist) |
| `numeric-countdown` | Annotate | `while (n > 0) n--` | Done |
| `cache-history` | Range | `Map.has` / `Map.get` miss path | Missing. C# fires on `Dictionary.TryGetValue` |
| `unbounded-worklist` | Unknown | queue refill, no visit | Done (incl. index-scan queues) |
| `data-dependent-recursion` | Range | uneven recursive arms | Partial — recurrence ids exist; C#’s conditional-arm count is richer |
| `graph-traversal` | Annotate | visited / successor worklist | Done (TS-only id; C# folds this into worklist bounds) |

### 1.2 Cardinality, sizes, and CFG

C# `CardinalityAnalyzer` + `AnalysisState` is the largest remaining
gap. It is a **companion pass**, not the cost walk:

| C# fact | What it does | TS today |
|---|---|---|
| `ControlFlowGraph.Create` → `UnreachableSyntax` | Skip dead enqueue / dead alloc (`UnreachableEnqueue`) | **No public CFG.** Fast-path `if (false)` / after-`return` can be syntax-only. Do not use `@internal` flow nodes |
| `LoopIndices` | Incremented integrals are **not** emitted as dimensions (`LoopIndexNotEmitted`) | Missing — a `for (let i = 0; i < n; i++)` can still leak `i` as a dim if a later use looks numeric |
| `Cardinality` (seed / current / max) | SizeDelta on cataloged grow/shrink/clear/replace | Missing as a structure. Space uses `allocs` + `noteGrow` (push/add/set) and a heap cap (`length > k` + `shift`). No seed vs current vs peak algebra |
| `HeapBounds` | `if (q.Count > k) Dequeue` → space `k` | Partial — same shape on arrays. No `PriorityQueue<T>` in JS; fixture `MinHeap` is cataloged log |
| `WorklistBounds` | refill + visit / successor / net-decrease / graph (visit+edges, flatten adj) | Partial — loop-shape + facts, not a published per-symbol bound map |
| `FlattenedAdj` / `EdgeCounts` | `for (var v in adj[u])` inside a graph loop is **add**, not multiply | Partial — `containsGraphWhile` + flatten `for-of adj[u]` |
| `ElementSizes` | jagged / nested element cost | Missing |
| `UnboundedHeaps` | grow-only heap → unknown space | Missing as a named fact |
| `LinearRegexes` | `RegexOptions.NonBacktracking` | N/A in JS |
| `Sizes` map | `.Length` / param / last assignment | Partial — `.length` and params; no last-assignment SizeDelta |
| Roslyn `AnalyzeDataFlow` | written-inside integrals for loop-index denylist | No public equivalent. Recreate from syntax (`++` / `+=` / `-=` on identifiers) |

C# cardinality fixtures that still have no TS twin:

| Fixture | Honest C# lesson | TS/JS shape |
|---|---|---|
| `Huffman` | net-decrease worklist; space tracks seed | two-queue / two-array merge of frequencies |
| `RunningMedian` | two heaps, each ≤ n | two `MinHeap`s or two sorted arrays |
| `WindowRemoveAt` / `WindowTryDequeue` | window space `k`, not `n` | `if (q.length > k) q.shift()` — heap detector exists; SizeDelta does not publish `k` as the collection’s max |
| `HeapifyFromEnumerable` | ctor replace-size | `new MinHeap(values)` / `Array.from` |
| `SortedSetInsert` | n log n expected | no `SortedSet`; `Set` is expected O(1) insert. A hand-rolled tree is `C(name)` |
| `StringBuilderJoin` | amortized linear build | `parts.push` + `join` (already cataloged) vs `s +=` (concat pattern) |
| `ImmutableListBuild` | each persist is O(n) or log | `arr.toSpliced` / spread-copy in a loop |
| `SpanScan` | no extra space | typed-array / `subarray` view |
| `CollectionSpread` | space n+m | `[...a, ...b]` — catalog `concat`/`slice` exists; spread-in-literal is not a SizeDelta |
| `HalvingShift` | `>>= 1` is log | done in `loopShapes` |
| `UnreachableEnqueue` | CFG reachability | syntax-only dead code, or honest over-count |
| `LoopIndexNotEmitted` | `i` is not a dimension | denylist pass |

### 1.3 Loop / recurrence / heap detectors

| C# detector | Status in TS |
|---|---|
| Comparison `for` / `while` (`i < n`) | Done |
| `*= 2` / `/= 2` / `>>= 1` as log | Done (v1 explicitly did not copy C#’s old `*= 2` miss) |
| Null-terminated `.next` | Done |
| Visited frontier | Done (`Set` or writes to `indeg`/`dist`/`visited`) |
| Index-scan queue (`qi < q.length` + `push`) | Done. JS `shift` is O(n) — fixtures use an index pointer |
| Binary search (`lo`/`hi` + mid `/ 2` or `>> 1`) | Done |
| Two-pointer (compared ids actually `++`/`--`) | Done |
| Linear / branching / D&C / memo / graph recurrence | Partial — slice merge-sort and fib/memo shapes exist; 1-D memo still not first-class (same as C#) |
| Heap cap from `Count > k` + shrink | Partial |

### 1.4 Space patterns (C# `RoslynSpaceComplexity*`)

| C# pattern | TS today |
|---|---|
| Constant / linear / two independent arrays | Partial — `new Array(n)` sizes; two allocs should peak-sum |
| Rectangular / square / cubic | **Missing** — `dp[i][j] = 0` does not allocate a 2-D table. CommonChild / Knapsack time can be `O(m n)` while space stays `O(1)` |
| Repeated-but-not-retained vs retained | Missing (needs seed/current/max) |
| Top-K heap space `k` | Partial |
| Sliding window space `k` | Partial |
| Unique-set space `n` | Partial (`noteGrow` on `add`) |
| Adjacency list / matrix | Partial list; matrix missing |
| Recursion stack (log / n / n for fib) | Partial |
| 2-D memo table | Missing |
| Subsets / perms / combinations retained | Missing |
| BFS/DFS extra space | Partial |

### 1.5 Edge-case vocabulary (C# `RoslynComplexityEdgeCases`)

These are **honesty classes**, not tighter math. TS must keep the
same vocabulary in comments and patterns:

| Class | C# examples | JS/TS extra hazard |
|---|---|---|
| INCONCLUSIVE | `dynamic`, reflection, `IQueryable`, `Expression.Compile` | `eval`, `new Function`, `Proxy`, computed keys, `with`, `arguments`, `this` rebound, prototype mutation |
| CONTEXT_DEPENDENT | unseeded field queue, interface impl | closure that mutates a captured array; module-level cache |
| RANGE | cache hit/miss, data-dependent recursion, iterator consumption | generator `yield`, `Map.get` miss, thenable that is not a Promise |
| DERIVABLE_WITH_SUMMARIES | custom `IEnumerable`, user `get_Item` | user `get`/`set` traps, JSDoc-less `any[]` |
| NON_TERMINATION_RISK | BFS no visit, Collatz | same, plus `for (;;)` / `while (true)` without a proven exit |

`SessionLedger.cs` stays **comment-only**. Do not assert production-
shaped comments as goldens. Dedicated fixtures only.

### 1.6 Catalog polarity (RESEARCH S1)

C# no longer treats uncataloged `System.*` as O(1). TS must not grow
a “well-known global ⇒ O(1)” list. `Math.*` is cataloged per member.
`JSON.*`, `Object.keys/values/entries`, `structuredClone` (not yet
cataloged) are O(n). An uncataloged `lib.es*.d.ts` method is
`C(name)` at Low — never O(1) because it lives in `lib`.

## 2. Why JavaScript is a harder problem

C# analysis rests on a closed method body, a resolved `IMethodSymbol`,
and (when needed) a CFG. JavaScript adds a second, **dynamic** layer
that the literature treats as a different problem — not a missing
`for`-loop shape.

### 2.1 Language facts the walker cannot wish away

| Fact | Why a C# port breaks | Honest Ohno move |
|---|---|---|
| Closures capture **mutable** bindings | A nested arrow can `push` on an outer array; the grow is not in the callee’s parameter list | Track captured identifiers that are collections; if a nested function writes them, charge the **caller’s** SizeDelta or `C(mutate)` |
| `this` / prototype / `call`/`apply`/`bind` | The receiver at the call site is not the method’s declared type | If `this` is untyped or re-bound, do not catalog `this.items.sort` as `Array.sort` |
| Computed keys `obj[p]` | Property names are data. Jelly (PACMPL 2024) names `x[p]` as the main unsoundness source in approximate interpretation | Never O(1). Array index of a proven `T[]` is O(1); else `C(get)` |
| `any` / JSDoc-less JS | The checker will happily type a parameter as `any` | Already: `C(iterate)` / `C(name)`, not invented `n` |
| First-class functions | The call graph is a points-to problem (Feldthaus field-based CG; ArkTS APAK 2026: closures + framework APIs break CHA/RTA) | Walk same-file resolved signatures only (already). Cross-file when `Program` is ready. Parameter callbacks stay `C(fn)` |
| `eval` / `new Function` / `Proxy` | Body is data | Keep `C(eval)` / `C(Function)` / `C(Proxy)` |
| Generators / async iterators | Cost is paid by the consumer (`iterator-yield`, `await-foreach`) | Port the C# annotate/unknown split |
| Prototype mutation / `Object.create` | Methods can appear after the class literal | Catalog only checker-resolved well-known symbols from `lib.es*.d.ts` |
| `arguments` / rest / spread | Arity and copies are not in the signature | Rest is a sized array when typed; `arguments` is `IArguments` → Array catalog for index, not for `.map` unless typed |
| Thenables | `await x` is not necessarily `Promise` | Soft `await-opaque` stays; do not assume `Promise.all` is n × O(1) |

### 2.2 What the research says — and what we will not build

Ohno is an **IDE-latency** deriver (local bound → composition →
amortization). RESEARCH-2026-08 already rejected SMT solvers and
ML classifiers as the engine. The JS literature is useful for
**honesty layering**, not as a new backend.

| Work | Use for Ohno | Do not do |
|---|---|---|
| [Using the Compiler API](https://github.com/microsoft/TypeScript/wiki/Using-the-Compiler-API) (Microsoft wiki) | `createProgram`, `getTypeChecker`, `getSymbolAtLocation`, `getTypeAtLocation`, `getResolvedSignature`, `oldProgram` incremental | Language service as the analysis engine |
| Program / TypeChecker docs (microsoft-typescript.mintlify.app) | Same public surface the worker already uses | Invent a CFG from checker internals |
| TS PR [#58036](https://github.com/microsoft/TypeScript/pull/58036) | Confirms flow nodes are gone from the public API | `// @ts-expect-error` on `node.flowNode` |
| [Announcing TypeScript 7.0](https://devblogs.microsoft.com/typescript/announcing-typescript-7-0/) | Side-by-side `@typescript/typescript6` until 7.1’s **new** API | Migrate the worker to `tsgo` in this plan |
| typescript-eslint typed linting / `parserOptions.projectService` | Deep-mode pattern: bind a long-lived `Program` to the nearest `tsconfig`, reuse it, do not `getPreEmitDiagnostics` on every keystroke | Copy their ESTree layer — we already walk `ts.Node` |
| Oxlint / `tsgolint` on typescript-go | Watch 7.1 API shape | Depend on it before it is stable |
| COSTA/PUBS, SPEED, Loopus, KoAT2, CoFloCo, RAML (RESEARCH §2.1) | Keep the three-layer discipline; add a cheap syntactic ranking (decrement + lower bound) **without** SMT | Port an integer transition system |
| TAJS (Jensen et al.); Value Partitioning (Møller, ECOOP 2020) | Closure free-variable precision is the hard bit; partition by captured bindings, not by a full heap | Clone TAJS |
| Approximate Interpretation / Jelly (PACMPL 2024) | Dynamic `x[p]` is the unsoundness source; over-approx kills scale; ignore-unknown kills recall | Whole-program points-to in the debounce path |
| Type inference for AOT JS (Samsung/Oracle) | TS/Flow unsound for compilation; prototype + first-class methods | Treat annotations as sound |
| ArkTS pointer analysis (APAK 2026) | Closures + framework APIs break CHA/RTA | Class-hierarchy call graphs for `any` |
| Feldthaus field-based call graphs; SAFE; Jalangi | Field-based CG is the cheap static approximation; Jalangi is **dynamic** | Dynamic instrumentation in the extension |
| BigO(Bench) / CodeComplex / TASTY | Validation corpus later, not the engine | Train a classifier |

**Implication:** do not add a JS points-to / TAJS clone. Steal the
*layering* already in RESEARCH-2026-08, plus an explicit **dynamic-JS
honesty layer** (`any`, computed keys, mutating closures, `eval` /
`Proxy`, `this` rebinding, generators, thenables).

## 3. Compiler API — examples to build from

Stay on the public surface. Concrete recipes:

### 3.1 Incremental `Program` (already v1; keep as the host)

Microsoft wiki: `createProgram(rootNames, options, host, oldProgram)`.
Deep mode copies typescript-eslint’s **projectService** idea: one
`Program` per `tsconfig`/`jsconfig`, reused across edits. Fast mode
stays ad-hoc + `checkJs: true` for JSDoc. Do not run
`getPreEmitDiagnostics` on the project on every keystroke.

### 3.2 Signature and symbol identity (call graph without points-to)

```
const sig = checker.getResolvedSignature(call);
const decl = sig?.declaration;          // walk if FunctionLike + body
const sym = checker.getSymbolAtLocation(callee);
const type = checker.getTypeAtLocation(receiver);
```

C# uses `IMethodSymbol` + `SymbolEqualityComparer`. TS equivalent
for “same function” is `checker.getSymbolAtLocation` (or the
signature’s declaration node), **not** name strings. Local walk
today refuses a declaration in another `SourceFile`. Phase 11
relaxes that to **same `Program`**, still capped (C#
`AnalysisState.MaxDepth = 8`).

### 3.3 Well-known lib members (catalog bind)

`symbol.getDeclarations()` → source file path contains `lib.es` or
`lib.dom`, **or** `TypeFlags` / `symbol.escapedName` is `Array` /
`Map` / `Set` / `String` / `Promise` / `RegExp` / a typed-array
name. This is the S1 polarity: lib membership is necessary, not
sufficient. The member still needs a catalog row.

### 3.4 What we will not use

| API | Reason |
|---|---|
| `ts.createLanguageService` | Completions / diagnostics. Wrong granularity for a whole-function cost walk |
| `node.flowNode` / `FlowFlags` | Removed from public API |
| `ts-morph` | Extra dependency; wraps the same public API; no CFG gift |
| typescript-go / `tsgolint` | No stable programmatic API in 7.0 |
| Roslyn `ControlFlowGraph` via a hidden C# hop | Do not send a TS AST through the Roslyn process |

A **syntax-only** reachability pass is allowed: `if (false)`,
`if (true) return; …`, statements after an unconditional `return` /
`throw` in the same block. That covers `UnreachableEnqueue` enough
to be honest. It is not a CFG.

## 4. Target architecture (additive)

No new process. Extend the worker in place:

```
src/extension/src/analysis/typescript/
  walk/
    body.ts          // cost walk (exists)
    sizes.ts         // dims + .length (exists)
    loopShapes.ts    // bound shapes (exists)
    cardinality.ts   // NEW — seed/current/max, loop-index denylist
    reachability.ts  // NEW — syntax-only dead statements
    captures.ts      // NEW — closure writes to outer collections
  patterns/
    recognize.ts     // remaining PatternRecognizer ids
  catalog/
    builtins.ts      // Promise, iterators, TypedArray, structuredClone
```

`AnalysisState` analog (keep it small; C#’s state is the checklist):

| Field | Phase |
|---|---|
| `loopIndices: Set<ts.Symbol>` | 6 |
| `cards: Map<ts.Symbol, { seed, current, max }>` | 6 |
| `unreachable: Set<ts.Node>` | 6 |
| `heaps` (already on `SizeState`) | extend in 6 |
| `captures: Map<ts.Symbol, 'read' \| 'grow' \| 'mutate'>` | 8 |
| `analyzing` / `cache` / depth cap | 11 (C# already has this; TS walks one file) |

Symbols from `checker.getSymbolAtLocation`. If the checker returns
undefined (untyped JS), fall back to a **same-function identifier
string** and drop the fact at the function boundary. Never invent a
global `n` from a free variable’s name.

## 5. Phased delivery

One phase per commit, independently revertable. C# tests stay green.
Existing TS comment-harness expectations stay green unless the same
commit changes a fixture **toward honesty** (tighter only with a
proof; otherwise looser).

### Phase 6 — Cardinality without a CFG

**Done 2026-08-16.** Highest C# gap. Port the *ideas* of `CardinalityAnalyzer`, not
`ControlFlowGraph`.

| Step | Work |
|---|---|
| 6.1 | `loopIndices`: any identifier that is `++`/`--`/`+=`/`-=` in a `for` incrementor or loop body, and whose checker type is number-like, is **not** a dimension |
| 6.2 | `Cardinality` on locals assigned `[]` / `new Map` / `new Set` / `new Array(n)` / `Array.from` (seed). `push`/`add`/`set` increment current and max by the enclosing loop bound. `pop`/`delete` decrement current only. `length = 0` / `clear` reset current |
| 6.3 | Publish max as the collection’s space when the walk asks for it (window `k`, Huffman seed, `[...a, ...b]`) |
| 6.4 | Syntax reachability: skip statements in an `if (false)` arm and in a block after an unconditional `return`/`throw`. Over-count if unsure |
| 6.5 | Fixtures: `samples/typescript/TsCardinality.ts` mirroring `RoslynCardinalityGaps.cs` (Huffman, window, spread, unreachable, loop-index). JS twins only where JSDoc can state the types |

**Out of 6:** full aliasing (`a = b; a.push` grows `b`). If the
checker says the symbols are the same binding, share the card; if
not, do not merge. That is the Jelly lesson at IDE budget.

### Phase 7 — Pattern parity

**Done 2026-08-16.** Add the remaining IDs that have a **JS shape**. Soft vs hard follows
C# (`PatternRefiner` / `apply.ts`).

| ID | JS trigger | Effect |
|---|---|---|
| `reflection-dispatch` | `Reflect.apply` / `Reflect.construct` / `obj[name](...)` where `name` is not a string literal | Unknown |
| `delegate-invoke` | call of a parameter or a value typed as a function type we do not walk | Unknown (keep `C(fn)` in the algebra) |
| `expression-compile` | `eval(...)` / `new Function(...)` | Unknown (today’s `C(name)` plus the pattern) |
| `iterator-yield` | `yield` / `yield*` | Annotate |
| `deferred-linq` | return of a generator or a method named `values`/`keys`/`entries`/`[Symbol.iterator]` without consumption | Annotate |
| `cache-history` | `map.has` / `map.get` then a compute+`set` in the miss branch | Range |
| `stream-io` | checker type from `node:fs` / `node:stream` / `ReadableStream` | Unknown |
| `parallel-loop` | `Promise.all` / `Promise.allSettled` / `worker_threads` | Unknown unless the element factory is a walked local of known bound — then annotate, do not claim wall-clock |
| `dynamic-dispatch` | `any`/`unknown` callee **or** `Proxy` | Unknown (split from today’s `interface-dispatch` so the panel reason is accurate) |

Still **not** first-class: Prisma/Knex/TypeORM (`queryable` stays
opaque). `lock-wait` / `thread-block` only if we see `Atomics.wait`
or a cataloged mutex; otherwise ignore.

### Phase 8 — Dynamic JS honesty (closures, `this`, keys)

**Done 2026-08-16.** This is the phase C# does not have an analog for. It is the reason
parity is “as close as possible,” not “identical.”

| Step | Work |
|---|---|
| 8.1 | Capture analysis **inside one function**: nested `function` / arrow; free identifiers that resolve to an outer collection; classify read vs `push`/`add`/`set` vs other writes |
| 8.2 | If a nested function that **grows** a capture is stored (`arr.push(fn)`, `return fn`, `el.addEventListener`) and not immediately invoked, the grow is **not** charged to this call — annotate `CONTEXT_DEPENDENT` / `C(mutate)` |
| 8.3 | If it **is** immediately invoked (`fn()`, `map(x => …)`), charge SizeDelta on the capture (this is the common `forEach` / `map` case and must stay tight) |
| 8.4 | `this.x` / `proto.x`: catalog only when the checker’s apparent type is a well-known lib type. `Function.prototype.call/apply/bind` → `delegate-invoke` |
| 8.5 | Computed `obj[p]`: O(1) only for proven array/string/typed-array index. Else `C(get)` / `C(set)` |
| 8.6 | Fixtures: `samples/javascript/JsClosures.js`, `samples/typescript/TsThis.ts` — mutating capture, rebound `this`, computed key, `Proxy`, stored callback |

Do not implement TAJS-style value partitioning. One function, one
binding table, then stop.

### Phase 9 — Catalog depth

**Done 2026-08-16.** Match C# `BclCatalogTests` tightness for **lib.es** / common Node
globals. One modern catalog; missing `target` APIs simply do not
resolve.

| Area | Entries | Notes |
|---|---|---|
| `Promise` | `then`/`catch`/`finally` soft; `all`/`allSettled`/`race` as n × element or Unknown | Do not claim High O(n) if the factory is opaque |
| `structuredClone` | O(n) time and space | v1 listed it; not in `builtins.ts` yet |
| TypedArray methods | treat as `Array` for scan/sort/fill; `subarray`/`slice` views are O(1) extra space | `set` copy is O(n) |
| Iterators | `Array.from` already; `Iterator.from`, `map`/`filter` on iterators — loop × callback or `C(iterate)` if untyped | |
| `Map`/`Set` iterators | `values`/`keys`/`entries`/`[Symbol.iterator]` are O(n) when **consumed**; creating the iterator is O(1) (deferred analog) | Fixes Group Anagrams `C(iterate)` on `Map.values()` when the `for-of` is in-function |
| `WeakMap`/`WeakSet` | expected O(1) get/has/set | Medium |
| `Object.assign` / spread object | O(n) | |
| `String#repeat` / `padStart` | O(n) | |
| `Array#findIndex` / `findLast` / `toSorted` (done) / `with` | scan or copy | |
| Node `Buffer` | treat as typed array when checker says `Buffer` | |
| DOM `NodeList` / `HTMLCollection` | O(n) iterate; **not** in v1 success — only if `lib.dom` is in the Program | |

`MinHeap` stays a **fixture convention**, not a language heap. Do
not invent a global `PriorityQueue` catalog unless the checker
resolves a known package we explicitly add (out of this plan).

### Phase 10 — Space parity

**Done 2026-08-16.** Depends on Phase 6 cards.

| Step | Work |
|---|---|
| 10.1 | 2-D / 3-D: `const dp: number[][] = …` / nested `new Array(m)` filled with `new Array(n)` → space `m n`. Also `dp[i][j] =` after `dp = Array.from({length: m}, () => Array(n).fill(0))` |
| 10.2 | Peak vs current: loop-local `const tmp = new Array(n)` that dies each iteration is O(n), not O(n²) (C# `RepeatedButNotRetained`) |
| 10.3 | Recursion stack: reuse recurrence id (log / n / n for branching) as aux space when no heap alloc dominates |
| 10.4 | Fixtures: `samples/typescript/TsSpace.ts` mirroring `RoslynSpaceComplexityPatterns.cs` |

CommonChild / Knapsack comments can then move from “space O(1)
because we cannot see the table” to `O(m n)` when the allocation is
visible.

### Phase 11 — Same-`Program` interprocedural walk

**Done 2026-08-16.** C# walks in-compilation bodies up to depth 8. TS `localBody` today
requires `decl.getSourceFile() === ctx.source`.

| Step | Work |
|---|---|
| 11.1 | Allow a resolved declaration in another file of the **same** `Program` when deep/ready. Fast/ad-hoc stays same-file |
| 11.2 | `analyzing` set + cache (C# `AnalysisState.Cache`) to cut recursion and mutual helpers |
| 11.3 | Getters / setters already same-file; extend to same-Program under the accessors setting |
| 11.4 | Unresolved import / `any` export stays `C(name)`. Do not follow `node_modules` bodies |

This is how C# gets `DERIVABLE_WITH_SUMMARIES` for user helpers
without a points-to analysis.

### Phase 12 — Cheap ranking (optional, after 6–8)

RESEARCH §2.1: a ranking function is “a variable that decreases and
is bounded below.” C# still infers many `while` bounds from
condition shape only.

| Allowed | Forbidden |
|---|---|
| Identifier in the condition, decremented (or halved) in the body, compared to a literal or a sized `.length` | SMT, KoAT, Loopus |
| Collatz / multiply-in-body stays `unproven-loop` | Using `@internal` back-edges |

**Done 2026-08-16.** Local ranking for `while (i < n) i++`,
`while (true) { if (i >= n) break; i++; }`,
`n = Math.floor(n / 2)`, `n >>= 1`, and `i *= 2`. Collatz stays
`unproven-loop`. No SMT.

### Phase 13 — Default-on gate (not a feature phase)

Flip `enabledByDefault` only when **all** of these hold:

1. `TsBclCatalog` + `TsHonesty` are as tight as `BclCatalogTests` +
   C# honesty fixtures: no silent O(1), no invented `n` on `any`.
2. `TsCardinality` + `TsSpace` match the C# cardinality/space
   fixtures **where types exist**.
3. Optimal / torture comment harness stays green; JS untyped cases
   stay `C(name)` / Unknown, not a weaker comment.
4. C# 325+ analyzer tests and the extension suite stay green.
5. A short README / marketplace note: typed TS ≈ C#; untyped JS is
   honest and looser.

**Done 2026-08-16.** Settings default **true**. Typed TS matches the
C# honesty bar on cataloged shapes; untyped JS stays `C(name)` /
Unknown. Users can still turn a language off.

## 6. Tests

Mirror C#. Do not assert `SessionLedger`. Use the existing comment
harness (`// expected: TIME / SPACE`).

| Suite | Role |
|---|---|
| `samples/typescript/TsCardinality.ts` | Phase 6 — Huffman, window k, spread, unreachable, loop-index |
| `samples/typescript/TsSpace.ts` | Phase 10 — 2-D table, peak vs current, recursion stack |
| `samples/typescript/TsPatternsMore.ts` | Phase 7 — cache-history, yield, Reflect, Promise.all |
| `samples/javascript/JsClosures.js` | Phase 8 — capture mutate, stored callback |
| `samples/typescript/TsThis.ts` | Phase 8 — `this`, `call`/`apply`, computed key, Proxy |
| Existing `TsOptimal` / `TsTorture` / `Js*` | Stay green; tighten comments only when the walker can prove it |
| `engine/parity.test.ts` | Unchanged goldens |
| C# suites | Untouched |

JS twins require JSDoc or a syntactic `[]` / `new Map` so the
checker has evidence. Untyped `for-of` over `any` must remain
`C(iterate)`.

## 7. Risks

| Risk | Mitigation |
|---|---|
| Pretending JS is C# | Phase 8 is mandatory before default-on. Closures and computed keys stay C(name) when unproven |
| Secret CFG via `flowNode` | Locked decision; code review reject |
| TypeScript 7 breaks `import ts` | Pin the JS `typescript` package; track 7.1 as a **separate** plan |
| Cardinality alias bugs (`a = b`) | Do not merge cards unless the checker symbol is the same |
| Catalog creep (`Math` today, `window` tomorrow) | Lib path or well-known `escapedName` + an explicit row. No “looks like a builtin” |
| Interprocedural latency | Depth 8, same-Program only, no `node_modules`, cache per symbol |
| Dual algebra drift | `algebra-vectors.json` stays the spec |

## 8. Success criteria

Parity means: **the same honesty classes and the same tight bounds
on textbook shapes when the checker can see types.** It does not
mean identical headlines on untyped JS.

- Typed `Array` / `Map` / `Set` / `String` / `Promise` / typed
  arrays: no `C(name)` for cataloged members; no O(1) on sorts or
  scans.
- Cardinality fixtures: loop index not a dim; window space `k`;
  spread space `n+m`; Huffman net-decrease; unreachable enqueue
  ignored **or** over-counted with Medium, never under-counted as
  O(1).
- 2-D DP space `O(m n)` when the table is allocated in view.
- `Map.values()` consumed in-function is O(n), not `C(iterate)`.
- Closures: immediate callback grow is charged; stored callback is
  `C(mutate)` / CONTEXT_DEPENDENT, not a fake `O(n)`.
- `any`, computed key, `eval`, `Proxy`, rebound `this`: Unknown or
  `C(name)`, never High O(1).
- C# tests green. No Roslyn spawn for TS-only. Default on after
  Phase 13.

## 9. Open (non-blocking)

- Whether `lib.dom` `NodeList` lands in Phase 9 or waits for a
  browser-fixture pack.
- Whether a syntactic `number[]` parameter in ad-hoc JSDoc-less JS
  sizes `n` (v1 left this open). Recommendation unchanged: syntactic
  `[]` / `new Array` catalogs; a bare name does not.
- TypeScript 7.1 API: start a **new** research note when Microsoft
  publishes it. This plan does not schedule a port.
- BigO(Bench) as a TS validation corpus: useful after Phase 13, not
  as an engine.

## 10. What this plan will not do

- SMT / ranking-function solvers (Loopus, KoAT, COSTA).
- Whole-program points-to (TAJS, Jelly, ArkTS).
- Dynamic instrumentation (Jalangi).
- ML class labels (BigO(Bench), CodeComplex) as the reported bound.
- Internal TypeScript flow analysis.
- typescript-go as the worker runtime before a stable API.
- Query-provider SQL bounds.
- Changing C# Core or starting Roslyn for TS.

## 11. Review pass (2026-08-16)

Post-Phase-13 findings that are **closed**: fast is ad-hoc and deep
is project; cancel / stale skip the cache; helper `bodyCache`
(second call is not fake recursion); `realpath` overlay; callee
`rangeOf`; program LRU; one prime visit; `BindKey` cards/heaps/
worklists; recurrence keeps walked space; default export and
object-literal methods; recognize dedupe by id+range; log bounds
use the real dimension; pointer advance is an AST check.

Left on purpose: the unsaved buffer is still cloned to the worker;
`findConfig` still walks `existsSync`; binary-search / two-pointer
detectors may still sniff a body with `getText()`.
