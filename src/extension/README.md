# Oʰ(Nᵒ) — Algorithmic Complexity

<p align="center">
  <img src="media/icon.png" alt="Oʰ(Nᵒ)" width="128" height="128">
</p>

Inline **Big-O time and auxiliary-space** estimates for functions in
the focused editor, with confidence, named approaches, recognized
patterns, and a derivation tree.

C# is the only selectable language (bundled Roslyn server).

This is **not** Visual Studio cyclomatic complexity
([CA1502](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1502)).
Ohno estimates how work and peak extra memory grow with input size.
Library costs are the current BCL, not the project's
`TargetFramework` — the same source gets the same bound on net8
as on net10.

The Complexity view lists up to three **approaches** (dominant,
nested, sequential, or alternative). Select a statement or loop to
re-analyze that span; a hint asks you to narrow further when more
than one approach remains. Clear the selection to return to the
whole function.

## Commands

- **Ohno: Run Deep Analysis** — wait for the project graph and
  re-analyze (fast already uses that graph when it is ready)
- **Ohno: Show Complexity Derivation** — focus the Complexity view
- **Ohno: Focus Complexity Panel** — open the activity-bar view
- **Ohno: Toggle Complexity Annotations** — hide or show end-of-line
  decorations
- **Ohno: Copy Complexity Summary** — copy the selection result if
  one is active, otherwise the function at the caret

## Settings

See the [repository README](https://github.com/jitterbox/Ohno/blob/main/README.md)
for the full table (`ohno.enabled`, `ohno.languages.*`,
`ohno.analysis.tier`, `ohno.annotations.*`).

## Known issues / unsupported

`#:package` restore, ad-hoc source generators, every `#if` arm,
`.razor` / `.cshtml` / `.csx`, and tight bounds for `IQueryable` /
`dynamic` / expression trees are **unsupported**. See the
[repository README](https://github.com/jitterbox/Ohno/blob/main/README.md#known-issues--unsupported).

## Attribution

GitLens (MIT) and Material Symbols (Apache 2.0). See
[NOTICE](https://github.com/jitterbox/Ohno/blob/main/NOTICE).

## License

MIT. See [LICENSE](https://github.com/jitterbox/Ohno/blob/main/LICENSE).
