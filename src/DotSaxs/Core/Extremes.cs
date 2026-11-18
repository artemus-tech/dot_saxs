using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace DotSaxs.Core;

/// <summary>
/// Максимум и минимум дробных чисел так, как их считает NumPy: <c>np.maximum.reduce</c> и <c>np.minimum.reduce</c>
/// (numpy/_core/src/umath/loops_minmax.dispatch.c.src). Значение у всех способов одно, но из равных 0 и −0 NumPy
/// возвращает то, что оказалось в нужной полосе вектора процессора, поэтому порядок сравнений повторён точно: для чисел
/// подряд — векторный код AVX2 (эталоны считаются с ним, DECISIONS.md п. 4), для чисел с шагом — восемь накопителей.
/// NaN в векторе — результат NaN.
/// </summary>
internal static class Extremes
{
    /// <summary>
    /// Максимум (<paramref name="max"/> = true) или минимум вида: <paramref name="count"/> чисел из
    /// <paramref name="data"/>, начиная с <paramref name="start"/>, с шагом <paramref name="stride"/>; чисел хотя бы одно.
    /// </summary>
    internal static T Reduce<T>(T[] data, int start, int count, int stride, bool max)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        // ref: numpy/_core/src/umath/reduction.c#L76-L145 — у свёртки без единицы первое число копируется в результат,
        // ref: numpy/_core/src/umath/ufunc_object.c#L2471-L2485 — а внутренний цикл получает остальные
        T first = data[start];
        int length = count - 1, p = start + stride;
        if (length < 1)
            return first;
        // ref: numpy/_core/src/umath/loops_minmax.dispatch.c.src#L327-L333 — числа подряд — векторный код
        return stride == 1 ? Simd(data, p, length, first, max) : Unrolled(data, p, stride, length, first, max);
    }

    // ref: numpy/_core/src/umath/loops_minmax.dispatch.c.src#L116-L153 — simd_reduce_c_max_f64: накопитель —
    // вектор из 256 бит (4 double или 8 float), за раз — 8 векторов, потом по вектору, потом свёртка полос и остаток
    private static T Simd<T>(T[] a, int p, int length, T first, bool max)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        int lanes = 32 / Unsafe.SizeOf<T>();
        Span<T> acc = stackalloc T[lanes];
        acc.Fill(first);
        int wide = lanes * 8;
        for (; length >= wide; length -= wide, p += wide)
        {
            for (int k = 0; k < lanes; k++)
            {
                T r01 = PropagateNaN(a[p + k], a[p + lanes + k], max);
                T r23 = PropagateNaN(a[p + (2 * lanes) + k], a[p + (3 * lanes) + k], max);
                T r45 = PropagateNaN(a[p + (4 * lanes) + k], a[p + (5 * lanes) + k], max);
                T r67 = PropagateNaN(a[p + (6 * lanes) + k], a[p + (7 * lanes) + k], max);
                acc[k] = PropagateNaN(acc[k], PropagateNaN(PropagateNaN(r01, r23, max), PropagateNaN(r45, r67, max), max), max);
            }
        }
        for (; length >= lanes; length -= lanes, p += lanes)
        {
            for (int k = 0; k < lanes; k++)
                acc[k] = PropagateNaN(acc[k], a[p + k], max);
        }

        // ref: numpy/_core/src/common/simd/avx2/math.h#L158-L207 — npyv_reduce_maxn_f64: NaN в любой полосе — NaN,
        // иначе полосы сворачиваются пополам: старшая половина с младшей, пока не останется одна
        T r = acc[0];
        bool nan = false;
        foreach (T x in acc)
            nan |= T.IsNaN(x);
        if (nan)
        {
            r = T.NaN;
        }
        else
        {
            for (int half = lanes / 2; half >= 1; half /= 2)
            {
                for (int k = 0; k < half; k++)
                    acc[k] = Choose(acc[k], acc[k + half], max);
            }
            r = acc[0];
        }
        // ref: numpy/_core/src/umath/loops_minmax.dispatch.c.src#L148-L151 — остаток — по одному числу
        for (; length > 0; length--, p++)
            r = PropagateNaN(r, a[p], max);
        return r;
    }

    // ref: numpy/_core/src/umath/loops_minmax.dispatch.c.src#L363-L397 — числа с шагом: восемь накопителей, потом
    // они сворачиваются парами, потом остаток
    private static T Unrolled<T>(T[] a, int p, int stride, int length, T first, bool max)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        T r = first;
        int i = 0;
        if (length >= 8)
        {
            Span<T> m = stackalloc T[8];
            for (int k = 0; k < 8; k++)
                m[k] = a[p + (k * stride)];
            for (i = 8; i + 8 <= length; i += 8)
            {
                for (int k = 0; k < 8; k++)
                    m[k] = PropagateNaN(m[k], a[p + ((i + k) * stride)], max);
            }
            T m0 = PropagateNaN(m[0], m[1], max), m2 = PropagateNaN(m[2], m[3], max);
            T m4 = PropagateNaN(m[4], m[5], max), m6 = PropagateNaN(m[6], m[7], max);
            m0 = PropagateNaN(m0, m2, max);
            m4 = PropagateNaN(m4, m6, max);
            r = PropagateNaN(r, PropagateNaN(m0, m4, max), max);
        }
        for (; i < length; i++)
            r = PropagateNaN(r, a[p + (i * stride)], max);
        return r;
    }

    /// <summary>
    /// Как команды процессора <c>maxpd</c> и <c>minpd</c>: x больше (меньше) y — x, иначе y. Из равных 0 и −0 — y.
    /// </summary>
    private static T Choose<T>(T x, T y, bool max)
        where T : IFloatingPointIeee754<T> => max ? (x > y ? x : y) : (x < y ? x : y);

    // ref: numpy/_core/src/common/simd/avx2/math.h#L66-L71 — npyv_maxn_f64: NaN в x — x, иначе maxpd (NaN в y — y)
    private static T PropagateNaN<T>(T x, T y, bool max)
        where T : IFloatingPointIeee754<T> => T.IsNaN(x) ? x : Choose(x, y, max);

    /// <summary>
    /// Номер максимума (<paramref name="max"/> = true) или минимума — как <c>np.argmax</c> и <c>np.argmin</c>: первый
    /// из равных; NaN больше всех, поэтому номер первого NaN.
    /// </summary>
    internal static int IndexOf<T>(T[] data, int start, int count, int stride, bool max)
        where T : INumber<T>
    {
        // ref: numpy/_core/src/multiarray/argfunc.dispatch.c.src#L244-L258 — заменяет, только если число больше (у
        // минимума — меньше) или NaN; на NaN останавливается
        int best = 0;
        T value = data[start];
        for (int i = 1; i < count && !T.IsNaN(value); i++)
        {
            T x = data[start + (i * stride)];
            if (T.IsNaN(x) || (max ? x > value : x < value))
            {
                best = i;
                value = x;
            }
        }
        return best;
    }
}
