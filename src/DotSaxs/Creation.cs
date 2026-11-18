using System;
using System.Globalization;
using System.Numerics;
using DotSaxs.Core;

namespace DotSaxs;

/// <summary>
/// Создание векторов и матриц, которому нужен вид чисел: <c>Vector&lt;double&gt;.Range(0, 10, 0.5)</c>,
/// <c>Vector&lt;double&gt;.LinSpace(0, 1, 50)</c>, <c>Matrix&lt;double&gt;.Identity(3)</c> (DECISIONS.md п. 22). Это
/// статические расширения C# 14: вызываются как методы самих типов. Остальное создание — у типов:
/// <c>Vector&lt;T&gt;.Create</c>, <c>Matrix&lt;T&gt;.Create</c>, <c>Matrix&lt;T&gt;.FromDiagonal</c>.
/// </summary>
public static class Creation
{
    /// <summary>Векторы из чисел double.</summary>
    extension(Vector<double>)
    {
        /// <summary>
        /// Числа от <paramref name="start"/> до <paramref name="stop"/> с шагом <paramref name="step"/>, конец не входит —
        /// как <c>np.arange(start, stop, step)</c> и <c>range</c> в Python: <c>Vector&lt;double&gt;.Range(0, 10, 0.5)</c>
        /// даёт 0, 0.5, …, 9.5. Числа — до бита как у NumPy: второе — <c>start + step</c>, остальные —
        /// <c>start + i · (второе − start)</c>. Целые подряд — <c>Enumerable.Range(0, n)</c>.
        /// </summary>
        /// <param name="start">Первое число.</param>
        /// <param name="stop">Граница: само число в вектор не входит.</param>
        /// <param name="step">Шаг; бывает отрицательным.</param>
        /// <returns>Новый вектор; пустой, если от <paramref name="start"/> шагом до <paramref name="stop"/> не дойти.</returns>
        /// <exception cref="ZeroDivisionError">Шаг — ноль, текст как у NumPy: <c>float division by zero</c>.</exception>
        /// <exception cref="ValueError">
        /// Длину не посчитать (NaN) — <c>arange: cannot compute length</c>; бесконечная —
        /// <c>Maximum allowed size exceeded</c>.
        /// </exception>
        /// <exception cref="MemoryError">Чисел больше, чем держит массив .NET.</exception>
        /// <remarks>
        /// Оригинал: numpy.arange — NumPy v2.5.4, numpy/_core/src/multiarray/ctors.c, строка 3291
        ///   https://github.com/numpy/numpy/blob/v2.5.4/numpy/_core/src/multiarray/ctors.c#L3291
        /// Вызывает: заполнение DOUBLE_fill — numpy/_core/src/multiarray/arraytypes.c.src, строка 3803
        /// </remarks>
        [Original("numpy.arange", "numpy/_core/src/multiarray/ctors.c", 3291)]
        public static Vector<double> Range(double start, double stop, double step)
        {
            // ref: numpy/_core/src/multiarray/ctors.c#L3188 — разность границ (числа Python, без проверок)
            double next = stop - start;
            // ref: numpy/_core/src/multiarray/ctors.c#L3213 — деление чисел Python: на ноль — ZeroDivisionError
            if (step == 0)
                throw new ZeroDivisionError("float division by zero");
            double value = next / step;
            long length;
            // ref: numpy/_core/src/multiarray/ctors.c#L3258-L3266 — частное округлилось до нуля, а разность — нет:
            // одно число или ни одного, по знаку частного
            if (value == 0 && next != 0)
                length = double.IsNegative(value) ? 0 : 1;
            else
                length = CeilToLength(value);
            if (length <= 0)
                return new Vector<double>([], 0, 0, 1);
            double[] data = Memory.Allocate<double>(length);
            // ref: numpy/_core/src/multiarray/ctors.c#L3404-L3413 — первое число — start, второе — start + step
            data[0] = start;
            if (data.Length > 1)
                data[1] = start + step;
            // ref: numpy/_core/src/multiarray/arraytypes.c.src#L3803-L3814 — остальные — start + i · (второе − start)
            double delta = data.Length > 1 ? data[1] - start : 0;
            for (int i = 2; i < data.Length; i++)
                data[i] = start + i * delta;
            return new Vector<double>(data, 0, data.Length, 1);
        }

        /// <summary>
        /// <paramref name="count"/> чисел от <paramref name="start"/> до <paramref name="stop"/> через равные промежутки —
        /// как <c>np.linspace(start, stop, num)</c>: <c>Vector&lt;double&gt;.LinSpace(0, 1, 5)</c> даёт 0, 0.25, 0.5,
        /// 0.75, 1. Последнее число — ровно <paramref name="stop"/>; без него — <paramref name="endpoint"/> = false.
        /// </summary>
        /// <param name="start">Первое число.</param>
        /// <param name="stop">Последнее число (или граница, если <paramref name="endpoint"/> = false).</param>
        /// <param name="count">Сколько чисел.</param>
        /// <param name="endpoint">Входит ли <paramref name="stop"/> в вектор.</param>
        /// <returns>Новый вектор.</returns>
        /// <exception cref="ValueError">Чисел меньше нуля — текст как у NumPy: <c>Number of samples, -1, must be non-negative.</c></exception>
        /// <exception cref="FloatingPointError">
        /// Переполнение или недопустимое значение — например, <c>stop</c> бесконечен (DECISIONS.md п. 32).
        /// </exception>
        /// <remarks>
        /// Оригинал: numpy.linspace — NumPy v2.5.4, numpy/_core/function_base.py, строка 28
        ///   https://github.com/numpy/numpy/blob/v2.5.4/numpy/_core/function_base.py#L28
        /// </remarks>
        [Original("numpy.linspace", "numpy/_core/function_base.py", 28)]
        public static Vector<double> LinSpace(double start, double stop, int count, bool endpoint = true)
        {
            // ref: numpy/_core/function_base.py#L123-L127 — чисел не меньше нуля
            if (count < 0)
                throw new ValueError(
                    string.Create(CultureInfo.InvariantCulture, $"Number of samples, {count}, must be non-negative."));
            int div = endpoint ? count - 1 : count;
            var flags = FloatFlags.None;
            // ref: numpy/_core/function_base.py#L141 — разность — функцией NumPy, с проверкой ошибок чисел
            double delta = CheckedMath.Subtract(stop, start, ref flags);
            FloatErrors.Check(flags, "subtract");

            // ref: numpy/_core/function_base.py#L142-L144 — номера 0, 1, …, count − 1
            var y = new double[count];
            flags = FloatFlags.None;
            if (div > 0)
            {
                double step = delta / div;
                if (step == 0)
                {
                    // ref: numpy/_core/function_base.py#L155-L161 — шаг округлился до нуля (субнормальные числа):
                    // номер делится на число шагов и умножается на разность
                    for (int i = 0; i < y.Length; i++)
                        y[i] = CheckedMath.Multiply(i / (double)div, delta, ref flags);
                }
                else
                {
                    // ref: numpy/_core/function_base.py#L162-L163 — номер · шаг
                    for (int i = 0; i < y.Length; i++)
                        y[i] = CheckedMath.Multiply((double)i, step, ref flags);
                }
            }
            else
            {
                // ref: numpy/_core/function_base.py#L166-L171 — 0 или 1 число: шага нет, номер · разность
                for (int i = 0; i < y.Length; i++)
                    y[i] = CheckedMath.Multiply((double)i, delta, ref flags);
            }
            FloatErrors.Check(flags, "multiply");

            // ref: numpy/_core/function_base.py#L173 — плюс start
            flags = FloatFlags.None;
            for (int i = 0; i < y.Length; i++)
                y[i] = CheckedMath.Add(y[i], start, ref flags);
            FloatErrors.Check(flags, "add");

            // ref: numpy/_core/function_base.py#L175-L176 — последнее — ровно stop
            if (endpoint && count > 1)
                y[count - 1] = stop;
            return new Vector<double>(y, 0, y.Length, 1);
        }
    }

    /// <summary>Матрицы из чисел, у которых есть ноль и единица.</summary>
    /// <typeparam name="T">Вид чисел.</typeparam>
    extension<T>(Matrix<T>)
        where T : unmanaged, INumberBase<T>
    {
        /// <summary>
        /// Единичная матрица <paramref name="n"/> × <paramref name="n"/>: единицы на диагонали, нули вокруг — как
        /// <c>np.identity(n)</c> и <c>np.eye(n)</c> (одно имя вместо двух, DECISIONS.md п. 22):
        /// <c>Matrix&lt;double&gt;.Identity(3)</c>.
        /// </summary>
        /// <param name="n">Сторона матрицы.</param>
        /// <returns>Новая матрица.</returns>
        /// <exception cref="ValueError">Сторона меньше нуля — текст как у NumPy: <c>negative dimensions are not allowed</c>.</exception>
        /// <exception cref="MemoryError">Чисел больше, чем держит массив .NET.</exception>
        /// <remarks>
        /// Оригинал: numpy.identity — NumPy v2.5.4, numpy/_core/numeric.py, строка 2180
        ///   https://github.com/numpy/numpy/blob/v2.5.4/numpy/_core/numeric.py#L2180
        /// Вызывает: numpy.eye — numpy/lib/_twodim_base_impl.py, строка 180
        /// </remarks>
        [Original("numpy.identity", "numpy/_core/numeric.py", 2180)]
        public static Matrix<T> Identity(int n)
        {
            // ref: numpy/_core/src/multiarray/ctors.c#L738-L742 — отрицательный размер
            if (n < 0)
                throw new ValueError("negative dimensions are not allowed");
            // ref: numpy/lib/_twodim_base_impl.py#L237-L249 — нули, потом единицы с шагом n + 1
            T[] data = Memory.Allocate<T>((long)n * n);
            for (int i = 0; i < n; i++)
                data[i * (n + 1)] = T.One;
            return new Matrix<T>(data, 0, n, n);
        }

        /// <summary>
        /// Квадратная матрица <paramref name="n"/> × <paramref name="n"/> с единицами на диагонали и под ней, нулями над
        /// ней — как <c>np.tri(n)</c>: <c>Matrix&lt;double&gt;.Tri(3)</c>.
        /// </summary>
        /// <param name="n">Сторона матрицы; меньше нуля — пустая матрица, как у NumPy.</param>
        /// <returns>Новая матрица.</returns>
        /// <exception cref="MemoryError">Чисел больше, чем держит массив .NET.</exception>
        /// <remarks>
        /// Оригинал: numpy.tri — NumPy v2.5.4, numpy/lib/_twodim_base_impl.py, строка 392
        ///   https://github.com/numpy/numpy/blob/v2.5.4/numpy/lib/_twodim_base_impl.py#L392
        /// </remarks>
        [Original("numpy.tri", "numpy/lib/_twodim_base_impl.py", 392)]
        public static Matrix<T> Tri(int n) => TriangleOf<T>(n, n, 0);

        /// <summary>
        /// Матрица <paramref name="rows"/> × <paramref name="columns"/>: единица там, где номер столбца не больше номера
        /// строки плюс <paramref name="offset"/>, остальное — нули, как <c>np.tri(rows, columns, k)</c>.
        /// </summary>
        /// <param name="rows">Сколько строк; меньше нуля — ни одной, как у NumPy.</param>
        /// <param name="columns">Сколько столбцов; меньше нуля — ни одного.</param>
        /// <param name="offset">На сколько граница выше главной диагонали (ниже — со знаком минус).</param>
        /// <returns>Новая матрица.</returns>
        /// <exception cref="MemoryError">Чисел больше, чем держит массив .NET.</exception>
        /// <remarks>Оригинал: numpy.tri — NumPy v2.5.4, numpy/lib/_twodim_base_impl.py, строка 392</remarks>
        [Original("numpy.tri", "numpy/lib/_twodim_base_impl.py", 392)]
        public static Matrix<T> Tri(int rows, int columns, int offset) => TriangleOf<T>(rows, columns, offset);
    }

    /// <summary>Матрицы из чисел double.</summary>
    extension(Matrix<double>)
    {
        /// <summary>
        /// Матрица Вандермонда — как <c>np.vander(x)</c>: в строке i — степени x[i] по убыванию, последний столбец —
        /// единицы: <c>Matrix&lt;double&gt;.Vandermonde(x)</c> — квадратная.
        /// </summary>
        /// <param name="x">Числа, по одному на строку.</param>
        /// <returns>Новая матрица.</returns>
        /// <exception cref="FloatingPointError">Переполнение степени (DECISIONS.md п. 32).</exception>
        /// <remarks>
        /// Оригинал: numpy.vander — NumPy v2.5.4, numpy/lib/_twodim_base_impl.py, строка 588
        ///   https://github.com/numpy/numpy/blob/v2.5.4/numpy/lib/_twodim_base_impl.py#L588
        /// </remarks>
        [Original("numpy.vander", "numpy/lib/_twodim_base_impl.py", 588)]
        public static Matrix<double> Vandermonde(Vector<double> x)
        {
            ArgumentNullException.ThrowIfNull(x);
            return VandermondeOf(x, x.Count, increasing: false);
        }

        /// <summary>
        /// Матрица Вандермонда из <paramref name="columns"/> столбцов — как <c>np.vander(x, N, increasing)</c>: в строке i —
        /// степени x[i] от <paramref name="columns"/> − 1 до 0, или от 0 вверх, если <paramref name="increasing"/>.
        /// Степени — умножением подряд, как у NumPy: x, x·x, (x·x)·x…
        /// </summary>
        /// <param name="x">Числа, по одному на строку.</param>
        /// <param name="columns">Сколько столбцов (степеней).</param>
        /// <param name="increasing">Степени по возрастанию.</param>
        /// <returns>Новая матрица.</returns>
        /// <exception cref="ValueError">Столбцов меньше нуля — текст как у NumPy: <c>negative dimensions are not allowed</c>.</exception>
        /// <exception cref="FloatingPointError">Переполнение степени (DECISIONS.md п. 32).</exception>
        /// <remarks>Оригинал: numpy.vander — NumPy v2.5.4, numpy/lib/_twodim_base_impl.py, строка 588</remarks>
        [Original("numpy.vander", "numpy/lib/_twodim_base_impl.py", 588)]
        public static Matrix<double> Vandermonde(Vector<double> x, int columns, bool increasing = false)
        {
            ArgumentNullException.ThrowIfNull(x);
            return VandermondeOf(x, columns, increasing);
        }
    }

    private static Matrix<T> TriangleOf<T>(int rows, int columns, int offset)
        where T : unmanaged, INumberBase<T>
    {
        // ref: numpy/lib/_twodim_base_impl.py#L456-L457 — greater_equal.outer(arange(N), arange(-k, M - k)): единица,
        // где строка ≥ столбец − k; arange отрицательной длины — пусто
        rows = Math.Max(rows, 0);
        columns = Math.Max(columns, 0);
        T[] data = Memory.Allocate<T>((long)rows * columns);
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < columns; j++)
            {
                if (i >= (long)j - offset)
                    data[(i * columns) + j] = T.One;
            }
        }
        return new Matrix<T>(data, 0, rows, columns);
    }

    private static Matrix<double> VandermondeOf(Vector<double> x, int columns, bool increasing)
    {
        // ref: numpy/lib/_twodim_base_impl.py#L666 — empty((len(x), N)): отрицательный размер
        if (columns < 0)
            throw new ValueError("negative dimensions are not allowed");
        int rows = x.Count;
        double[] data = Memory.Allocate<double>((long)rows * columns);
        var flags = FloatFlags.None;
        for (int i = 0; i < rows; i++)
        {
            // ref: numpy/lib/_twodim_base_impl.py#L667-L672 — нулевая степень — 1, первая — x, дальше
            // multiply.accumulate: каждая следующая — предыдущая · x
            double power = 1;
            for (int p = 0; p < columns; p++)
            {
                if (p == 1)
                    power = x[i];
                else if (p > 1)
                    power = CheckedMath.Multiply(power, x[i], ref flags);
                data[(i * columns) + (increasing ? p : columns - 1 - p)] = power;
            }
        }
        FloatErrors.Check(flags, "accumulate");
        return new Matrix<double>(data, 0, rows, columns);
    }

    /// <summary>
    /// Длина <c>np.arange</c> — частное, округлённое вверх, с проверкой: NaN — <c>arange: cannot compute length</c>,
    /// за пределами целого 64 бит — <c>Maximum allowed size exceeded</c> (у NumPy это OverflowError, который он
    /// заменяет на ValueError).
    /// </summary>
    private static long CeilToLength(double value)
    {
        // ref: numpy/_core/src/multiarray/ctors.c#L3069-L3087 — _arange_safe_ceil_to_intp
        double ceiling = Math.Ceiling(value);
        if (double.IsNaN(ceiling))
            throw new ValueError("arange: cannot compute length");
        if (!(long.MinValue <= ceiling && ceiling <= long.MaxValue))
            // ref: numpy/_core/src/multiarray/ctors.c#L3380-L3384 — OverflowError становится ValueError
            throw new ValueError("Maximum allowed size exceeded");
        // 2⁶³ проходит проверку (long.MaxValue в double округляется до 2⁶³), но в long не помещается: чисел столько
        // всё равно не выделить.
        return ceiling >= 9.2233720368547758E18 ? long.MaxValue : (long)ceiling;
    }
}
