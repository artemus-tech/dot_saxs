using System.Numerics;

namespace DotSaxs.Core;

/// <summary>
/// Действия над числами, которые запоминают ошибки чисел так же, как их отмечает процессор, — NumPy читает эти флаги
/// после каждой функции над массивом (DECISIONS.md п. 14, 32). Из конечных чисел получилась бесконечность —
/// переполнение; из чисел без NaN получился NaN (∞ − ∞, 0 · ∞) — недопустимое значение.
/// </summary>
internal static class CheckedMath
{
    /// <summary>x + y.</summary>
    internal static T Add<T>(T x, T y, ref FloatFlags flags)
        where T : IFloatingPointIeee754<T> => Mark(x, y, x + y, ref flags);

    /// <summary>x − y.</summary>
    internal static T Subtract<T>(T x, T y, ref FloatFlags flags)
        where T : IFloatingPointIeee754<T> => Mark(x, y, x - y, ref flags);

    /// <summary>x · y.</summary>
    internal static T Multiply<T>(T x, T y, ref FloatFlags flags)
        where T : IFloatingPointIeee754<T> => Mark(x, y, x * y, ref flags);

    /// <summary>x / y: конечное не нулевое число делится на ноль — деление на ноль; 0 / 0 и ∞ / ∞ — недопустимое значение.</summary>
    internal static T Divide<T>(T x, T y, ref FloatFlags flags)
        where T : IFloatingPointIeee754<T>
    {
        T z = x / y;
        if (T.IsZero(y) && T.IsFinite(x) && !T.IsZero(x))
        {
            flags |= FloatFlags.Divide;
            return z;
        }
        return Mark(x, y, z, ref flags);
    }

    private static T Mark<T>(T x, T y, T z, ref FloatFlags flags)
        where T : IFloatingPointIeee754<T>
    {
        if (T.IsFinite(z))
            return z;
        if (T.IsNaN(z))
        {
            if (!T.IsNaN(x) && !T.IsNaN(y))
                flags |= FloatFlags.Invalid;
        }
        else if (T.IsFinite(x) && T.IsFinite(y))
        {
            flags |= FloatFlags.Over;
        }
        return z;
    }
}
