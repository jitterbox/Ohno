using System;
using System.Collections.Generic;
using System.Linq;

public static class Samples
{
    public static int[] TopK(int[] values, int k)
    {
        var pq = new PriorityQueue<int, int>();

        foreach (var value in values)
        {
            pq.Enqueue(value, value);

            if (pq.Count > k)
                pq.Dequeue();
        }

        return pq.UnorderedItems
            .Select(x => x.Element)
            .ToArray();
    }
}
