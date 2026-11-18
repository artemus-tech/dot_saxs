using System;
using System.Collections.Generic;
using System.Numerics;

namespace DotSaxs;

/// <summary>
/// Равенство векторов по числам — как <c>np.array_equal(a, b, equal_nan=True)</c>: длины и числа совпадают, NaN равен
/// NaN, 0 равен −0 (DECISIONS.md п. 34): <c>VectorComparer&lt;double&gt;.ByValues.Equals(a, b)</c>. Тот же компаратор —
/// для <c>HashSet</c>, <c>Dictionary</c> и <c>Assert.Equal</c>. Сам <c>a.Equals(b)</c>, как у всех классов .NET,
/// проверяет, один ли это объект.
/// </summary>
/// <typeparam name="T">Вид чисел.</typeparam>
public sealed class VectorComparer<T> : IEqualityComparer<Vector<T>>
    where T : unmanaged
{
    private VectorComparer()
    {
    }

    /// <summary>Сравнение по длине и числам.</summary>
    public static VectorComparer<T> ByValues { get; } = new();

    /// <summary>
    /// Равны ли векторы по числам — как <c>np.array_equal(a, b, equal_nan=True)</c>: одной длины, и числа на одних
    /// местах равны; NaN равен NaN, у комплексных NaN — если NaN хоть одна часть, как <c>np.isnan</c>.
    /// </summary>
    /// <param name="x">Первый вектор.</param>
    /// <param name="y">Второй вектор.</param>
    /// <returns>Равны ли.</returns>
    /// <remarks>
    /// Оригинал: numpy.array_equal — NumPy v2.5.4, numpy/_core/numeric.py, строка 2472
    ///   https://github.com/numpy/numpy/blob/v2.5.4/numpy/_core/numeric.py#L2472
    /// </remarks>
    [Original("numpy.array_equal", "numpy/_core/numeric.py", 2472)]
    public bool Equals(Vector<T>? x, Vector<T>? y)
    {
        if (ReferenceEquals(x, y))
            return true;
        if (x is null || y is null)
            return false;
        // ref: numpy/_core/numeric.py#L2534 — размеры разные — не равны
        if (x.Count != y.Count)
            return false;
        // ref: numpy/_core/numeric.py#L2549-L2554 — равны или оба NaN
        for (int i = 0; i < x.Count; i++)
        {
            if (!NumberEquality.EqualOrBothNaN(x.Data[x.Offset + i * x.Stride], y.Data[y.Offset + i * y.Stride]))
                return false;
        }
        return true;
    }

    /// <summary>Хеш по длине и числам: у равных по числам векторов он один.</summary>
    /// <param name="obj">Вектор.</param>
    /// <returns>Хеш.</returns>
    public int GetHashCode(Vector<T> obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        var hash = new HashCode();
        hash.Add(obj.Count);
        foreach (T x in obj)
            hash.Add(NumberEquality.HashOf(x));
        return hash.ToHashCode();
    }
}

/// <summary>
/// Равенство матриц по числам — как <c>np.array_equal(a, b, equal_nan=True)</c>: строк и столбцов столько же, числа
/// на одних местах равны, NaN равен NaN (DECISIONS.md п. 34): <c>MatrixComparer&lt;double&gt;.ByValues.Equals(a, b)</c>.
/// Транспонированная матрица и её копия по строкам равны: сравниваются числа, а не то, как они лежат в памяти.
/// </summary>
/// <typeparam name="T">Вид чисел.</typeparam>
public sealed class MatrixComparer<T> : IEqualityComparer<Matrix<T>>
    where T : unmanaged
{
    private MatrixComparer()
    {
    }

    /// <summary>Сравнение по размерам и числам.</summary>
    public static MatrixComparer<T> ByValues { get; } = new();

    /// <summary>
    /// Равны ли матрицы по числам — как <c>np.array_equal(a, b, equal_nan=True)</c>: те же размеры, и числа на одних
    /// местах равны; NaN равен NaN.
    /// </summary>
    /// <param name="x">Первая матрица.</param>
    /// <param name="y">Вторая матрица.</param>
    /// <returns>Равны ли.</returns>
    /// <remarks>
    /// Оригинал: numpy.array_equal — NumPy v2.5.4, numpy/_core/numeric.py, строка 2472
    ///   https://github.com/numpy/numpy/blob/v2.5.4/numpy/_core/numeric.py#L2472
    /// </remarks>
    [Original("numpy.array_equal", "numpy/_core/numeric.py", 2472)]
    public bool Equals(Matrix<T>? x, Matrix<T>? y)
    {
        if (ReferenceEquals(x, y))
            return true;
        if (x is null || y is null)
            return false;
        // ref: numpy/_core/numeric.py#L2534 — размеры разные — не равны
        if (x.RowCount != y.RowCount || x.ColumnCount != y.ColumnCount)
            return false;
        for (int i = 0; i < x.RowCount; i++)
        {
            if (!VectorComparer<T>.ByValues.Equals(x.GetRow(i), y.GetRow(i)))
                return false;
        }
        return true;
    }

    /// <summary>Хеш по размерам и числам: у равных по числам матриц он один.</summary>
    /// <param name="obj">Матрица.</param>
    /// <returns>Хеш.</returns>
    public int GetHashCode(Matrix<T> obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        var hash = new HashCode();
        hash.Add(obj.RowCount);
        hash.Add(obj.ColumnCount);
        foreach (Vector<T> row in obj.GetRows())
        {
            foreach (T x in row)
                hash.Add(NumberEquality.HashOf(x));
        }
        return hash.ToHashCode();
    }
}

/// <summary>Сравнение чисел, как в <c>np.array_equal(…, equal_nan=True)</c>.</summary>
internal static class NumberEquality
{
    /// <summary>
    /// Числа равны или оба NaN. Для double и float так сравнивает сам .NET (<c>double.Equals</c>: NaN равен NaN, 0 равен
    /// −0); комплексное число — NaN, если NaN хоть одна часть, как <c>np.isnan</c>.
    /// </summary>
    internal static bool EqualOrBothNaN<T>(T x, T y)
        where T : unmanaged
    {
        if (typeof(T) == typeof(Complex))
        {
            var a = (Complex)(object)x;
            var b = (Complex)(object)y;
            bool nanA = IsNaN(a), nanB = IsNaN(b);
            if (nanA || nanB)
                return nanA && nanB;
            return a.Real == b.Real && a.Imaginary == b.Imaginary;
        }
        return EqualityComparer<T>.Default.Equals(x, y);
    }

    /// <summary>Хеш числа, согласный с <c>EqualOrBothNaN</c>: у всех NaN он один, у 0 и −0 — тоже.</summary>
    internal static int HashOf<T>(T x)
        where T : unmanaged
    {
        if (typeof(T) == typeof(Complex))
        {
            var a = (Complex)(object)x;
            return IsNaN(a) ? 0 : HashCode.Combine(a.Real, a.Imaginary);
        }
        return EqualityComparer<T>.Default.GetHashCode(x);
    }

    private static bool IsNaN(Complex z) => double.IsNaN(z.Real) || double.IsNaN(z.Imaginary);
}
