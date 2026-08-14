#!/usr/bin/env node
import * as fs from 'node:fs';
import * as path from 'node:path';

const root = path.resolve(import.meta.dirname, '..', 'test', 'fixtures');
fs.mkdirSync(path.join(root, 'csharp'), { recursive: true });
fs.mkdirSync(path.join(root, 'typescript'), { recursive: true });

const csharp = {
  'EfQueries.cs': `using System.Linq;
using System.Collections.Generic;

public class Order { public int Id { get; set; } public int Total { get; set; } }

public static class EfQueries
{
    public static List<Order> LoadExpensive(IQueryable<Order> orders)
    {
        return orders.Where(o => o.Total > 100).ToList();
    }

    public static List<Order> LoadAfterAsEnumerable(IQueryable<Order> orders)
    {
        return orders.AsEnumerable().Where(o => o.Total > 100).ToList();
    }

    public static IQueryable<Order> BuildExpression(
        IQueryable<Order> orders,
        System.Linq.Expressions.Expression<System.Func<Order, bool>> pred)
    {
        return orders.Where(pred);
    }
}
`,
  'Recursion.cs': `public static class RecursionCases
{
    public static int Linear(int n) => n <= 0 ? 0 : n + Linear(n - 1);

    public static void MergeSort(int[] a, int n)
    {
        if (n <= 1) return;
        MergeSort(a, n / 2);
        MergeSort(a, n / 2);
    }

    public static void A(int n) { if (n > 0) B(n); }
    public static void B(int n) { if (n > 0) A(n - 1); }
}
`,
  'Heaps.cs': `using System.Collections.Generic;
using System.Linq;

public static class Heaps
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
        return pq.UnorderedItems.Select(x => x.Element).ToArray();
    }

    public static void Unbounded(int[] values)
    {
        var pq = new PriorityQueue<int, int>();
        foreach (var value in values)
            pq.Enqueue(value, value);
    }
}
`,
  'Dispatch.cs': `using System;

public interface IProcessor { void Process(int item); }

public static class Dispatch
{
    public static void Walk(int[] items, IProcessor processor)
    {
        foreach (var item in items)
            processor.Process(item);
    }

    public static void DynamicWalk(int[] items, dynamic d)
    {
        foreach (var item in items)
            d.Process(item);
    }

    public static void Reflect(object target)
    {
        target.GetType().GetMethod("Process")?.Invoke(target, new object[] { 1 });
    }
}
`,
};

const typescript = {
  'loops.ts': `export function contains(items: number[], value: number): boolean {
  for (const n of items) {
    if (n === value) return true;
  }
  return false;
}

export function sortNums(nums: number[]): number[] {
  return nums.toSorted();
}
`,
};

for (const [name, body] of Object.entries(csharp)) {
  fs.writeFileSync(path.join(root, 'csharp', name), body);
}
for (const [name, body] of Object.entries(typescript)) {
  fs.writeFileSync(path.join(root, 'typescript', name), body);
}

console.log('Wrote fixtures to', root);
