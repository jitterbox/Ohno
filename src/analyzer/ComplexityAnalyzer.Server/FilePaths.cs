namespace ComplexityAnalyzer.Server;

/// <summary>
/// Normalizes file URIs and filesystem paths for Windows and Unix.
/// </summary>
internal static class FilePaths
{
    public static string FromUri(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            && parsed.IsFile)
        {
            return Normalize(parsed.LocalPath);
        }

        return Normalize(uri);
    }

    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        try
        {
            return Path.GetFullPath(path);
        }
        catch (ArgumentException)
        {
            return path.Replace('/', Path.DirectorySeparatorChar);
        }
        catch (NotSupportedException)
        {
            return path.Replace('/', Path.DirectorySeparatorChar);
        }
        catch (PathTooLongException)
        {
            return path.Replace('/', Path.DirectorySeparatorChar);
        }
    }

    public static bool Equal(string? left, string right)
    {
        if (string.IsNullOrEmpty(left)) return false;
        return string.Equals(
            Normalize(left),
            Normalize(right),
            Comparison);
    }

    private static StringComparison Comparison =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}
