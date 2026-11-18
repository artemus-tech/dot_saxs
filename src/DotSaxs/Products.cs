using System;
using System.Globalization;
using DotSaxs.Core;

namespace DotSaxs;

/// <summary>
/// Произведения векторов — как в NumPy, до бита (DECISIONS.md п. 22): векторное — <c>a.CrossProduct(b)</c>
/// (<c>np.cross</c>). Скалярное (<c>ScalarProduct</c>) и все пары (<c>CartesianProduct</c>) — на этапе 3, вместе с BLAS.
/// </summary>
public static class Products
{
    /// <summary>
    /// Векторное произведение векторов из трёх чисел — как <c>np.cross(a, b)</c>:
    /// (a₁b₂ − a₂b₁, a₂b₀ − a₀b₂, a₀b₁ − a₁b₀). Каждое умножение и вычитание — как отдельная функция NumPy.
    /// </summary>
    /// <param name="a">Первый вектор, три числа.</param>
    /// <param name="b">Второй вектор, три числа.</param>
    /// <returns>Новый вектор из трёх чисел.</returns>
    /// <exception cref="ValueError">В векторе не три числа — текст как у NumPy.</exception>
    /// <exception cref="FloatingPointError">Переполнение или ∞ − ∞ (DECISIONS.md п. 32).</exception>
    /// <remarks>
    /// Оригинал: numpy.cross — NumPy v2.5.4, numpy/_core/numeric.py, строка 1564
    ///   https://github.com/numpy/numpy/blob/v2.5.4/numpy/_core/numeric.py#L1564
    /// </remarks>
    [Original("numpy.cross", "numpy/_core/numeric.py", 1564)]
    public static Vector<double> CrossProduct(this Vector<double> a, Vector<double> b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        // ref: numpy/_core/numeric.py#L1679-L1683 — только векторы из трёх чисел
        if (a.Count != 3 || b.Count != 3)
        {
            throw new ValueError(string.Create(CultureInfo.InvariantCulture,
                $"Both input arrays must be (arrays of) 3-dimensional vectors, but they are {a.Count} and {b.Count} dimensional instead."));
        }
        double a0 = a[0], a1 = a[1], a2 = a[2], b0 = b[0], b1 = b[1], b2 = b[2];
        // ref: numpy/_core/numeric.py#L1710-L1718 — по одному действию, каждое со своей проверкой ошибок чисел
        double cp0 = Multiply(a1, b2);
        double tmp = Multiply(a2, b1);
        cp0 = Subtract(cp0, tmp);
        double cp1 = Multiply(a2, b0);
        tmp = Multiply(a0, b2);
        cp1 = Subtract(cp1, tmp);
        double cp2 = Multiply(a0, b1);
        tmp = Multiply(a1, b0);
        cp2 = Subtract(cp2, tmp);
        return new Vector<double>([cp0, cp1, cp2], 0, 3, 1);
    }

    private static double Multiply(double x, double y)
    {
        var flags = FloatFlags.None;
        double z = CheckedMath.Multiply(x, y, ref flags);
        FloatErrors.Check(flags, "multiply");
        return z;
    }

    private static double Subtract(double x, double y)
    {
        var flags = FloatFlags.None;
        double z = CheckedMath.Subtract(x, y, ref flags);
        FloatErrors.Check(flags, "subtract");
        return z;
    }
}
