# Plan — TypeScript and JavaScript support

Companion to [DEVELOPER.md](DEVELOPER.md) §6 “Add a language” and
[RESEARCH-2026-08.md](RESEARCH-2026-08.md) (“shipped-but-unreachable
TS analyzer”). Decisions below are locked from the 2026-08-16
scoping pass.

## Decisions (locked)

| Question | Choice |
|---|---|
| Surface | `.ts`, `.tsx`, `.js`, `.jsx` (and `languageId` `typescript` / `javascript` / `typescriptreact` / `javascriptreact`) |
| Default | **On** after Phase 13 — `ohno.languages.typescript` / `javascript` default **true**. Untyped JS stays `C(name)` / Unknown |
| Engine | TypeScript in a **Node worker thread**, using `ts.createProgram` / `program.getTypeChecker()`. Algebra is **shared conceptually** with C# Core, not by sending TS through the Roslyn process |

Angular templates, Vue/Svelte SFCs, and `.mts`/`.cts` as first-class
ids are **out of v1**. `.mts`/`.cts` can ride along if `languageId` is
still `typescript`.

## Why this is not “turn on the stub”

`src/extension/src/analysis/typescriptAnalyzer.ts` was the honesty-
violating stub. It has been deleted; the worker facade replaced it.

It violates the honesty rule the C# engine spent 0.1.3–0.1.6 closing:

- Costs are **strings** (`"n"`, `"n log n"`), not `ComplexityExpression`.
- Unknown loops invent `n`.
- `has` / `get` / `set` are O(1) High by **name**, on any receiver.
- “Deep” is `createProgram` with `noResolve: true` and a host that
  only sees one file — not a project graph.
- No patterns, approaches, selection, accessors, or `C(name)` algebra.

RESEARCH already flagged it as shipped-but-unreachable. v1 **replaces**
it. The two tests are rewritten against the new engine.

## What we leverage unchanged

The product surface is already language-agnostic. C# stays on the
Roslyn server. TS/JS must emit the **same** `AnalyzeResponse` so
nothing in the UI forks.

| Piece | Reuse |
|---|---|
| `IComplexityAnalyzer` + `AnalyzerRegistry` | Register a `TypeScriptAnalyzer` facade that RPCs to the worker |
| `AnalyzeDocumentRequest` / `AnalyzeResponse` | Same wire shape (`src/shared/protocol.ts`) |
| Annotations, CodeLens, Complexity panel, selection, debounce, `maxFileSizeKb`, `annotations.mode` | No UI changes beyond language enablement |
| `ComplexityExpression`, `Cx`, `ComplexitySimplifier`, `CostComposer`, `ComplexityFormatter`, `ExplanationFormatter`, `EvidencePruner`, `ComposedCost` | Port to TS against **`src/shared/algebra-vectors.json`** (same trees → same `Format` / `FormatBigO`) |
| Honesty rule | O(1) only from a catalog or a constant-primitive allowlist; else `C(name)` at Low |
| Catalog-not-versioned rule | One modern lib catalog (current TypeScript `lib.es*.d.ts`), not per-`target` |
| Fast vs deep | Fast = ad-hoc `Program` (buffer + default lib). Deep / ready = `tsconfig`/`jsconfig` `Program` |
| Selection | Same `selection` range; walk only nodes overlapping it |
| Confidence / approaches / patterns | Same roles (`dominant`, `nested`, `sequential`, `alternative`) |

**Do not** start the C# server for a TS/JS-only workspace. Today
`activate()` constructs `AnalyzerRpcClient` unconditionally; `ensure()`
spawns on first C# analyze or `setSolution`. Keep that lazy. A TS-only
user must not download-or-run `ComplexityAnalyzer.Server`.

## TypeScript compiler — what we actually use

There is **no Roslyn `IOperation`**. The public compiler API is:

| Roslyn | TypeScript compiler API |
|---|---|
| `CSharpCompilation` + `SemanticModel` | `ts.Program` + `ts.TypeChecker` |
| `IOperation` tree | Syntax `ts.Node` + checker queries on those nodes |
| `MSBuildWorkspace` | `ts.parseJsonConfigFileContent` + `createProgram` (and project references) |
| Incremental workspace | Keep a `Program`, pass it as `oldProgram` on the next `createProgram`, or `createWatchCompilerHost` |
| `GetDeclaredSymbol` / `GetTypeInfo` | `checker.getSymbolAtLocation` / `getTypeAtLocation` / `getTypeOfSymbolAtLocation` |
| `IInvocationOperation.TargetMethod` | `checker.getResolvedSignature(call)` → `signature.declaration` + symbol flags |
| `ControlFlowGraph` | Limited: `ts.getControlFlowContainer`, flow-node types. **No** full CFG. Recurrence / worklist detectors must walk syntax + symbols, same as today’s C# detectors walk `IOperation` |
| Ad-hoc `CompilationFactory` | `ts.createSourceFile` + `createProgram` with a host that serves the buffer, `lib.esnext.d.ts` from the `typescript` package, and **does resolve** `@types` when a `tsconfig` exists |

**Language service** (`ts.createLanguageService`) is the editor API
(completions, diagnostics). We do **not** use it as the analysis
engine. Microsoft’s own guidance: LS for editor features; `Program` /
watch/`BuilderProgram` for analysis-like work. We need whole-function
AST walks and signature resolution, which `Program` + `TypeChecker`
give directly.

**Worker thread** holds the `Program`. The extension host sends
`{ uri, text, version, tier, selection }` and gets `AnalyzeResponse`.
Typecheck stays off the UI thread. Incremental: on edit, replace that
`SourceFile` and `createProgram(rootNames, options, host, oldProgram)`.

`getTypeChecker()` is lazy. We only query types for nodes the walker
visits in the **current function** (and its local callees when we
inline). We do not run `getPreEmitDiagnostics` on the project on
every keystroke.

### Fast vs deep (TS analog of CompilationFactory / MSBuildWorkspace)

**Fast (default)** is always ad-hoc: one `SourceFile` (`ScriptKind`
from languageId) plus `lib.es*.d.ts` from the bundled `typescript`
package. Same-file inlining only. Missing modules stay unresolved
(`C(name)`), never assumed O(1). Fast does **not** load `tsconfig`.

**Deep** (`Ohno: Run Deep Analysis`)

- Walk up for `tsconfig.json` / `jsconfig.json`.
- Parse with `ts.readConfigFile` + `parseJsonConfigFileContent`
  (mtime-cached). Overlay the buffer via `realpath`.
- Same-`Program` inlining for resolved declarations (not
  `node_modules`). LRU-evict cached programs.
- If the config is missing or the program fails to load, fall back
  to ad-hoc and **do not invent a bound**.

**One analyzer for TS and JS.** The TypeScript compiler is the JS
compiler. `ScriptKind` is `TS` / `TSX` / `JS` / `JSX` from
`languageId`; `allowJs` / `jsconfig.json` put `.js`/`.jsx` in the
same `Program`. There is no second JS engine and no syntax-only
fork. The walker and catalog are identical. What changes is
**how often the checker can prove a type**:

| Receiver the checker sees | Confidence |
|---|---|
| `T[]` / `Map` / `Set` / `string` from a `.d.ts` or annotation | Catalog bind (High or Medium if expected) |
| JSDoc `@type {number[]}` / `checkJs` | Same as an annotation |
| Syntactic `[]` / `new Array` / `new Map` | Catalog (we can see the ctor) |
| `any` / `unknown` / unresolved import | `C(name)` at Low — never O(1) |
| Untyped `for…of` over `any` | Not O(n) High; `C(iterate)` or Unknown |

`checkJs` is a bonus for types, not a requirement. Untyped JS is
still analyzed; semantic confidence drops in place.

## Minimal expansions (keep C# behavior identical)

These are the only product changes needed so C# does not regress and
TS can plug in.

1. **`BUILTIN_LANGUAGES`** — add `typescript`, `javascript`,
   `typescriptreact`, `javascriptreact`. Phase 13 flipped
   `enabledByDefault` to **true**. `documentSelectors()` and
   `ohno.languages.*` follow automatically.
2. **`package.json`** — `onLanguage:typescript` (and js/tsx/jsx);
   four `ohno.languages.*` properties; activation still works when
   only C# is on.
3. **Lazy C# server** — do not construct/bind `SolutionBinder` work
   for non-C# documents. First C# analyze still starts the server.
4. **Worker lifecycle** — start on first enabled TS/JS analyze;
   dispose with the extension; `disposed` flag like `rpcClient`
   (no respawn after deactivate).
5. **Protocol** — no wire change required. Optional later:
   `languageId` on the response for debugging. Do not add it in v1
   unless a UI bug needs it.
6. **C# Core** — **no change** to the .NET algebra. Parity is
   enforced by `src/shared/algebra-vectors.json`, not by linking
   the two runtimes. Protocol schema, catalog snapshot, and those
   vectors are the cheap shared hub — see `src/shared/README.md`.
   Do not send a TS AST through the Roslyn process.
7. **Existing C# tests** — 325 analyzer + 58 extension must stay
   green. The two current TS stub tests are replaced, not kept as
   string-matcher tests.

## Architecture

```
Extension host
  ├─ AnalyzerRegistry
  │    ├─ CSharpAnalyzer ── JSON-RPC stdio ── ComplexityAnalyzer.Server
  │    └─ TypeScriptAnalyzer ── worker_threads ── ohno-ts-worker
  │                                ├─ Program cache (per tsconfig)
  │                                ├─ TypeChecker (lazy, per query)
  │                                ├─ engine/   (ported Core algebra)
  │                                ├─ catalog/  (lib.es + builtins)
  │                                └─ walk/     (Node → ComposedCost)
  └─ UI (annotations, panel, selection) — unchanged
```

Suggested layout (all new, except the facade):

```
src/extension/src/analysis/typescript/
  facade.ts          // IComplexityAnalyzer, posts to worker
  worker.ts          // message loop, Program cache
  tsconfigBinder.ts  // walk-up + parseJsonConfigFileContent
  walk/
    functions.ts     // collect function-like (incl. arrows, methods)
    operations.ts    // loops, calls, news, assigns, jsx
    calls.ts         // resolved signature → catalog / walk / C(name)
    sizes.ts         // .length, params, new Array(n)
  engine/            // port of ComplexityAnalyzer.Core
  catalog/
    arrays.ts maps.ts strings.ts objects.ts regex.ts promises.ts
  patterns/          // concat, regex, worklist, recurrence, heap
```

The current `typescriptAnalyzer.ts` is deleted once the facade lands.

## Semantic walk (the IOperation stand-in)

Walk **syntax**, ask the **checker** at each interesting node.

| Syntax | Cost |
|---|---|
| `for` / `while` / `do` | `LoopBoundInferrer` port: `.length`, integral param, `*= 2` / `/= 2` / `>>= 1`, literal ceiling vs floor |
| `for…of` | Bound = size of the iterated type (`T[]`, `Set`, `Map`, `string`) |
| `for…in` | Bound = key count of the object (usually `n`); Medium + note |
| `for await` | Hard-opaque (`await-foreach` analog) |
| `Array#forEach/map/filter/reduce/flatMap` | **Loop**, not a cataloged O(n) with a free callback. Bind `n` × body of the callback (walk the function or `C(fn)` if it is a parameter) |
| `CallExpression` | `getResolvedSignature` → catalog / local walk / `C(name)` |
| `NewExpression` | `new Array(n)`, `new Map(iterable)`, `new Set(iterable)` sized; else catalog or O(1) only for known empty ctors |
| `PropertyAccess` `.length` | Size of receiver if array/string; user getter → walk if declaration is in the program |
| `obj[key]` | Not O(1). Array index of a `T[]` is O(1); `Map`/`Set` not via `[]`; index signature / `any` → `C(get)` |
| `+` / template in a loop | Repeated string concat pattern (O(n²)) |
| `await` | Soft: name it, keep the structural bound (same as C#) |
| `yield` | Annotate; cost depends on consumption |
| Recursion | Same-name call in the function or a hoisted sibling; 1-D memo still not first-class |
| JSX `<Foo>{items.map(...)}` | `map` is the loop; element create is O(1) per node |
| `eval` / `new Function` / `Proxy` / `any` call | Unknown or `C(name)` |

**`any` / `unknown` / unresolved module:** never O(1). That is the JS
honesty hole the stub falls into.

**Getters / accessors:** same `ohno.annotations.accessors` policy.
A user `get count()` is walked. `array.length` is a cataloged field.

## Catalog (JS/TS builtins)

One table, keyed by **checker-resolved well-known symbol**, not by
method name alone (`sort` on a user class is not `Array.sort`).

Resolve via `symbol.getDeclarations()` → source file is
`lib.es*.d.ts` or a known `@types` path, **or**
`checker.symbolToString` with the containing type’s
`TypeFlags` / `symbol.escapedName` (`Array`, `ReadonlyArray`,
`Map`, `Set`, `String`, `Object`, `Promise`, `RegExp`,
`TypedArray`).

| Type | Exact / expected | Notes |
|---|---|---|
| `Array#push/pop` | Amortized O(1) | `unshift`/`shift`/`splice` are O(n) |
| `Array#sort` / `toSorted` | O(n log n) | `sorts: true` |
| `Array#map/filter/forEach/reduce` | O(n) × callback | Callback is the body |
| `Array#indexOf/includes/find/some/every` | O(n) | |
| `Array#flat` / `flatMap` | O(n) or O(n·m) | Depth default 1 |
| `Map`/`Set` get/has/set/add | Expected O(1) | Medium |
| `String#includes/indexOf/split` | O(n) | `split` space O(n) |
| `String#replace` + `RegExp` | Opaque unless literal + no backref (port `RegexFacts`) | |
| `Object.keys/values/entries` | O(n) | |
| `JSON.parse/stringify` | O(n) | |
| `Promise.all` | Soft await + n × element | Not a tight High O(n) if the factory is opaque |
| `structuredClone` | O(n) | |

Do **not** name-match `get`/`set`/`has`. A user `cache.get` that
walks a list is O(n).

Lib version: same rule as the BCL note in the README. One modern
catalog. `target: es5` vs `esnext` does not change the headline
(`Array.sort` is still a sort). APIs that do not exist on the
file’s lib simply never resolve.

## Patterns to port (v1)

Enough to keep the panel honest, not a full C# clone on day one.

| Pattern | TS/JS shape |
|---|---|
| Repeated string concat | `s += x` / `` s = `${s}${x}` `` in a loop |
| Regex | `RegExp` / `/…/` / `String#match`. No `NonBacktracking` in JS — default is backtracking → Unknown unless a trivial literal |
| Worklist | `while (q.length)` + `shift`/`pop` + enqueue |
| Unbounded worklist | Same without a visited `Set` |
| Heap / sliding window | `if (q.length > k) q.shift()` |
| Branching recursion | Two self-calls (`fib`) |
| Linear recursion | One self-call with `n-1` |
| Interface / `any` dispatch | Soft if a loop exists |
| Queryable analog | **Defer** Prisma/Knex/TypeORM — treat as opaque call, do not invent SQL |

C# patterns we skip in v1: `IQueryable`, `Expression.Compile`,
`lock`, `dynamic` (JS `any` covers the honesty part).

## Honesty holes we must not ship

These are the SessionLedger lessons, in JS form:

| Hole | TS analog | Required behavior |
|---|---|---|
| Invented O(1) | `obj.get(x)` by name | Catalog only on resolved `Map`/`Set`/`Array` |
| Invented `n` | `for (const x of stream)` where type is `any` or `Iterable<T>` with no size | `C(iterate)` or Unknown, not O(n) High |
| Unseeded field | `while (this.q.length)` | Same undercount risk — note Medium or size from last assignment in-function |
| `*= 2` vs `/= 2` | Port **both** as log (C# still misses `*= 2`; do not copy that bug) |
| `goto` | N/A | labelled `break`/`continue` only |
| Deferred `Range().Sum()` | `Array.from({length: n})` / `range(n).reduce` | Size the source |
| Generic `Equals` | `a === b` is O(1) for primitives; user `equals()` is a call | |

## Tests

Mirror the C# fixture style. Do not assert `SessionLedger`; assert
dedicated samples.

| Suite | Role |
|---|---|
| `engine/parity.test.ts` | Load `src/shared/algebra-vectors.json` (already asserted by C# `AlgebraVectorTests`) |
| `samples/typescript/TsBclCatalog.ts` | Everyday `Array`/`Map`/`Set`/`String` usage — no `C(name)`, no silent O(1) on sorts |
| `samples/typescript/TsLoops.ts` | Nested, triangular, `for…of`, `for await`, `do`, `while(true)+break` |
| `samples/javascript/JsUntyped.js` | Untyped `arr.sort()` still a sort if checker sees `any[]` **or** `C(sort)` if the receiver is `any` — pick one rule and lock it (recommendation: if syntactic `ArrayLiteral` or `new Array`, catalog; else `C(sort)`) |
| `samples/typescript/TsHonesty.ts` | User `get()`, `any` call, callback parameter, `eval` |
| `TsConfigTests` | `tsconfig` vs ad-hoc: `#if`-like `const enum` / `paths` — project defines win when ready |
| Existing C# + extension suites | Untouched, still green |

## Phased delivery

One phase per commit, independently revertable. C# behavior does
not change in any phase.

### Phase 0 — Quarantine and wiring

- Do not register the stub.
- Add the four language ids, opt-in settings, activation events.
- Lazy C# server (no behavior change for C# files).
- Worker bootstrap + ping test.
- Rewrite/delete stub tests so they do not lock in string algebra.
- Update `config.test.ts`: it currently asserts
  `languageEnabled('typescript', …)` is **false even when the flag is
  true**, because `languageId` is not in `BUILTIN_LANGUAGES`. After
  Phase 0 that becomes “false by default, true when the user opts in.”
- `package.json` today: `onLanguage:csharp` only, and only
  `ohno.languages.csharp`. Add the four `onLanguage:*` events and
  `ohno.languages.{typescript,javascript,typescriptreact,javascriptreact}`.
- `documentSelectors()` is derived from `BUILTIN_LANGUAGES`; CodeLens
  (and the commented hover) pick it up automatically.

### Phase 1 — Engine port

- Port `ComplexityExpression`, `Cx`, `ComplexitySimplifier`,
  `CostComposer`, `ComplexityFormatter`, `ComposedCost`,
  `EvidencePruner` to `engine/`.
- Golden parity against C# snapshots (the LeetCode / algebra cases
  that are expression-only, no Roslyn).
- Line length / function-size rules as in the C# Core.

### Phase 2 — Ad-hoc walker + catalog v0

- `createSourceFile` + default lib `Program`.
- Function collection (declarations, methods, constructors, arrows
  assigned to `const`, class getters).
- Loops, calls, `new`, `.length`.
- Catalog: `Array` (incl. `sort`/`toSorted`), `Map`, `Set`,
  `String` scans. Callback-as-loop for `map`/`forEach`.
- Honesty: unresolved → `C(name)`.
- Selection range.

### Phase 3 — `tsconfig` / `jsconfig` Program

- Walk-up binder, cache per config path, `oldProgram` reuse.
- Fast uses the project when ready; otherwise ad-hoc.
- Deep waits and warns on fallback.
- `allowJs` for `.js`/`.jsx`.

### Phase 4 — Patterns + panel completeness

- Concat, regex, worklist, recurrence, heap-bound.
- Approaches + confidence reasons.
- Accessors policy.
- JSX: `map` in children.

### Phase 5 — Fixtures, docs, default still off

- Samples + tests above.
- README / DEVELOPER / marketplace: TS/JS opt-in, same honesty
  and catalog-not-versioned rules.
- Changelog.

### Later (not v1)

Capability work that closes the gap with C# — cardinality,
remaining patterns, closures / `this` / computed keys, catalog
depth, space tables, same-`Program` inlining — lives in
[PLAN-TYPESCRIPT-PARITY.md](PLAN-TYPESCRIPT-PARITY.md). That plan
does not change the locked decisions above.

Still out of both plans until explicitly scoped:

- Angular templates (second frontend: Angular compiler or
  `HtmlParser` + component class types).
- Prisma/Knex as queryable-soft.
- `SolutionBuilder` for project references that only exist as
  emitted `.d.ts`.

## Risks

| Risk | Mitigation |
|---|---|
| Typecheck cost on large `node_modules` | Do not pull the whole program into the walker; query checker only on visited nodes. Cache `Program`. Debounce unchanged. |
| `any` everywhere in JS | `C(name)` not O(1); document that untyped JS is looser |
| Name-only catalog regressions | Key by containing type from the checker, same lesson as `get_Item` |
| Dual algebra drift | Golden parity tests in CI; C# Core remains the spec |
| Worker crash | Facade returns a warning + empty functions; do not take down the extension host or the C# server |
| JSX / React runtime | Treat JSX as `createElement` + children; do not catalog `react` beyond that in v1 |
| Path aliases / `baseUrl` | Only correct when the `tsconfig` `Program` is ready |

## Success criteria (v1)

- C# 325 + existing extension tests still green.
- Enabled TS/JS file: same panel, annotations, selection, and
  `annotations.mode` as C#.
- `arr.toSorted()` / `arr.sort()` → O(n log n), never O(1).
- User `obj.get(x)` → `C(get)` or a walked body, never O(1) High.
- `for await` → Unknown, not O(n).
- Untyped JS call → `C(name)` Low, not invented O(1).
- No Roslyn process in a TS-only session.
- Languages off until the user flips the setting.

## Open (non-blocking)

These can be decided during Phase 2 without changing the shape:

- Whether a syntactic `number[]` parameter in ad-hoc JSDoc-less JS
  is enough to size `n`, or we wait for `tsconfig` + JSDoc.
- How far to walk local functions in the same file (C# walks
  in-compilation bodies; do the same).
- Whether `structuredClone` / `JSON.*` land in catalog v0 or v1.1.
