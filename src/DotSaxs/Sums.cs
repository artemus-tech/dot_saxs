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
        return Total(v.Data, v.Offset, v.Count, v.Stride);
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
        return Total(v.Data, v.Offset, v.Count, v.Stride);
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
        // Матрица хранит числа по строкам без промежутков, поэтому NumPy складывает их как один вектор.
        return Total(m.Data, m.Offset, m.RowCount * m.ColumnCount, 1);
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
        int rows = m.RowCount, columns = m.ColumnCount;
        var sums = new double[rows];
        var plain = default(PlainAddition<double>);
        for (int i = 0; i < rows; i++)
            sums[i] = 0.0 + Pairwise.Sum(m.Data, m.Offset + i * columns, columns, 1, ref plain);
        // Ошибки чисел — после расчёта всех строк, как у NumPy: пересчитываются только строки с inf или nan.
        var check = default(CheckedAddition<double>);
        for (int i = 0; i < rows; i++)
        {
            if (!double.IsFinite(sums[i]))
                check.Add(0.0, Pairwise.Sum(m.Data, m.Offset + i * columns, columns, 1, ref check));
        }
        FloatErrors.Check(check.Flags, Reduce);
        return Vector<double>.From(sums);
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
        return RowByRow(m.GetRows().ToArray(), m.ColumnCount);
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
        return RowByRow(list, length);
    }

    /// <summary>
    /// Попарная сумма с проверкой ошибок чисел: обычный расчёт, а если вышло inf или nan — пересчёт в том же порядке,
    /// чтобы узнать, было ли переполнение или inf − inf. Конечная сумма значит, что ошибок не было: inf и nan по дороге
    /// уже не исчезают.
    /// </summary>
    private static T Total<T>(T[] data, int start, int n, int stride)
        where T : System.Numerics.IFloatingPointIeee754<T>
    {
        // ref: numpy/_core/src/umath/reduction.c#L347 — начальное значение суммы — единица сложения, +0
        // ref: numpy/_core/src/umath/loops_arithm_fp.dispatch.c.src#L52-L54 — к нему прибавляется попарная сумма
        var plain = default(PlainAddition<T>);
        T sum = T.Zero + Pairwise.Sum(data, start, n, stride, ref plain);
        if (!T.IsFinite(sum))
        {
            var check = default(CheckedAddition<T>);
            check.Add(T.Zero, Pairwise.Sum(data, start, n, stride, ref check));
            FloatErrors.Check(check.Flags, Reduce);
        }
        return sum;
    }

    /// <summary>
    /// Сложение векторов по местам строка за строкой — так NumPy считает <c>np.sum(a, axis=0)</c>: результат
    /// начинается с нулей, к нему по очереди прибавляется каждая строка.
    /// </summary>
    private static Vector<double> RowByRow(Vector<double>[] rows, int length)
    {
        var sums = new double[length];
        foreach (Vector<double> row in rows)
        {
            for (int j = 0; j < length; j++)
                sums[j] += row[j];
        }
        var check = default(CheckedAddition<double>);
        for (int j = 0; j < length; j++)
        {
            if (double.IsFinite(sums[j]))
                continue;
            double sum = 0.0;
            foreach (Vector<double> row in rows)
                sum = check.Add(sum, row[j]);
        }
        FloatErrors.Check(check.Flags, Reduce);
        return Vector<double>.From(sums);
    }
}
