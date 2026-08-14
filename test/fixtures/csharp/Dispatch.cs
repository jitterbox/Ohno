using System;

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
