# Shared contracts

Cheap, language-neutral facts. C# and TypeScript each implement
their own walker. They do **not** share a process.

| File | Role |
|---|---|
| `protocol.schema.json` | Wire shape. Source of truth vs `protocol.ts` and `Contracts.cs`. |
| `protocol.ts` | TypeScript types the extension already imports. |
| `catalog.schema.json` | Entry format (`SizeKind`, `CostKind`, flags). |
| `catalog.json` | Snapshot of `OperationCatalog.CreateDefault()`. |
| `algebra-vectors.json` | Golden `Format` / `FormatBigO` trees. C# Core is the spec. |
| `algebra-vectors.schema.json` | Shape of those trees. |

Honesty (O(1) only from a catalog or a constant-primitive allowlist;
else `C(name)` at Low) is enforced by tests and docs, not a shared
class.

After changing `OperationCatalog`, refresh the snapshot:

```bash
OHNO_WRITE_SHARED=1 dotnet test \
  src/analyzer/ComplexityAnalyzer.Tests \
  --filter SharedCatalog
```
