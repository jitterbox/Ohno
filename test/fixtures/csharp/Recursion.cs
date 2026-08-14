public static class RecursionCases
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
