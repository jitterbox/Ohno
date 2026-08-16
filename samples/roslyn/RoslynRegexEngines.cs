using System;
using System.Text.RegularExpressions;

namespace Ohno.Samples.Roslyn;

/// <summary>
/// The two .NET regex engines, which have genuinely different bounds.
/// </summary>
/// <remarks>
/// The default engine backtracks: cost depends on the pattern and the
/// input together, and a pathological pattern is exponential. That is
/// reported as opaque, and should stay that way.
/// <para>
/// <c>RegexOptions.NonBacktracking</c> simulates the automaton and
/// never revisits a character, so a match is linear in the subject no
/// matter what the pattern is. That guarantee is documented by the
/// runtime, so naming the option earns a real bound.
/// </para>
/// <para>
/// The distinction only holds where the option is provable at the
/// construction site. A <c>Regex</c> arriving from elsewhere keeps the
/// opaque treatment.
/// </para>
/// </remarks>
public static partial class RoslynRegexEngines
{
    // INCONCLUSIVE — the default engine can backtrack.
    public static bool BacktrackingMatch(string text)
    {
        var pattern = new Regex("^(a+)+$");
        return pattern.IsMatch(text);
    }

    // Known Time: Θ(n) — non-backtracking engine, linear scan.
    public static bool LinearMatch(string text)
    {
        var pattern = new Regex("^(a+)+$", RegexOptions.NonBacktracking);
        return pattern.IsMatch(text);
    }

    // Known Time: Θ(n) — the option survives being combined.
    public static bool LinearWithCombinedOptions(string text)
    {
        var pattern = new Regex(
            "^[a-z]+$",
            RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);
        return pattern.IsMatch(text);
    }

    // Known Time: Θ(n) — construction and use in one expression.
    public static bool LinearInlineMatch(string text)
    {
        return new Regex("a+", RegexOptions.NonBacktracking)
            .IsMatch(text);
    }

    // Known Time: Θ(n) — static overload carrying the options.
    public static bool LinearStaticMatch(string text)
    {
        return Regex.IsMatch(text, "^[0-9]+$", RegexOptions.NonBacktracking);
    }

    // Known Time: Θ(n), Space Θ(n) — Replace materializes a new string.
    public static string LinearReplace(string text)
    {
        var pattern = new Regex("\\s+", RegexOptions.NonBacktracking);
        return pattern.Replace(text, " ");
    }

    // INCONCLUSIVE — options are not visible at this call site.
    public static bool ProvidedRegex(Regex pattern, string text)
    {
        return pattern.IsMatch(text);
    }

    // INCONCLUSIVE — the default engine, stated explicitly.
    public static bool ExplicitBacktracking(string text)
    {
        return Regex.IsMatch(text, "^(a+)+$", RegexOptions.Compiled);
    }
}
