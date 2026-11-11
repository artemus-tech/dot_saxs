using System;
using System.Collections.Generic;
using System.Linq;
using DotSaxs.Core;

namespace DotSaxs;

/// <summary>
/// Свои суммы — как <c>np.sum</c>, до бита (DECISIONS.md п. 24). Обычный LINQ <c>Sum</c> складывает подряд: десять
/// раз по 0.1 у него 0.9999999999999999, а у NumPy и здесь — 1.0. C# сам выбирает эти методы вместо LINQ.
/// </summary>
/// <remarks>
/// Переполнение и inf − inf по умолчанию бросают <see cref="FloatingPointError"/> с текстом NumPy, например
/// <c>invalid value encountered in reduce</c>; молчать — <c>using (Np.ErrState(all: "ignore"))</c> (п. 32).
/// </remarks>
public static class Sums
{
    // Имя, с которым NumPy сообщает об ошибках чисел в сумме: «overflow encountered in reduce».
    private const string Reduce = "reduce";

    // Оси свёртки — биты: ось 0 — 0b01, ось 1 — 0b10; все оси — все биты.
    private const int Axis0 = 0b01;
    private const int Axis1 = 0b10;
    private const int AllAxes = ~0;

    /// <summary>Сумма чисел — как <c>np.sum(v)</c>. У пустого вектора — 0.</summary>
    /// <param name="v">Вектор.</param>
    /// <returns>Сумма.</returns>
    /// <exception cref="FloatingPointError">Переполнение или inf − inf (DECISIONS.md п. 32).</exception>
    /// <remarks>
    /// Оригинал: numpy.sum — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 2343
    ///   https://github.com/numpy/numpy/blob/v2.5.4/numpy/_core/fromnumeric.py#L2343
    /// Вызывает: попарное суммирование — numpy/_core/src/umath/loops_utils.h.src, строка 81
    /// </remarks>
    [Original("numpy.sum", "numpy/_core/fromnumeric.py", 2343)]
    public static double Sum(this Vector<double> v)
    {
        ArgumentNullException.ThrowIfNull(v);
        return Total(v.Data, v.Layout, AllAxes)[0];
    }

    /// <summary>Сумма чисел — как <c>np.sum(v)</c>: складывается во float, как у NumPy. У пустого вектора — 0.</summary>
    /// <param name="v">Вектор.</param>
    /// <returns>Сумма.</returns>
    /// <exception cref="FloatingPointError">Переполнение или inf − inf (DECISIONS.md п. 32).</exception>
    /// <remarks>
    /// Оригинал: numpy.sum — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 2343
    ///   https://github.com/numpy/numpy/blob/v2.5.4/numpy/_core/fromnumeric.py#L2343
    /// </remarks>
    [Original("numpy.sum", "numpy/_core/fromnumeric.py", 2343)]
    public static float Sum(this Vector<float> v)
    {
        ArgumentNullException.ThrowIfNull(v);
        return Total(v.Data, v.Layout, AllAxes)[0];
    }

    /// <summary>Сумма чисел — как <c>np.sum(v)</c>: при переполнении long идёт по кругу, как у NumPy.</summary>
    /// <param name="v">Вектор.</param>
    /// <returns>Сумма.</returns>
    /// <remarks>
    /// Оригинал: numpy.sum — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 2343
    ///   https://github.com/numpy/numpy/blob/v2.5.4/numpy/_core/fromnumeric.py#L2343
    /// </remarks>
    [Original("numpy.sum", "numpy/_core/fromnumeric.py", 2343)]
    public static long Sum(this Vector<long> v)
    {
        ArgumentNullException.ThrowIfNull(v);
        long sum = 0;
        foreach (long x in v)
            sum = unchecked(sum + x);
        return sum;
    }

    /// <summary>
    /// Сумма чисел — как <c>np.sum(v)</c>: NumPy складывает int в long (int64), поэтому и результат — long.
    /// </summary>
    /// <param name="v">Вектор.</param>
    /// <returns>Сумма.</returns>
    /// <remarks>
    /// Оригинал: numpy.sum — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 2343
    ///   https://github.com/numpy/numpy/blob/v2.5.4/numpy/_core/fromnumeric.py#L2343
    /// </remarks>
    [Original("numpy.sum", "numpy/_core/fromnumeric.py", 2343)]
    public static long Sum(this Vector<int> v)
    {
        ArgumentNullException.ThrowIfNull(v);
        long sum = 0;
        foreach (int x in v)
            sum = unchecked(sum + x);
        return sum;
    }

    /// <summary>Сумма всех чисел матрицы — как <c>np.sum(m)</c>: попарно по всем числам подряд, по строкам.</summary>
    /// <param name="m">Матрица.</param>
    /// <returns>Сумма.</returns>
    /// <exception cref="FloatingPointError">Переполнение или inf − inf (DECISIONS.md п. 32).</exception>
    /// <remarks>
    /// Оригинал: numpy.sum — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 2343
    ///   https://github.com/numpy/numpy/blob/v2.5.4/numpy/_core/fromnumeric.py#L2343
    /// </remarks>
    [Original("numpy.sum", "numpy/_core/fromnumeric.py", 2343)]
    public static double Sum(this Matrix<double> m)
    {
        ArgumentNullException.ThrowIfNull(m);
        return Total(m.Data, m.Layout, AllAxes)[0];
    }

    /// <summary>Суммы строк — как <c>np.sum(m, axis=1)</c>: каждая строка — попарно.</summary>
    /// <param name="m">Матрица.</param>
    /// <returns>Вектор из <see cref="Matrix{T}.RowCount"/> сумм.</returns>
    /// <exception cref="FloatingPointError">Переполнение или inf − inf (DECISIONS.md п. 32).</exception>
    /// <remarks>
    /// Оригинал: numpy.sum — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 2343
    ///   https://github.com/numpy/numpy/blob/v2.5.4/numpy/_core/fromnumeric.py#L2343
    /// </remarks>
    [Original("numpy.sum", "numpy/_core/fromnumeric.py", 2343)]
    public static Vector<double> GetRowSums(this Matrix<double> m)
    {
        ArgumentNullException.ThrowIfNull(m);
        return ToVector(Total(m.Data, m.Layout, Axis1));
    }

    /// <summary>
    /// Суммы столбцов — как <c>np.sum(m, axis=0)</c>: NumPy прибавляет строку за строкой, а не попарно, поэтому и
    /// здесь так же. То же самое — <c>m.GetRows().Sum()</c>.
    /// </summary>
    /// <param name="m">Матрица.</param>
    /// <returns>Вектор из <see cref="Matrix{T}.ColumnCount"/> сумм.</returns>
    /// <exception cref="FloatingPointError">Переполнение или inf − inf (DECISIONS.md п. 32).</exception>
    /// <remarks>
    /// Оригинал: numpy.sum — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 2343
    ///   https://github.com/numpy/numpy/blob/v2.5.4/numpy/_core/fromnumeric.py#L2343
    /// </remarks>
    [Original("numpy.sum", "numpy/_core/fromnumeric.py", 2343)]
    public static Vector<double> GetColumnSums(this Matrix<double> m)
    {
        ArgumentNullException.ThrowIfNull(m);
        return ToVector(Total(m.Data, m.Layout, Axis0));
    }

    /// <summary>
    /// Векторы одной длины, сложенные по местам: первое число с первым и так далее — как
    /// <c>np.sum(np.stack(vectors), axis=0)</c>. Строки матрицы — <c>m.GetRows().Sum()</c>, то же, что
    /// <see cref="GetColumnSums"/>.
    /// </summary>
    /// <param name="vectors">Векторы одной длины, хотя бы один.</param>
    /// <returns>Вектор сумм.</returns>
    /// <exception cref="ValueError">Векторов нет или они разной длины.</exception>
    /// <exception cref="FloatingPointError">Переполнение или inf − inf (DECISIONS.md п. 32).</exception>
    /// <remarks>
    /// Оригинал: numpy.sum — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 2343
    ///   https://github.com/numpy/numpy/blob/v2.5.4/numpy/_core/fromnumeric.py#L2343
    /// </remarks>
    [Original("numpy.sum", "numpy/_core/fromnumeric.py", 2343)]
    public static Vector<double> Sum(this IEnumerable<Vector<double>> vectors)
    {
        ArgumentNullException.ThrowIfNull(vectors);
        Vector<double>[] list = vectors.ToArray();
        if (list.Length == 0)
            throw new ValueError(Messages.NoVectors());
        int length = list[0].Count;
        for (int k = 0; k < list.Length; k++)
        {
            ArgumentNullException.ThrowIfNull(list[k], nameof(vectors));
            if (list[k].Count != length)
                throw new ValueError(Messages.VectorLengthsDiffer(length, k, list[k].Count));
        }
        // np.stack кладёт векторы строками новой матрицы, и сумма по оси 0 идёт по ней.
        var stacked = new double[list.Length * length];
        for (int k = 0; k < list.Length; k++)
            list[k].CopyTo(stacked, k * length);
        return ToVector(Total(stacked, Layout.Contiguous([list.Length, length]), Axis0));
    }

    /// <summary>
    /// Сумма по осям из <paramref name="reduced"/> в порядке NumPy (Core/Reduction.cs) с проверкой ошибок чисел:
    /// обычный расчёт, а если где-то вышло inf или nan — пересчёт в том же порядке, чтобы узнать, было ли переполнение
    /// или inf − inf. Конечная сумма значит, что ошибок в ней не было: inf и nan по дороге уже не исчезают.
    /// </summary>
    private static T[] Total<T>(T[] data, in Layout layout, int reduced)
        where T : System.Numerics.IFloatingPointIeee754<T>
    {
        var plain = default(PlainAddition<T>);
        T[] sums = Reduction.Sum(data, layout, reduced, ref plain);
        foreach (T sum in sums)
        {
            if (T.IsFinite(sum))
                continue;
            var check = default(CheckedAddition<T>);
            Reduction.Sum(data, layout, reduced, ref check);
            FloatErrors.Check(check.Flags, Reduce);
            break;
        }
        return sums;
    }

    private static Vector<double> ToVector(double[] sums) => new(sums, 0, sums.Length, 1);
}
