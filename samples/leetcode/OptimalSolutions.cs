using System;
using System.Collections.Generic;
using System.Linq;

namespace Ohno.Samples.LeetCode;

/// <summary>
/// Known-optimal C# solutions used to compare Ohno derivations.
/// Expected bounds are documented on each method.
/// </summary>
public static class OptimalSolutions
{
    // LC 1 — Two Sum. Time O(n), Space O(n).
    public static int[] TwoSum(int[] nums, int target)
    {
        var map = new Dictionary<int, int>();
        for (var i = 0; i < nums.Length; i++)
        {
            var need = target - nums[i];
            if (map.TryGetValue(need, out var j))
                return new[] { j, i };
            map[nums[i]] = i;
        }

        return Array.Empty<int>();
    }

    // LC 121 — Best Time to Buy and Sell Stock. Time O(n), Space O(1).
    public static int MaxProfit(int[] prices)
    {
        var min = int.MaxValue;
        var best = 0;
        foreach (var price in prices)
        {
            if (price < min) min = price;
            var gain = price - min;
            if (gain > best) best = gain;
        }

        return best;
    }

    // LC 217 — Contains Duplicate. Time O(n), Space O(n).
    public static bool ContainsDuplicate(int[] nums)
    {
        var seen = new HashSet<int>();
        foreach (var value in nums)
        {
            if (!seen.Add(value)) return true;
        }

        return false;
    }

    // LC 53 — Maximum Subarray (Kadane). Time O(n), Space O(1).
    public static int MaxSubArray(int[] nums)
    {
        var best = nums[0];
        var current = nums[0];
        for (var i = 1; i < nums.Length; i++)
        {
            current = Math.Max(nums[i], current + nums[i]);
            best = Math.Max(best, current);
        }

        return best;
    }

    // LC 11 — Container With Most Water. Time O(n), Space O(1).
    public static int MaxArea(int[] height)
    {
        var left = 0;
        var right = height.Length - 1;
        var best = 0;
        while (left < right)
        {
            var width = right - left;
            var h = Math.Min(height[left], height[right]);
            best = Math.Max(best, width * h);
            if (height[left] < height[right]) left++;
            else right--;
        }

        return best;
    }

    // LC 704 — Binary Search. Time O(log n), Space O(1).
    public static int BinarySearch(int[] nums, int target)
    {
        var lo = 0;
        var hi = nums.Length - 1;
        while (lo <= hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (nums[mid] == target) return mid;
            if (nums[mid] < target) lo = mid + 1;
            else hi = mid - 1;
        }

        return -1;
    }

    // LC 20 — Valid Parentheses. Time O(n), Space O(n).
    public static bool IsValid(string s)
    {
        var stack = new Stack<char>();
        foreach (var c in s)
        {
            if (c is '(' or '[' or '{')
            {
                stack.Push(c);
                continue;
            }

            if (stack.Count == 0) return false;
            var open = stack.Pop();
            if (c == ')' && open != '(') return false;
            if (c == ']' && open != '[') return false;
            if (c == '}' && open != '{') return false;
        }

        return stack.Count == 0;
    }

    // LC 3 — Longest Substring Without Repeating. Time O(n), Space O(n).
    public static int LengthOfLongestSubstring(string s)
    {
        var last = new Dictionary<char, int>();
        var start = 0;
        var best = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (last.TryGetValue(s[i], out var prev) && prev >= start)
                start = prev + 1;
            last[s[i]] = i;
            best = Math.Max(best, i - start + 1);
        }

        return best;
    }

    // LC 56 — Merge Intervals. Time O(n log n), Space O(n).
    public static int[][] Merge(int[][] intervals)
    {
        Array.Sort(intervals, (a, b) => a[0].CompareTo(b[0]));
        var merged = new List<int[]>();
        foreach (var interval in intervals)
        {
            if (merged.Count == 0 || merged[^1][1] < interval[0])
                merged.Add(interval);
            else
                merged[^1][1] = Math.Max(merged[^1][1], interval[1]);
        }

        return merged.ToArray();
    }

    // LC 347 — Top K Frequent. Time O(n log k), Space O(k + n)
    // (O(n) when k ≤ n; Ohno keeps independent dimensions).
    public static int[] TopKFrequent(int[] nums, int k)
    {
        var counts = new Dictionary<int, int>();
        foreach (var value in nums)
            counts[value] = counts.GetValueOrDefault(value) + 1;

        var heap = new PriorityQueue<int, int>();
        foreach (var (value, count) in counts)
        {
            heap.Enqueue(value, count);
            if (heap.Count > k) heap.Dequeue();
        }

        return heap.UnorderedItems.Select(x => x.Element).ToArray();
    }

    // LC 15 — 3Sum. Time O(n²), Space O(1) auxiliary.
    public static IList<IList<int>> ThreeSum(int[] nums)
    {
        Array.Sort(nums);
        var result = new List<IList<int>>();
        for (var i = 0; i < nums.Length; i++)
        {
            if (i > 0 && nums[i] == nums[i - 1]) continue;
            var left = i + 1;
            var right = nums.Length - 1;
            while (left < right)
            {
                var sum = nums[i] + nums[left] + nums[right];
                if (sum == 0)
                {
                    result.Add(new[] { nums[i], nums[left], nums[right] });
                    left++;
                    right--;
                    while (left < right && nums[left] == nums[left - 1]) left++;
                }
                else if (sum < 0) left++;
                else right--;
            }
        }

        return result;
    }

    // LC 70 — Climbing Stairs. Time O(n), Space O(1).
    public static int ClimbStairs(int n)
    {
        if (n <= 2) return n;
        var a = 1;
        var b = 2;
        for (var i = 3; i <= n; i++)
        {
            var next = a + b;
            a = b;
            b = next;
        }

        return b;
    }

    // LC 198 — House Robber. Time O(n), Space O(1).
    public static int Rob(int[] nums)
    {
        var prev = 0;
        var curr = 0;
        foreach (var value in nums)
        {
            var next = Math.Max(curr, prev + value);
            prev = curr;
            curr = next;
        }

        return curr;
    }

    // LC 23 — Merge k Sorted Lists. Time O(n log k), Space O(k).
    public static ListNode? MergeKLists(ListNode[] lists)
    {
        var heap = new PriorityQueue<ListNode, int>();
        foreach (var node in lists)
        {
            if (node != null)
                heap.Enqueue(node, node.Val);
        }

        var dummy = new ListNode();
        var tail = dummy;
        while (heap.Count > 0)
        {
            var node = heap.Dequeue();
            tail.Next = node;
            tail = node;
            if (node.Next != null)
                heap.Enqueue(node.Next, node.Next.Val);
        }

        return dummy.Next;
    }

    // LC 206 — Reverse Linked List. Time O(n), Space O(1).
    public static ListNode? ReverseList(ListNode? head)
    {
        ListNode? prev = null;
        var current = head;
        while (current is not null)
        {
            var next = current.Next;
            current.Next = prev;
            prev = current;
            current = next;
        }

        return prev;
    }

    // LC 141 — Linked List Cycle. Time O(n), Space O(1).
    public static bool HasCycle(ListNode? head)
    {
        var slow = head;
        var fast = head;
        while (fast is not null && fast.Next is not null)
        {
            slow = slow!.Next;
            fast = fast.Next.Next;
            if (slow == fast) return true;
        }

        return false;
    }

    // LC 238 — Product of Array Except Self. Time O(n), Space O(1) extra.
    public static int[] ProductExceptSelf(int[] nums)
    {
        var result = new int[nums.Length];
        var prefix = 1;
        for (var i = 0; i < nums.Length; i++)
        {
            result[i] = prefix;
            prefix *= nums[i];
        }

        var suffix = 1;
        for (var i = nums.Length - 1; i >= 0; i--)
        {
            result[i] *= suffix;
            suffix *= nums[i];
        }

        return result;
    }

    // LC 33 — Search in Rotated Sorted Array. Time O(log n), Space O(1).
    public static int SearchRotated(int[] nums, int target)
    {
        var lo = 0;
        var hi = nums.Length - 1;
        while (lo <= hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (nums[mid] == target) return mid;
            if (nums[lo] <= nums[mid])
            {
                if (nums[lo] <= target && target < nums[mid])
                    hi = mid - 1;
                else lo = mid + 1;
            }
            else if (nums[mid] < target && target <= nums[hi])
                lo = mid + 1;
            else hi = mid - 1;
        }

        return -1;
    }

    // LC 42 — Trapping Rain Water. Time O(n), Space O(1).
    public static int Trap(int[] height)
    {
        var left = 0;
        var right = height.Length - 1;
        var leftMax = 0;
        var rightMax = 0;
        var water = 0;
        while (left < right)
        {
            if (height[left] < height[right])
            {
                if (height[left] >= leftMax) leftMax = height[left];
                else water += leftMax - height[left];
                left++;
            }
            else
            {
                if (height[right] >= rightMax) rightMax = height[right];
                else water += rightMax - height[right];
                right--;
            }
        }

        return water;
    }

    // LC 49 — Group Anagrams. Time O(k n log k), Space O(k + n).
    public static List<List<string>> GroupAnagrams(string[] strs)
    {
        var groups = new Dictionary<string, List<string>>();
        foreach (var s in strs)
        {
            var chars = s.ToCharArray();
            Array.Sort(chars);
            var key = new string(chars);
            if (!groups.TryGetValue(key, out var list))
            {
                list = new List<string>();
                groups[key] = list;
            }

            list.Add(s);
        }

        return groups.Values.ToList();
    }

    // LC 322 — Coin Change. Time O(m n), Space O(m).
    public static int CoinChange(int[] coins, int amount)
    {
        var dp = new int[amount + 1];
        Array.Fill(dp, amount + 1);
        dp[0] = 0;
        for (var a = 1; a <= amount; a++)
        {
            foreach (var coin in coins)
            {
                if (coin <= a)
                    dp[a] = Math.Min(dp[a], dp[a - coin] + 1);
            }
        }

        return dp[amount] > amount ? -1 : dp[amount];
    }

    // LC 300 — Longest Increasing Subsequence. Time O(n log n), Space O(n).
    public static int LengthOfLIS(int[] nums)
    {
        var tails = new List<int>();
        foreach (var value in nums)
        {
            var i = tails.BinarySearch(value);
            if (i < 0) i = ~i;
            if (i == tails.Count) tails.Add(value);
            else tails[i] = value;
        }

        return tails.Count;
    }

    // LC 743 — Network Delay Time. Time O(m log n + n log n),
    // Space O(m + n). Start is a vertex id, not a size.
    public static int NetworkDelayTime(
        int n, int[][] times, int start)
    {
        var adj = new List<(int To, int W)>[n + 1];
        for (var i = 0; i <= n; i++)
            adj[i] = new List<(int, int)>();
        foreach (var e in times)
            adj[e[0]].Add((e[1], e[2]));

        var dist = new int[n + 1];
        Array.Fill(dist, int.MaxValue);
        dist[start] = 0;
        var heap = new PriorityQueue<int, int>();
        heap.Enqueue(start, 0);
        while (heap.Count > 0)
        {
            heap.TryDequeue(out var u, out var d);
            if (d > dist[u]) continue;
            foreach (var (v, w) in adj[u])
            {
                var nd = d + w;
                if (nd >= dist[v]) continue;
                dist[v] = nd;
                heap.Enqueue(v, nd);
            }
        }

        var best = 0;
        for (var i = 1; i <= n; i++)
        {
            if (dist[i] == int.MaxValue) return -1;
            best = Math.Max(best, dist[i]);
        }

        return best;
    }

    // LC 207 — Course Schedule (Kahn). Time O(m + n), Space O(m + n).
    public static bool CanFinish(
        int numCourses, int[][] prerequisites)
    {
        var adj = new List<int>[numCourses];
        var indeg = new int[numCourses];
        for (var i = 0; i < numCourses; i++)
            adj[i] = new List<int>();
        foreach (var e in prerequisites)
        {
            adj[e[1]].Add(e[0]);
            indeg[e[0]]++;
        }

        var q = new Queue<int>();
        for (var i = 0; i < numCourses; i++)
            if (indeg[i] == 0)
                q.Enqueue(i);

        var seen = 0;
        while (q.Count > 0)
        {
            var u = q.Dequeue();
            seen++;
            foreach (var v in adj[u])
            {
                indeg[v]--;
                if (indeg[v] == 0)
                    q.Enqueue(v);
            }
        }

        return seen == numCourses;
    }
}

public sealed class ListNode
{
    public int Val;
    public ListNode? Next;
    public ListNode(int val = 0, ListNode? next = null)
    {
        Val = val;
        Next = next;
    }
}
