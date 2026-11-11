namespace DotSaxs.ConventionsTests.Samples;

/// <summary>[Original] не сходится с names.csv: функции нет, функция не переносится, метод не назван в таблице.</summary>
public static class Mislabeled
{
    /// <summary>Такой функции в NumPy нет.</summary>
    [Original("numpy.no_such_function", "numpy/no_such_file.py", 1)]
    public static void Run()
    {
    }

    /// <summary>numpy.swapaxes не переносится (DECISIONS.md п. 24).</summary>
    [Original("numpy.swapaxes", "numpy/_core/fromnumeric.py", 1)]
    public static void Swap()
    {
    }

    /// <summary>numpy.sum переносится, но names.csv не называет метод Total.</summary>
    [Original("numpy.sum", "numpy/_core/fromnumeric.py", 2343)]
    public static double Total() => 0;
}

/// <summary>[Original] сходится с names.csv: Sum — в примере вызова, GetColumnSums — в примечании.</summary>
public static class WellLabeled
{
    /// <summary>Сумма.</summary>
    [Original("numpy.sum", "numpy/_core/fromnumeric.py", 2343)]
    public static double Sum() => 0;

    /// <summary>Суммы столбцов.</summary>
    [Original("numpy.sum", "numpy/_core/fromnumeric.py", 2343)]
    public static double GetColumnSums() => 0;
}

/// <summary>Diff переносит numpy.diff (names.csv: «метод», v.Diff()), но без [Original]; GetRow — свой метод, ему можно.</summary>
public static class Unlabeled
{
    /// <summary>Разности без [Original].</summary>
    public static double Diff() => 0;

    /// <summary>Свой метод без оригинала.</summary>
    public static double GetRow() => 0;
}
