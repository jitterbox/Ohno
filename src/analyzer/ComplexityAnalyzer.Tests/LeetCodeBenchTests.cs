using ComplexityAnalyzer.Core;
using Xunit;
using Xunit.Abstractions;

namespace ComplexityAnalyzer.Tests;

/// <summary>
/// Known-optimal C# solutions from
/// <c>samples/leetcode/OptimalSolutions.cs</c>.
/// Guards against inventing a tighter or looser bound on textbook
/// algorithms (two pointers, heap-k, DP, binary search).
/// </summary>
public class LeetCodeBenchTests
{
    private readonly ITestOutputHelper _output;

    public LeetCodeBenchTests(ITestOutputHelper output) => _output = output;

    public static TheoryData<string, string, string> Cases => new()
    {
        { "TwoSum", "O(n)", "O(n)" },
        { "MaxProfit", "O(n)", "O(1)" },
        { "ContainsDuplicate", "O(n)", "O(n)" },
        { "MaxSubArray", "O(n)", "O(1)" },
        { "MaxArea", "O(n)", "O(1)" },
        { "BinarySearch", "O(log n)", "O(1)" },
        { "IsValid", "O(n)", "O(n)" },
        { "LengthOfLongestSubstring", "O(n)", "O(n)" },
        { "Merge", "O(n log n)", "O(n)" },
        { "TopKFrequent", "O(n log k)", "O(k + n)" },
        { "ThreeSum", "O(n²)", "O(n)" },
        { "ClimbStairs", "O(n)", "O(1)" },
        { "Rob", "O(n)", "O(1)" },
        { "MergeKLists", "O(n log k)", "O(k)" },
        { "ReverseList", "O(n)", "O(1)" },
        { "HasCycle", "O(n)", "O(1)" },
        { "ProductExceptSelf", "O(n)", "O(n)" },
        { "SearchRotated", "O(log n)", "O(1)" },
        { "Trap", "O(n)", "O(1)" },
        { "GroupAnagrams", "O(k n log k)", "O(k + n)" },
        { "CoinChange", "O(m n)", "O(m)" },
        { "LengthOfLIS", "O(n log n)", "O(n)" },
        { "NetworkDelayTime", "O(m log n + n log n)", "O(m + n)" },
        { "CanFinish", "O(m + n)", "O(m + n)" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void OptimalSolution_MatchesKnownBound(
        string name, string time, string space)
    {
        var source = File.ReadAllText(SolutionsPath());
        var result = SnippetAnalyzer.AnalyzeNamed(source, name: name);
        var actualTime = ComplexityFormatter.FormatBigO(result.Time);
        var actualSpace = ComplexityFormatter.FormatBigO(result.AuxiliarySpace);
        _output.WriteLine(
            $"{name}: {actualTime} / {actualSpace} " +
            $"(expected {time} / {space}) " +
            $"conf={result.Confidence}");
        Assert.Equal(time, actualTime);
        Assert.Equal(space, actualSpace);
    }

    private static string SolutionsPath()
    {
        var dir = AppContext.BaseDirectory;
        var current = new DirectoryInfo(dir);
        while (current is not null)
        {
            var candidate = Path.Combine(
                current.FullName,
                "samples",
                "leetcode",
                "OptimalSolutions.cs");
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }

        throw new FileNotFoundException("OptimalSolutions.cs not found.");
    }
}
