using System.Numerics;

namespace DotSaxs.Core;

/// <summary>Сложение двух чисел внутри суммы: обычное или с учётом ошибок чисел.</summary>
internal interface IAddition<T>
    where T : IFloatingPointIeee754<T>
{
    /// <summary>x + y.</summary>
    T Add(T x, T y);
}

/// <summary>Обычное сложение — так NumPy складывает всегда; ошибки чисел отмечает процессор.</summary>
internal readonly struct PlainAddition<T> : IAddition<T>
    where T : IFloatingPointIeee754<T>
{
    /// <inheritdoc/>
    public T Add(T x, T y) => x + y;
}

/// <summary>
/// Сложение, которое запоминает ошибки чисел так же, как их отмечает процессор: из двух конечных чисел получилась
/// бесконечность — переполнение; из двух чисел без nan получился nan (inf − inf) — недопустимое значение. Флагов
/// процессора .NET не читает (DECISIONS.md п. 14), поэтому сумму с ошибкой пересчитывают в том же порядке так.
/// </summary>
internal struct CheckedAddition<T> : IAddition<T>
    where T : IFloatingPointIeee754<T>
{
    /// <summary>Какие ошибки уже случились.</summary>
    public FloatFlags Flags;

    /// <inheritdoc/>
    public T Add(T x, T y)
    {
        T z = x + y;
        if (!T.IsFinite(z))
        {
            if (T.IsNaN(z))
            {
                if (!T.IsNaN(x) && !T.IsNaN(y))
                    Flags |= FloatFlags.Invalid;
            }
            else if (T.IsFinite(x) && T.IsFinite(y))
            {
                Flags |= FloatFlags.Over;
            }
        }
        return z;
    }
}

/// <summary>
/// Попарное суммирование NumPy: ошибка округления растёт как log n, а не как n. Порядок сложений — ровно как у NumPy,
/// поэтому сумма совпадает до бита. Числа берутся из <c>a[start]</c>, <c>a[start + stride]</c>… — так же суммируется
/// и строка, и столбец матрицы.
/// </summary>
internal static class Pairwise
{
    // ref: numpy/_core/src/umath/loops_utils.h.src#L63 — PW_BLOCKSIZE: до 128 чисел — один блок с 8 накопителями
    private const int BlockSize = 128;

    // ref: numpy/_core/src/umath/loops_utils.h.src#L81 — @TYPE@_pairwise_sum для float и double
    internal static T Sum<T, TAddition>(T[] a, int start, int n, int stride, ref TAddition addition)
        where T : IFloatingPointIeee754<T>
        where TAddition : struct, IAddition<T>
    {
        // ref: numpy/_core/src/umath/loops_utils.h.src#L83 — меньше 8 чисел — подряд
        if (n < 8)
        {
            // ref: numpy/_core/src/umath/loops_utils.h.src#L89 — начинаем с −0: сумма одних −0 остаётся −0
            T res = T.NegativeZero;
            for (int i = 0; i < n; i++)
                res = addition.Add(res, a[start + i * stride]);
            return res;
        }
        // ref: numpy/_core/src/umath/loops_utils.h.src#L96 — до 128 чисел — 8 накопителей
        if (n <= BlockSize)
        {
            // ref: numpy/_core/src/umath/loops_utils.h.src#L105-L112 — накопители начинаются с первых 8 чисел
            T r0 = a[start];
            T r1 = a[start + stride];
            T r2 = a[start + 2 * stride];
            T r3 = a[start + 3 * stride];
            T r4 = a[start + 4 * stride];
            T r5 = a[start + 5 * stride];
            T r6 = a[start + 6 * stride];
            T r7 = a[start + 7 * stride];
            int i;
            // ref: numpy/_core/src/umath/loops_utils.h.src#L114-L125 — по 8 чисел, каждое в свой накопитель
            for (i = 8; i < n - n % 8; i += 8)
            {
                int p = start + i * stride;
                r0 = addition.Add(r0, a[p]);
                r1 = addition.Add(r1, a[p + stride]);
                r2 = addition.Add(r2, a[p + 2 * stride]);
                r3 = addition.Add(r3, a[p + 3 * stride]);
                r4 = addition.Add(r4, a[p + 4 * stride]);
                r5 = addition.Add(r5, a[p + 5 * stride]);
                r6 = addition.Add(r6, a[p + 6 * stride]);
                r7 = addition.Add(r7, a[p + 7 * stride]);
            }
            // ref: numpy/_core/src/umath/loops_utils.h.src#L128-L129 — накопители складываются парами
            T res = addition.Add(
                addition.Add(addition.Add(r0, r1), addition.Add(r2, r3)),
                addition.Add(addition.Add(r4, r5), addition.Add(r6, r7)));
            // ref: numpy/_core/src/umath/loops_utils.h.src#L132-L134 — остаток, меньше 8 чисел, — подряд
            for (; i < n; i++)
                res = addition.Add(res, a[start + i * stride]);
            return res;
        }
        // ref: numpy/_core/src/umath/loops_utils.h.src#L139-L143 — пополам; первая половина кратна 8
        int n2 = n / 2;
        n2 -= n2 % 8;
        T first = Sum(a, start, n2, stride, ref addition);
        T second = Sum(a, start + n2 * stride, n - n2, stride, ref addition);
        return addition.Add(first, second);
    }
}
