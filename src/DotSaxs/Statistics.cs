using System;
using System.Linq;
using System.Numerics;
using DotSaxs.Core;

namespace DotSaxs;

/// <summary>
/// Своя статистика вектора — как в NumPy, до бита (DECISIONS.md п. 24): <c>v.Max()</c>, <c>v.Mean()</c>,
/// <c>v.StandardDeviation()</c>, <c>v.Median()</c>, <c>v.Sort()</c>… У LINQ результат бывает другим: <c>Max</c> пропускает
/// NaN, сумма подряд теряет последние биты, NaN при сортировке у него первым. C# сам выбирает эти методы вместо LINQ.
/// </summary>
/// <remarks>
/// У матрицы своей статистики нет (DECISIONS.md п. 22, 34): всех чисел — <c>m.Flatten().Mean()</c>, по строкам —
/// <c>m.GetRows().Select(r => r.Mean())</c>. Ошибки чисел (переполнение, ∞ − ∞, деление на ноль) по умолчанию —
/// исключение <see cref="FloatingPointError"/> с текстом NumPy, молча — <c>using (Np.ErrState(all: "ignore"))</c> (п. 32).
/// Целые при переполнении тоже бросают исключение; NumPy молча идёт по кругу.
/// </remarks>
public static class Statistics
{
    // Сколько чисел NumPy переводит в double за раз, когда считает сумму целых (размер буфера итератора).
    private const int BufferSize = ReductionPlan.BufferSize;

    /// <summary>Наибольшее число — как <c>np.max(v)</c>. NaN в векторе — результат NaN (LINQ <c>Max</c> его пропускает).</summary>
    /// <param name="v">Вектор, хотя бы одно число.</param>
    /// <returns>Наибольшее число.</returns>
    /// <exception cref="ValueError">Вектор пустой — текст как у NumPy.</exception>
    /// <remarks>
    /// Оригинал: numpy.max — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 3051
    ///   https://github.com/numpy/numpy/blob/v2.5.4/numpy/_core/fromnumeric.py#L3051
    /// Вызывает: np.maximum.reduce — numpy/_core/src/umath/loops_minmax.dispatch.c.src, строка 116
    /// </remarks>
    [Original("numpy.max", "numpy/_core/fromnumeric.py", 3051)]
    public static double Max(this Vector<double> v) => FloatExtreme(v, max: true);

    /// <summary>Наибольшее число — как <c>np.max(v)</c> у float32. NaN в векторе — результат NaN.</summary>
    /// <param name="v">Вектор, хотя бы одно число.</param>
    /// <returns>Наибольшее число.</returns>
    /// <exception cref="ValueError">Вектор пустой — текст как у NumPy.</exception>
    /// <remarks>Оригинал: numpy.max — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 3051</remarks>
    [Original("numpy.max", "numpy/_core/fromnumeric.py", 3051)]
    public static float Max(this Vector<float> v) => FloatExtreme(v, max: true);

    /// <summary>Наибольшее число — как <c>np.max(v)</c>.</summary>
    /// <param name="v">Вектор, хотя бы одно число.</param>
    /// <returns>Наибольшее число.</returns>
    /// <exception cref="ValueError">Вектор пустой — текст как у NumPy.</exception>
    /// <remarks>Оригинал: numpy.max — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 3051</remarks>
    [Original("numpy.max", "numpy/_core/fromnumeric.py", 3051)]
    public static long Max(this Vector<long> v) => IntegerExtreme(v, max: true);

    /// <summary>Наибольшее число — как <c>np.max(v)</c>.</summary>
    /// <param name="v">Вектор, хотя бы одно число.</param>
    /// <returns>Наибольшее число.</returns>
    /// <exception cref="ValueError">Вектор пустой — текст как у NumPy.</exception>
    /// <remarks>Оригинал: numpy.max — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 3051</remarks>
    [Original("numpy.max", "numpy/_core/fromnumeric.py", 3051)]
    public static int Max(this Vector<int> v) => IntegerExtreme(v, max: true);

    /// <summary>Наименьшее число — как <c>np.min(v)</c>. NaN в векторе — результат NaN.</summary>
    /// <param name="v">Вектор, хотя бы одно число.</param>
    /// <returns>Наименьшее число.</returns>
    /// <exception cref="ValueError">Вектор пустой — текст как у NumPy.</exception>
    /// <remarks>
    /// Оригинал: numpy.min — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 3188
    ///   https://github.com/numpy/numpy/blob/v2.5.4/numpy/_core/fromnumeric.py#L3188
    /// Вызывает: np.minimum.reduce — numpy/_core/src/umath/loops_minmax.dispatch.c.src, строка 116
    /// </remarks>
    [Original("numpy.min", "numpy/_core/fromnumeric.py", 3188)]
    public static double Min(this Vector<double> v) => FloatExtreme(v, max: false);

    /// <summary>Наименьшее число — как <c>np.min(v)</c> у float32. NaN в векторе — результат NaN.</summary>
    /// <param name="v">Вектор, хотя бы одно число.</param>
    /// <returns>Наименьшее число.</returns>
    /// <exception cref="ValueError">Вектор пустой — текст как у NumPy.</exception>
    /// <remarks>Оригинал: numpy.min — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 3188</remarks>
    [Original("numpy.min", "numpy/_core/fromnumeric.py", 3188)]
    public static float Min(this Vector<float> v) => FloatExtreme(v, max: false);

    /// <summary>Наименьшее число — как <c>np.min(v)</c>.</summary>
    /// <param name="v">Вектор, хотя бы одно число.</param>
    /// <returns>Наименьшее число.</returns>
    /// <exception cref="ValueError">Вектор пустой — текст как у NumPy.</exception>
    /// <remarks>Оригинал: numpy.min — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 3188</remarks>
    [Original("numpy.min", "numpy/_core/fromnumeric.py", 3188)]
    public static long Min(this Vector<long> v) => IntegerExtreme(v, max: false);

    /// <summary>Наименьшее число — как <c>np.min(v)</c>.</summary>
    /// <param name="v">Вектор, хотя бы одно число.</param>
    /// <returns>Наименьшее число.</returns>
    /// <exception cref="ValueError">Вектор пустой — текст как у NumPy.</exception>
    /// <remarks>Оригинал: numpy.min — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 3188</remarks>
    [Original("numpy.min", "numpy/_core/fromnumeric.py", 3188)]
    public static int Min(this Vector<int> v) => IntegerExtreme(v, max: false);

    /// <summary>
    /// Номер наибольшего числа — как <c>np.argmax(v)</c>: из равных — первый; NaN считается наибольшим, поэтому номер
    /// первого NaN. У матрицы — <c>m.Flatten().IndexOfMax()</c>.
    /// </summary>
    /// <param name="v">Вектор, хотя бы одно число.</param>
    /// <returns>Номер, с нуля.</returns>
    /// <exception cref="ValueError">Вектор пустой — текст как у NumPy.</exception>
    /// <remarks>
    /// Оригинал: numpy.argmax — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 1260
    ///   https://github.com/numpy/numpy/blob/v2.5.4/numpy/_core/fromnumeric.py#L1260
    /// Вызывает: argmax — numpy/_core/src/multiarray/argfunc.dispatch.c.src, строка 128
    /// </remarks>
    [Original("numpy.argmax", "numpy/_core/fromnumeric.py", 1260)]
    public static int IndexOfMax(this Vector<double> v) => IndexOf(v, max: true);

    /// <summary>Номер наибольшего числа — как <c>np.argmax(v)</c>: из равных — первый, NaN — наибольший.</summary>
    /// <param name="v">Вектор, хотя бы одно число.</param>
    /// <returns>Номер, с нуля.</returns>
    /// <exception cref="ValueError">Вектор пустой — текст как у NumPy.</exception>
    /// <remarks>Оригинал: numpy.argmax — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 1260</remarks>
    [Original("numpy.argmax", "numpy/_core/fromnumeric.py", 1260)]
    public static int IndexOfMax(this Vector<float> v) => IndexOf(v, max: true);

    /// <summary>Номер наибольшего числа — как <c>np.argmax(v)</c>: из равных — первый.</summary>
    /// <param name="v">Вектор, хотя бы одно число.</param>
    /// <returns>Номер, с нуля.</returns>
    /// <exception cref="ValueError">Вектор пустой — текст как у NumPy.</exception>
    /// <remarks>Оригинал: numpy.argmax — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 1260</remarks>
    [Original("numpy.argmax", "numpy/_core/fromnumeric.py", 1260)]
    public static int IndexOfMax(this Vector<long> v) => IndexOf(v, max: true);

    /// <summary>Номер наибольшего числа — как <c>np.argmax(v)</c>: из равных — первый.</summary>
    /// <param name="v">Вектор, хотя бы одно число.</param>
    /// <returns>Номер, с нуля.</returns>
    /// <exception cref="ValueError">Вектор пустой — текст как у NumPy.</exception>
    /// <remarks>Оригинал: numpy.argmax — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 1260</remarks>
    [Original("numpy.argmax", "numpy/_core/fromnumeric.py", 1260)]
    public static int IndexOfMax(this Vector<int> v) => IndexOf(v, max: true);

    /// <summary>
    /// Номер наименьшего числа — как <c>np.argmin(v)</c>: из равных — первый; NaN считается наименьшим, поэтому номер
    /// первого NaN. У матрицы — <c>m.Flatten().IndexOfMin()</c>.
    /// </summary>
    /// <param name="v">Вектор, хотя бы одно число.</param>
    /// <returns>Номер, с нуля.</returns>
    /// <exception cref="ValueError">Вектор пустой — текст как у NumPy.</exception>
    /// <remarks>
    /// Оригинал: numpy.argmin — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 1360
    ///   https://github.com/numpy/numpy/blob/v2.5.4/numpy/_core/fromnumeric.py#L1360
    /// </remarks>
    [Original("numpy.argmin", "numpy/_core/fromnumeric.py", 1360)]
    public static int IndexOfMin(this Vector<double> v) => IndexOf(v, max: false);

    /// <summary>Номер наименьшего числа — как <c>np.argmin(v)</c>: из равных — первый, NaN — наименьший.</summary>
    /// <param name="v">Вектор, хотя бы одно число.</param>
    /// <returns>Номер, с нуля.</returns>
    /// <exception cref="ValueError">Вектор пустой — текст как у NumPy.</exception>
    /// <remarks>Оригинал: numpy.argmin — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 1360</remarks>
    [Original("numpy.argmin", "numpy/_core/fromnumeric.py", 1360)]
    public static int IndexOfMin(this Vector<float> v) => IndexOf(v, max: false);

    /// <summary>Номер наименьшего числа — как <c>np.argmin(v)</c>: из равных — первый.</summary>
    /// <param name="v">Вектор, хотя бы одно число.</param>
    /// <returns>Номер, с нуля.</returns>
    /// <exception cref="ValueError">Вектор пустой — текст как у NumPy.</exception>
    /// <remarks>Оригинал: numpy.argmin — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 1360</remarks>
    [Original("numpy.argmin", "numpy/_core/fromnumeric.py", 1360)]
    public static int IndexOfMin(this Vector<long> v) => IndexOf(v, max: false);

    /// <summary>Номер наименьшего числа — как <c>np.argmin(v)</c>: из равных — первый.</summary>
    /// <param name="v">Вектор, хотя бы одно число.</param>
    /// <returns>Номер, с нуля.</returns>
    /// <exception cref="ValueError">Вектор пустой — текст как у NumPy.</exception>
    /// <remarks>Оригинал: numpy.argmin — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 1360</remarks>
    [Original("numpy.argmin", "numpy/_core/fromnumeric.py", 1360)]
    public static int IndexOfMin(this Vector<int> v) => IndexOf(v, max: false);

    /// <summary>
    /// Среднее — как <c>np.mean(v)</c>: своя сумма (<see cref="Sums"/>), делённая на число чисел. У пустого вектора
    /// 0 / 0 — по умолчанию исключение, с <c>Np.ErrState(invalid: "ignore")</c> — NaN.
    /// </summary>
    /// <param name="v">Вектор.</param>
    /// <returns>Среднее.</returns>
    /// <exception cref="FloatingPointError">Переполнение, ∞ − ∞ или пустой вектор (DECISIONS.md п. 32).</exception>
    /// <remarks>
    /// Оригинал: numpy.mean — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 3738
    ///   https://github.com/numpy/numpy/blob/v2.5.4/numpy/_core/fromnumeric.py#L3738
    /// Вызывает: _mean — numpy/_core/_methods.py, строка 115
    /// </remarks>
    [Original("numpy.mean", "numpy/_core/fromnumeric.py", 3738)]
    public static double Mean(this Vector<double> v)
    {
        ArgumentNullException.ThrowIfNull(v);
        // ref: numpy/_core/_methods.py#L132 — сумма — np.add.reduce
        double sum = v.Sum();
        // ref: numpy/_core/_methods.py#L138-L142 — делится на число чисел как скаляр NumPy
        return ScalarDivide(sum, v.Count);
    }

    /// <summary>Среднее — как <c>np.mean(v)</c> у float32: сумма во float, частное округляется до float.</summary>
    /// <param name="v">Вектор.</param>
    /// <returns>Среднее.</returns>
    /// <exception cref="FloatingPointError">Переполнение, ∞ − ∞ или пустой вектор (DECISIONS.md п. 32).</exception>
    /// <remarks>Оригинал: numpy.mean — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 3738</remarks>
    [Original("numpy.mean", "numpy/_core/fromnumeric.py", 3738)]
    public static float Mean(this Vector<float> v)
    {
        ArgumentNullException.ThrowIfNull(v);
        // ref: numpy/_core/_methods.py#L138-L142 — float32 делится на целое NumPy в double и округляется до float
        return (float)ScalarDivide(v.Sum(), v.Count);
    }

    /// <summary>Среднее целых — как <c>np.mean(v)</c>: NumPy складывает их в double, результат — double.</summary>
    /// <param name="v">Вектор.</param>
    /// <returns>Среднее.</returns>
    /// <exception cref="FloatingPointError">Пустой вектор: 0 / 0 (DECISIONS.md п. 32).</exception>
    /// <remarks>Оригинал: numpy.mean — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 3738</remarks>
    [Original("numpy.mean", "numpy/_core/fromnumeric.py", 3738)]
    public static double Mean(this Vector<long> v) => ScalarDivide(SumAsDouble(v), Count(v));

    /// <summary>Среднее целых — как <c>np.mean(v)</c>: NumPy складывает их в double, результат — double.</summary>
    /// <param name="v">Вектор.</param>
    /// <returns>Среднее.</returns>
    /// <exception cref="FloatingPointError">Пустой вектор: 0 / 0 (DECISIONS.md п. 32).</exception>
    /// <remarks>Оригинал: numpy.mean — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 3738</remarks>
    [Original("numpy.mean", "numpy/_core/fromnumeric.py", 3738)]
    public static double Mean(this Vector<int> v) => ScalarDivide(SumAsDouble(v), Count(v));

    /// <summary>
    /// Дисперсия — как <c>np.var(v, ddof=…)</c>: сумма квадратов отклонений от среднего, делённая на число чисел минус
    /// <paramref name="deltaDegreesOfFreedom"/>. По умолчанию — дисперсия всей совокупности (делится на n), как в NumPy;
    /// выборочная — <c>v.Variance(deltaDegreesOfFreedom: 1)</c> (делится на n − 1).
    /// </summary>
    /// <param name="v">Вектор.</param>
    /// <param name="deltaDegreesOfFreedom">Сколько вычесть из числа чисел в знаменателе (<c>ddof</c> в NumPy).</param>
    /// <returns>Дисперсия.</returns>
    /// <exception cref="FloatingPointError">Переполнение, ∞ − ∞ или знаменатель не больше нуля (DECISIONS.md п. 32).</exception>
    /// <remarks>
    /// Оригинал: numpy.var — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 4076
    ///   https://github.com/numpy/numpy/blob/v2.5.4/numpy/_core/fromnumeric.py#L4076
    /// Вызывает: _var — numpy/_core/_methods.py, строка 148
    /// </remarks>
    [Original("numpy.var", "numpy/_core/fromnumeric.py", 4076)]
    public static double Variance(this Vector<double> v, int deltaDegreesOfFreedom = 0) =>
        FloatVariance(v, deltaDegreesOfFreedom);

    /// <summary>Дисперсия — как <c>np.var(v, ddof=…)</c> у float32: считается во float, как у NumPy.</summary>
    /// <param name="v">Вектор.</param>
    /// <param name="deltaDegreesOfFreedom">Сколько вычесть из числа чисел в знаменателе (<c>ddof</c> в NumPy).</param>
    /// <returns>Дисперсия.</returns>
    /// <exception cref="FloatingPointError">Переполнение, ∞ − ∞ или знаменатель не больше нуля (DECISIONS.md п. 32).</exception>
    /// <remarks>Оригинал: numpy.var — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 4076</remarks>
    [Original("numpy.var", "numpy/_core/fromnumeric.py", 4076)]
    public static float Variance(this Vector<float> v, int deltaDegreesOfFreedom = 0) =>
        FloatVariance(v, deltaDegreesOfFreedom);

    /// <summary>Дисперсия целых — как <c>np.var(v, ddof=…)</c>: NumPy считает её в double.</summary>
    /// <param name="v">Вектор.</param>
    /// <param name="deltaDegreesOfFreedom">Сколько вычесть из числа чисел в знаменателе (<c>ddof</c> в NumPy).</param>
    /// <returns>Дисперсия.</returns>
    /// <exception cref="FloatingPointError">Знаменатель не больше нуля (DECISIONS.md п. 32).</exception>
    /// <remarks>Оригинал: numpy.var — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 4076</remarks>
    [Original("numpy.var", "numpy/_core/fromnumeric.py", 4076)]
    public static double Variance(this Vector<long> v, int deltaDegreesOfFreedom = 0) =>
        IntegerVariance(v, deltaDegreesOfFreedom);

    /// <summary>Дисперсия целых — как <c>np.var(v, ddof=…)</c>: NumPy считает её в double.</summary>
    /// <param name="v">Вектор.</param>
    /// <param name="deltaDegreesOfFreedom">Сколько вычесть из числа чисел в знаменателе (<c>ddof</c> в NumPy).</param>
    /// <returns>Дисперсия.</returns>
    /// <exception cref="FloatingPointError">Знаменатель не больше нуля (DECISIONS.md п. 32).</exception>
    /// <remarks>Оригинал: numpy.var — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 4076</remarks>
    [Original("numpy.var", "numpy/_core/fromnumeric.py", 4076)]
    public static double Variance(this Vector<int> v, int deltaDegreesOfFreedom = 0) =>
        IntegerVariance(v, deltaDegreesOfFreedom);

    /// <summary>
    /// Стандартное отклонение — как <c>np.std(v, ddof=…)</c>: корень из <see cref="Variance(Vector{double}, int)"/>.
    /// Выборочное — <c>v.StandardDeviation(deltaDegreesOfFreedom: 1)</c>.
    /// </summary>
    /// <param name="v">Вектор.</param>
    /// <param name="deltaDegreesOfFreedom">Сколько вычесть из числа чисел в знаменателе (<c>ddof</c> в NumPy).</param>
    /// <returns>Стандартное отклонение.</returns>
    /// <exception cref="FloatingPointError">Переполнение, ∞ − ∞ или знаменатель не больше нуля (DECISIONS.md п. 32).</exception>
    /// <remarks>
    /// Оригинал: numpy.std — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 3872
    ///   https://github.com/numpy/numpy/blob/v2.5.4/numpy/_core/fromnumeric.py#L3872
    /// Вызывает: _std — numpy/_core/_methods.py, строка 217
    /// </remarks>
    [Original("numpy.std", "numpy/_core/fromnumeric.py", 3872)]
    public static double StandardDeviation(this Vector<double> v, int deltaDegreesOfFreedom = 0) =>
        // ref: numpy/_core/_methods.py#L222-L227 — корень из дисперсии
        Math.Sqrt(v.Variance(deltaDegreesOfFreedom));

    /// <summary>Стандартное отклонение — как <c>np.std(v, ddof=…)</c> у float32.</summary>
    /// <param name="v">Вектор.</param>
    /// <param name="deltaDegreesOfFreedom">Сколько вычесть из числа чисел в знаменателе (<c>ddof</c> в NumPy).</param>
    /// <returns>Стандартное отклонение.</returns>
    /// <exception cref="FloatingPointError">Переполнение, ∞ − ∞ или знаменатель не больше нуля (DECISIONS.md п. 32).</exception>
    /// <remarks>Оригинал: numpy.std — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 3872</remarks>
    [Original("numpy.std", "numpy/_core/fromnumeric.py", 3872)]
    public static float StandardDeviation(this Vector<float> v, int deltaDegreesOfFreedom = 0) =>
        MathF.Sqrt(v.Variance(deltaDegreesOfFreedom));

    /// <summary>Стандартное отклонение целых — как <c>np.std(v, ddof=…)</c>: в double.</summary>
    /// <param name="v">Вектор.</param>
    /// <param name="deltaDegreesOfFreedom">Сколько вычесть из числа чисел в знаменателе (<c>ddof</c> в NumPy).</param>
    /// <returns>Стандартное отклонение.</returns>
    /// <exception cref="FloatingPointError">Знаменатель не больше нуля (DECISIONS.md п. 32).</exception>
    /// <remarks>Оригинал: numpy.std — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 3872</remarks>
    [Original("numpy.std", "numpy/_core/fromnumeric.py", 3872)]
    public static double StandardDeviation(this Vector<long> v, int deltaDegreesOfFreedom = 0) =>
        Math.Sqrt(v.Variance(deltaDegreesOfFreedom));

    /// <summary>Стандартное отклонение целых — как <c>np.std(v, ddof=…)</c>: в double.</summary>
    /// <param name="v">Вектор.</param>
    /// <param name="deltaDegreesOfFreedom">Сколько вычесть из числа чисел в знаменателе (<c>ddof</c> в NumPy).</param>
    /// <returns>Стандартное отклонение.</returns>
    /// <exception cref="FloatingPointError">Знаменатель не больше нуля (DECISIONS.md п. 32).</exception>
    /// <remarks>Оригинал: numpy.std — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 3872</remarks>
    [Original("numpy.std", "numpy/_core/fromnumeric.py", 3872)]
    public static double StandardDeviation(this Vector<int> v, int deltaDegreesOfFreedom = 0) =>
        Math.Sqrt(v.Variance(deltaDegreesOfFreedom));

    /// <summary>
    /// Медиана — как <c>np.median(v)</c>: среднее одного или двух чисел посередине упорядоченного вектора. NaN в
    /// векторе — результат NaN; пустой вектор — 0 / 0, по умолчанию исключение.
    /// </summary>
    /// <param name="v">Вектор.</param>
    /// <returns>Медиана.</returns>
    /// <exception cref="FloatingPointError">Пустой вектор или ∞ − ∞ посередине (DECISIONS.md п. 32).</exception>
    /// <remarks>
    /// Оригинал: numpy.median — NumPy v2.5.4, numpy/lib/_function_base_impl.py, строка 3916
    ///   https://github.com/numpy/numpy/blob/v2.5.4/numpy/lib/_function_base_impl.py#L3916
    /// Вызывает: _median — numpy/lib/_function_base_impl.py, строка 4004
    /// </remarks>
    [Original("numpy.median", "numpy/lib/_function_base_impl.py", 3916)]
    public static double Median(this Vector<double> v) => FloatMedian(v);

    /// <summary>Медиана — как <c>np.median(v)</c> у float32. NaN в векторе — результат NaN.</summary>
    /// <param name="v">Вектор.</param>
    /// <returns>Медиана.</returns>
    /// <exception cref="FloatingPointError">Пустой вектор или ∞ − ∞ посередине (DECISIONS.md п. 32).</exception>
    /// <remarks>Оригинал: numpy.median — NumPy v2.5.4, numpy/lib/_function_base_impl.py, строка 3916</remarks>
    [Original("numpy.median", "numpy/lib/_function_base_impl.py", 3916)]
    public static float Median(this Vector<float> v) => FloatMedian(v);

    /// <summary>Медиана целых — как <c>np.median(v)</c>: среднее чисел посередине в double.</summary>
    /// <param name="v">Вектор.</param>
    /// <returns>Медиана.</returns>
    /// <exception cref="FloatingPointError">Пустой вектор (DECISIONS.md п. 32).</exception>
    /// <remarks>Оригинал: numpy.median — NumPy v2.5.4, numpy/lib/_function_base_impl.py, строка 3916</remarks>
    [Original("numpy.median", "numpy/lib/_function_base_impl.py", 3916)]
    public static double Median(this Vector<long> v) => IntegerMedian(v);

    /// <summary>Медиана целых — как <c>np.median(v)</c>: среднее чисел посередине в double.</summary>
    /// <param name="v">Вектор.</param>
    /// <returns>Медиана.</returns>
    /// <exception cref="FloatingPointError">Пустой вектор (DECISIONS.md п. 32).</exception>
    /// <remarks>Оригинал: numpy.median — NumPy v2.5.4, numpy/lib/_function_base_impl.py, строка 3916</remarks>
    [Original("numpy.median", "numpy/lib/_function_base_impl.py", 3916)]
    public static double Median(this Vector<int> v) => IntegerMedian(v);

    /// <summary>
    /// Произведение всех чисел — как <c>np.prod(v)</c>: подряд, начиная с 1; у пустого вектора — 1.
    /// </summary>
    /// <param name="v">Вектор.</param>
    /// <returns>Произведение.</returns>
    /// <exception cref="FloatingPointError">Переполнение или 0 · ∞ (DECISIONS.md п. 32).</exception>
    /// <remarks>
    /// Оригинал: numpy.prod — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 3326
    ///   https://github.com/numpy/numpy/blob/v2.5.4/numpy/_core/fromnumeric.py#L3326
    /// Вызывает: np.multiply.reduce — numpy/_core/src/umath/loops_arithm_fp.dispatch.c.src, строка 55
    /// </remarks>
    [Original("numpy.prod", "numpy/_core/fromnumeric.py", 3326)]
    public static double Product(this Vector<double> v) => FloatProduct(v);

    /// <summary>Произведение всех чисел — как <c>np.prod(v)</c> у float32: во float.</summary>
    /// <param name="v">Вектор.</param>
    /// <returns>Произведение.</returns>
    /// <exception cref="FloatingPointError">Переполнение или 0 · ∞ (DECISIONS.md п. 32).</exception>
    /// <remarks>Оригинал: numpy.prod — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 3326</remarks>
    [Original("numpy.prod", "numpy/_core/fromnumeric.py", 3326)]
    public static float Product(this Vector<float> v) => FloatProduct(v);

    /// <summary>
    /// Произведение всех чисел — как <c>np.prod(v)</c>. Переполнение — исключение (DECISIONS.md п. 32); NumPy молча идёт
    /// по кругу, так — только явно: <c>using (Np.ErrState(over: "ignore"))</c>.
    /// </summary>
    /// <param name="v">Вектор.</param>
    /// <returns>Произведение.</returns>
    /// <exception cref="FloatingPointError">Произведение не помещается в long.</exception>
    /// <remarks>Оригинал: numpy.prod — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 3326</remarks>
    [Original("numpy.prod", "numpy/_core/fromnumeric.py", 3326)]
    public static long Product(this Vector<long> v) => LongProduct(v);

    /// <summary>
    /// Произведение всех чисел — как <c>np.prod(v)</c>: NumPy перемножает int в long (int64), поэтому и результат — long.
    /// Переполнение — исключение (DECISIONS.md п. 32).
    /// </summary>
    /// <param name="v">Вектор.</param>
    /// <returns>Произведение.</returns>
    /// <exception cref="FloatingPointError">Произведение не помещается в long.</exception>
    /// <remarks>Оригинал: numpy.prod — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 3326</remarks>
    [Original("numpy.prod", "numpy/_core/fromnumeric.py", 3326)]
    public static long Product(this Vector<int> v) => LongProduct(v);

    /// <summary>
    /// Суммы с накоплением — как <c>np.cumsum(v)</c>: первое число, сумма первых двух и так далее, подряд.
    /// </summary>
    /// <param name="v">Вектор.</param>
    /// <returns>Новый вектор той же длины.</returns>
    /// <exception cref="FloatingPointError">Переполнение или ∞ − ∞ (DECISIONS.md п. 32).</exception>
    /// <remarks>
    /// Оригинал: numpy.cumsum — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 2877
    ///   https://github.com/numpy/numpy/blob/v2.5.4/numpy/_core/fromnumeric.py#L2877
    /// </remarks>
    [Original("numpy.cumsum", "numpy/_core/fromnumeric.py", 2877)]
    public static Vector<double> CumulativeSum(this Vector<double> v) => FloatAccumulate(v, product: false);

    /// <summary>Суммы с накоплением — как <c>np.cumsum(v)</c> у float32.</summary>
    /// <param name="v">Вектор.</param>
    /// <returns>Новый вектор той же длины.</returns>
    /// <exception cref="FloatingPointError">Переполнение или ∞ − ∞ (DECISIONS.md п. 32).</exception>
    /// <remarks>Оригинал: numpy.cumsum — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 2877</remarks>
    [Original("numpy.cumsum", "numpy/_core/fromnumeric.py", 2877)]
    public static Vector<float> CumulativeSum(this Vector<float> v) => FloatAccumulate(v, product: false);

    /// <summary>Суммы с накоплением — как <c>np.cumsum(v)</c>. Переполнение — исключение (DECISIONS.md п. 32).</summary>
    /// <param name="v">Вектор.</param>
    /// <returns>Новый вектор той же длины.</returns>
    /// <exception cref="FloatingPointError">Сумма не помещается в long.</exception>
    /// <remarks>Оригинал: numpy.cumsum — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 2877</remarks>
    [Original("numpy.cumsum", "numpy/_core/fromnumeric.py", 2877)]
    public static Vector<long> CumulativeSum(this Vector<long> v) => LongAccumulate(v, product: false);

    /// <summary>
    /// Суммы с накоплением — как <c>np.cumsum(v)</c>: NumPy складывает int в long (int64). Переполнение — исключение.
    /// </summary>
    /// <param name="v">Вектор.</param>
    /// <returns>Новый вектор той же длины.</returns>
    /// <exception cref="FloatingPointError">Сумма не помещается в long.</exception>
    /// <remarks>Оригинал: numpy.cumsum — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 2877</remarks>
    [Original("numpy.cumsum", "numpy/_core/fromnumeric.py", 2877)]
    public static Vector<long> CumulativeSum(this Vector<int> v) => LongAccumulate(v, product: false);

    /// <summary>
    /// Произведения с накоплением — как <c>np.cumprod(v)</c>: первое число, произведение первых двух и так далее.
    /// </summary>
    /// <param name="v">Вектор.</param>
    /// <returns>Новый вектор той же длины.</returns>
    /// <exception cref="FloatingPointError">Переполнение или 0 · ∞ (DECISIONS.md п. 32).</exception>
    /// <remarks>
    /// Оригинал: numpy.cumprod — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 3451
    ///   https://github.com/numpy/numpy/blob/v2.5.4/numpy/_core/fromnumeric.py#L3451
    /// </remarks>
    [Original("numpy.cumprod", "numpy/_core/fromnumeric.py", 3451)]
    public static Vector<double> CumulativeProduct(this Vector<double> v) => FloatAccumulate(v, product: true);

    /// <summary>Произведения с накоплением — как <c>np.cumprod(v)</c> у float32.</summary>
    /// <param name="v">Вектор.</param>
    /// <returns>Новый вектор той же длины.</returns>
    /// <exception cref="FloatingPointError">Переполнение или 0 · ∞ (DECISIONS.md п. 32).</exception>
    /// <remarks>Оригинал: numpy.cumprod — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 3451</remarks>
    [Original("numpy.cumprod", "numpy/_core/fromnumeric.py", 3451)]
    public static Vector<float> CumulativeProduct(this Vector<float> v) => FloatAccumulate(v, product: true);

    /// <summary>Произведения с накоплением — как <c>np.cumprod(v)</c>. Переполнение — исключение (DECISIONS.md п. 32).</summary>
    /// <param name="v">Вектор.</param>
    /// <returns>Новый вектор той же длины.</returns>
    /// <exception cref="FloatingPointError">Произведение не помещается в long.</exception>
    /// <remarks>Оригинал: numpy.cumprod — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 3451</remarks>
    [Original("numpy.cumprod", "numpy/_core/fromnumeric.py", 3451)]
    public static Vector<long> CumulativeProduct(this Vector<long> v) => LongAccumulate(v, product: true);

    /// <summary>
    /// Произведения с накоплением — как <c>np.cumprod(v)</c>: NumPy перемножает int в long (int64). Переполнение —
    /// исключение.
    /// </summary>
    /// <param name="v">Вектор.</param>
    /// <returns>Новый вектор той же длины.</returns>
    /// <exception cref="FloatingPointError">Произведение не помещается в long.</exception>
    /// <remarks>Оригинал: numpy.cumprod — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 3451</remarks>
    [Original("numpy.cumprod", "numpy/_core/fromnumeric.py", 3451)]
    public static Vector<long> CumulativeProduct(this Vector<int> v) => LongAccumulate(v, product: true);

    /// <summary>
    /// Разности соседних чисел — как <c>np.diff(v)</c>: <c>v[1] − v[0]</c>, <c>v[2] − v[1]</c>…; на одно число короче.
    /// Вторые разности — <c>v.Diff().Diff()</c>.
    /// </summary>
    /// <param name="v">Вектор.</param>
    /// <returns>Новый вектор; у вектора из 0 или 1 числа — пустой.</returns>
    /// <exception cref="FloatingPointError">Переполнение или ∞ − ∞ (DECISIONS.md п. 32).</exception>
    /// <remarks>
    /// Оригинал: numpy.diff — NumPy v2.5.4, numpy/lib/_function_base_impl.py, строка 1415
    ///   https://github.com/numpy/numpy/blob/v2.5.4/numpy/lib/_function_base_impl.py#L1415
    /// </remarks>
    [Original("numpy.diff", "numpy/lib/_function_base_impl.py", 1415)]
    public static Vector<double> Diff(this Vector<double> v) => FloatDiff(v);

    /// <summary>Разности соседних чисел — как <c>np.diff(v)</c> у float32.</summary>
    /// <param name="v">Вектор.</param>
    /// <returns>Новый вектор на одно число короче.</returns>
    /// <exception cref="FloatingPointError">Переполнение или ∞ − ∞ (DECISIONS.md п. 32).</exception>
    /// <remarks>Оригинал: numpy.diff — NumPy v2.5.4, numpy/lib/_function_base_impl.py, строка 1415</remarks>
    [Original("numpy.diff", "numpy/lib/_function_base_impl.py", 1415)]
    public static Vector<float> Diff(this Vector<float> v) => FloatDiff(v);

    /// <summary>Разности соседних чисел — как <c>np.diff(v)</c>. Переполнение — исключение (DECISIONS.md п. 32).</summary>
    /// <param name="v">Вектор.</param>
    /// <returns>Новый вектор на одно число короче.</returns>
    /// <exception cref="FloatingPointError">Разность не помещается в long.</exception>
    /// <remarks>Оригинал: numpy.diff — NumPy v2.5.4, numpy/lib/_function_base_impl.py, строка 1415</remarks>
    [Original("numpy.diff", "numpy/lib/_function_base_impl.py", 1415)]
    public static Vector<long> Diff(this Vector<long> v) => IntegerDiff(v);

    /// <summary>Разности соседних чисел — как <c>np.diff(v)</c>: у int они тоже int. Переполнение — исключение.</summary>
    /// <param name="v">Вектор.</param>
    /// <returns>Новый вектор на одно число короче.</returns>
    /// <exception cref="FloatingPointError">Разность не помещается в int.</exception>
    /// <remarks>Оригинал: numpy.diff — NumPy v2.5.4, numpy/lib/_function_base_impl.py, строка 1415</remarks>
    [Original("numpy.diff", "numpy/lib/_function_base_impl.py", 1415)]
    public static Vector<int> Diff(this Vector<int> v) => IntegerDiff(v);

    /// <summary>
    /// Где меняется значение — как <c>np.diff(v)</c> у bool: NumPy вместо разности берёт «не равно».
    /// </summary>
    /// <param name="v">Вектор.</param>
    /// <returns>Новый вектор на одно число короче: true, где соседние значения разные.</returns>
    /// <remarks>Оригинал: numpy.diff — NumPy v2.5.4, numpy/lib/_function_base_impl.py, строка 1415</remarks>
    [Original("numpy.diff", "numpy/lib/_function_base_impl.py", 1415)]
    public static Vector<bool> Diff(this Vector<bool> v)
    {
        ArgumentNullException.ThrowIfNull(v);
        // ref: numpy/lib/_function_base_impl.py#L1539-L1541 — у bool — not_equal соседних
        var result = new bool[Math.Max(v.Count - 1, 0)];
        for (int i = 0; i < result.Length; i++)
            result[i] = v[i + 1] != v[i];
        return new Vector<bool>(result, 0, result.Length, 1);
    }

    /// <summary>
    /// Числа по возрастанию — как <c>np.sort(v, stable=True)</c>: NaN в конце; у равных чисел (в том числе 0 и −0) порядок
    /// как в векторе. Обычный <c>np.sort</c> переставляет 0 и −0 как придётся. По убыванию — <c>v.SortDescending()</c>.
    /// </summary>
    /// <param name="v">Вектор.</param>
    /// <typeparam name="T">Вид чисел.</typeparam>
    /// <returns>Новый вектор.</returns>
    /// <remarks>
    /// Оригинал: numpy.sort — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 942
    ///   https://github.com/numpy/numpy/blob/v2.5.4/numpy/_core/fromnumeric.py#L942
    /// </remarks>
    [Original("numpy.sort", "numpy/_core/fromnumeric.py", 942)]
    public static Vector<T> Sort<T>(this Vector<T> v)
        where T : unmanaged, INumber<T> => Take(v, Order(v, descending: false));

    /// <summary>
    /// Числа по убыванию — как <c>np.sort(v, stable=True, descending=True)</c>: NaN тоже в конце; у равных чисел порядок
    /// как в векторе.
    /// </summary>
    /// <param name="v">Вектор.</param>
    /// <typeparam name="T">Вид чисел.</typeparam>
    /// <returns>Новый вектор.</returns>
    /// <remarks>Оригинал: numpy.sort — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 942</remarks>
    [Original("numpy.sort", "numpy/_core/fromnumeric.py", 942)]
    public static Vector<T> SortDescending<T>(this Vector<T> v)
        where T : unmanaged, INumber<T> => Take(v, Order(v, descending: true));

    /// <summary>
    /// Номера чисел по возрастанию — как <c>np.argsort(v, stable=True)</c>: <c>v[номер]</c> по порядку даёт
    /// <c>v.Sort()</c>. У равных чисел — по порядку в векторе, NaN в конце. Обычный <c>np.argsort</c> у равных
    /// чисел неустойчив, и порядок у него зависит от процессора (DECISIONS.md п. 34).
    /// </summary>
    /// <param name="v">Вектор.</param>
    /// <typeparam name="T">Вид чисел.</typeparam>
    /// <returns>Вектор номеров, с нуля.</returns>
    /// <remarks>
    /// Оригинал: numpy.argsort — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 1116
    ///   https://github.com/numpy/numpy/blob/v2.5.4/numpy/_core/fromnumeric.py#L1116
    /// </remarks>
    [Original("numpy.argsort", "numpy/_core/fromnumeric.py", 1116)]
    public static Vector<int> IndicesOfValuesOrdered<T>(this Vector<T> v)
        where T : unmanaged, INumber<T> => Indices(Order(v, descending: false));

    /// <summary>
    /// Номера чисел по убыванию — как <c>np.argsort(v, stable=True, descending=True)</c>: у равных чисел — по порядку в
    /// векторе, NaN тоже в конце.
    /// </summary>
    /// <param name="v">Вектор.</param>
    /// <typeparam name="T">Вид чисел.</typeparam>
    /// <returns>Вектор номеров, с нуля.</returns>
    /// <remarks>Оригинал: numpy.argsort — NumPy v2.5.4, numpy/_core/fromnumeric.py, строка 1116</remarks>
    [Original("numpy.argsort", "numpy/_core/fromnumeric.py", 1116)]
    public static Vector<int> IndicesOfValuesOrderedDescending<T>(this Vector<T> v)
        where T : unmanaged, INumber<T> => Indices(Order(v, descending: true));

    private static T FloatExtreme<T>(Vector<T> v, bool max)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        ArgumentNullException.ThrowIfNull(v);
        if (v.Count == 0)
            throw NoIdentity(max);
        return Extremes.Reduce(v.Data, v.Offset, v.Count, v.Stride, max);
    }

    private static T IntegerExtreme<T>(Vector<T> v, bool max)
        where T : unmanaged, IBinaryInteger<T>
    {
        ArgumentNullException.ThrowIfNull(v);
        if (v.Count == 0)
            throw NoIdentity(max);
        // У целых равные числа неразличимы: любой порядок сравнений даёт одно и то же.
        T result = v[0];
        foreach (T x in v)
        {
            if (max ? x > result : x < result)
                result = x;
        }
        return result;
    }

    // ref: numpy/_core/src/umath/reduction.c#L99-L103 — пустой массив у свёртки без единицы
    private static ValueError NoIdentity(bool max) =>
        new($"zero-size array to reduction operation {(max ? "maximum" : "minimum")} which has no identity");

    private static int IndexOf<T>(Vector<T> v, bool max)
        where T : unmanaged, INumber<T>
    {
        ArgumentNullException.ThrowIfNull(v);
        // ref: numpy/_core/src/multiarray/calculation.c#L140-L144 — пустой массив
        if (v.Count == 0)
            throw new ValueError($"attempt to get {(max ? "argmax" : "argmin")} of an empty sequence");
        return Extremes.IndexOf(v.Data, v.Offset, v.Count, v.Stride, max);
    }

    /// <summary>Частное скаляров NumPy (<c>ret / rcount</c>) с проверкой ошибок чисел: 0 / 0, x / 0.</summary>
    private static double ScalarDivide(double x, long n)
    {
        var flags = FloatFlags.None;
        double result = CheckedMath.Divide(x, (double)n, ref flags);
        FloatErrors.Check(flags, "scalar divide");
        return result;
    }

    private static int Count<T>(Vector<T> v)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(v);
        return v.Count;
    }

    /// <summary>
    /// Сумма целых в double — как <c>np.add.reduce(v, dtype=float64)</c>: NumPy переводит числа в double кусками по
    /// размеру буфера (8192), каждый кусок складывает попарно и прибавляет к сумме.
    /// </summary>
    private static double SumAsDouble<T>(Vector<T> v)
        where T : unmanaged, IBinaryInteger<T>
    {
        ArgumentNullException.ThrowIfNull(v);
        var buffer = new double[Math.Min(v.Count, BufferSize)];
        var addition = default(CheckedAddition<double>);
        double sum = 0;
        for (int start = 0; start < v.Count; start += buffer.Length)
        {
            int count = Math.Min(buffer.Length, v.Count - start);
            for (int i = 0; i < count; i++)
                buffer[i] = double.CreateTruncating(v[start + i]);
            // ref: numpy/_core/src/umath/loops_arithm_fp.dispatch.c.src#L52-L54 — к сумме — попарная сумма куска
            sum = addition.Add(sum, Pairwise.Sum(buffer, 0, count, 1, ref addition));
        }
        FloatErrors.Check(addition.Flags, "reduce");
        return sum;
    }

    private static T FloatVariance<T>(Vector<T> v, int deltaDegreesOfFreedom)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        ArgumentNullException.ThrowIfNull(v);
        int n = v.Count;
        // ref: numpy/_core/_methods.py#L168-L179 — среднее: сумма, потом деление функцией NumPy (у float32 — в double)
        T sum = Sums.Total(v.Data, v.Layout, Sums.AllAxes)[0];
        var flags = FloatFlags.None;
        T mean = T.CreateTruncating(CheckedMath.Divide(double.CreateTruncating(sum), (double)n, ref flags));
        FloatErrors.Check(flags, "divide");
        // ref: numpy/_core/_methods.py#L188-L190 — отклонения и их квадраты
        T[] squares = Deviations(v, mean);
        return Spread(squares, n, deltaDegreesOfFreedom);
    }

    private static double IntegerVariance<T>(Vector<T> v, int deltaDegreesOfFreedom)
        where T : unmanaged, IBinaryInteger<T>
    {
        ArgumentNullException.ThrowIfNull(v);
        int n = v.Count;
        // ref: numpy/_core/_methods.py#L158-L160 — целые NumPy считает в double
        var flags = FloatFlags.None;
        double mean = CheckedMath.Divide(SumAsDouble(v), (double)n, ref flags);
        FloatErrors.Check(flags, "divide");
        return Spread(Deviations(v.Select(x => double.CreateTruncating(x)), mean), n, deltaDegreesOfFreedom);
    }

    /// <summary>Квадраты отклонений от среднего — np.subtract, потом np.square, каждая со своей проверкой ошибок.</summary>
    private static T[] Deviations<T>(Vector<T> v, T mean)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        var x = new T[v.Count];
        var flags = FloatFlags.None;
        for (int i = 0; i < x.Length; i++)
            x[i] = CheckedMath.Subtract(v.Data[v.Offset + (i * v.Stride)], mean, ref flags);
        FloatErrors.Check(flags, "subtract");
        flags = FloatFlags.None;
        for (int i = 0; i < x.Length; i++)
            x[i] = CheckedMath.Multiply(x[i], x[i], ref flags);
        FloatErrors.Check(flags, "square");
        return x;
    }

    /// <summary>Сумма квадратов, делённая на число степеней свободы, но не меньше нуля.</summary>
    private static T Spread<T>(T[] squares, int n, int deltaDegreesOfFreedom)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        // ref: numpy/_core/_methods.py#L201 — сумма квадратов — np.add.reduce
        T total = Sums.Total(squares, Layout.Contiguous([squares.Length]), Sums.AllAxes)[0];
        // ref: numpy/_core/_methods.py#L204-L211 — степеней свободы n − ddof, но не меньше нуля; деление как скаляров
        long freedom = Math.Max((long)n - deltaDegreesOfFreedom, 0);
        return T.CreateTruncating(ScalarDivide(double.CreateTruncating(total), freedom));
    }

    private static T FloatMedian<T>(Vector<T> v)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        ArgumentNullException.ThrowIfNull(v);
        // Array.Sort ставит NaN в начало, а NumPy — в конец: номер k у NumPy — k + (число NaN) здесь.
        T[] sorted = v.ToArray();
        Array.Sort(sorted);
        int n = sorted.Length, nans = 0;
        while (nans < n && T.IsNaN(sorted[nans]))
            nans++;
        T At(int k) => k < n - nans ? sorted[k + nans] : T.NaN;
        T result = MiddleMean(n, At);
        // ref: numpy/lib/_function_base_impl.py#L4054-L4055 — NaN в конце упорядоченного — медиана NaN
        return n > 0 && T.IsNaN(At(n - 1)) ? T.NaN : result;
    }

    private static double IntegerMedian<T>(Vector<T> v)
        where T : unmanaged, IBinaryInteger<T>
    {
        ArgumentNullException.ThrowIfNull(v);
        T[] sorted = v.ToArray();
        Array.Sort(sorted);
        return MiddleMean(sorted.Length, k => double.CreateTruncating(sorted[k]));
    }

    /// <summary>
    /// Среднее одного (n нечётное) или двух (чётное) чисел посередине — как <c>np.mean(part[indexer])</c> в
    /// <c>_median</c>; у пустого — 0 / 0.
    /// </summary>
    private static T MiddleMean<T>(int n, Func<int, T> at)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        // ref: numpy/lib/_function_base_impl.py#L4044-L4052 — одно число посередине или два, потом их среднее
        T[] middle;
        if (n == 0)
            middle = [];
        else if (n % 2 == 1)
            middle = [at(n / 2)];
        else
            middle = [at((n / 2) - 1), at(n / 2)];
        T sum = Sums.Total(middle, Layout.Contiguous([middle.Length]), Sums.AllAxes)[0];
        return T.CreateTruncating(ScalarDivide(double.CreateTruncating(sum), middle.Length));
    }

    private static T FloatProduct<T>(Vector<T> v)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        ArgumentNullException.ThrowIfNull(v);
        // ref: numpy/_core/src/umath/loops_arithm_fp.dispatch.c.src#L55-L65 — произведение подряд, начиная с единицы
        T product = T.One;
        var flags = FloatFlags.None;
        foreach (T x in v)
            product = CheckedMath.Multiply(product, x, ref flags);
        FloatErrors.Check(flags, "reduce");
        return product;
    }

    private static long LongProduct<T>(Vector<T> v)
        where T : unmanaged, IBinaryInteger<T>
    {
        ArgumentNullException.ThrowIfNull(v);
        long product = 1;
        var flags = FloatFlags.None;
        foreach (T x in v)
            product = MultiplyLong(product, long.CreateTruncating(x), ref flags);
        FloatErrors.Check(flags, "reduce");
        return product;
    }

    private static Vector<T> FloatAccumulate<T>(Vector<T> v, bool product)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        ArgumentNullException.ThrowIfNull(v);
        // ref: numpy/_core/src/umath/ufunc_object.c — accumulate: первое число копируется, дальше — предыдущий итог и
        // следующее число
        var result = new T[v.Count];
        var flags = FloatFlags.None;
        for (int i = 0; i < result.Length; i++)
        {
            T x = v.Data[v.Offset + (i * v.Stride)];
            result[i] = i == 0 ? x
                : product ? CheckedMath.Multiply(result[i - 1], x, ref flags)
                : CheckedMath.Add(result[i - 1], x, ref flags);
        }
        FloatErrors.Check(flags, "accumulate");
        return new Vector<T>(result, 0, result.Length, 1);
    }

    private static Vector<long> LongAccumulate<T>(Vector<T> v, bool product)
        where T : unmanaged, IBinaryInteger<T>
    {
        ArgumentNullException.ThrowIfNull(v);
        var result = new long[v.Count];
        var flags = FloatFlags.None;
        for (int i = 0; i < result.Length; i++)
        {
            long x = long.CreateTruncating(v[i]);
            result[i] = i == 0 ? x
                : product ? MultiplyLong(result[i - 1], x, ref flags)
                : AddLong(result[i - 1], x, ref flags);
        }
        FloatErrors.Check(flags, "accumulate");
        return new Vector<long>(result, 0, result.Length, 1);
    }

    private static Vector<T> FloatDiff<T>(Vector<T> v)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        ArgumentNullException.ThrowIfNull(v);
        // ref: numpy/lib/_function_base_impl.py#L1539-L1541 — a[1:] − a[:-1], функцией NumPy
        var result = new T[Math.Max(v.Count - 1, 0)];
        var flags = FloatFlags.None;
        for (int i = 0; i < result.Length; i++)
            result[i] = CheckedMath.Subtract(v[i + 1], v[i], ref flags);
        FloatErrors.Check(flags, "subtract");
        return new Vector<T>(result, 0, result.Length, 1);
    }

    private static Vector<T> IntegerDiff<T>(Vector<T> v)
        where T : unmanaged, IBinaryInteger<T>
    {
        ArgumentNullException.ThrowIfNull(v);
        var result = new T[Math.Max(v.Count - 1, 0)];
        var flags = FloatFlags.None;
        for (int i = 0; i < result.Length; i++)
        {
            T x = v[i + 1], y = v[i];
            T difference = unchecked(x - y);
            // Переполнение: у x и y знаки разные, а у разности знак не как у x.
            if (T.IsNegative(x) != T.IsNegative(y) && T.IsNegative(difference) != T.IsNegative(x))
                flags |= FloatFlags.Over;
            result[i] = difference;
        }
        FloatErrors.Check(flags, "subtract");
        return new Vector<T>(result, 0, result.Length, 1);
    }

    /// <summary>Сложение long по кругу, как у NumPy; переполнение отмечается во <paramref name="flags"/>.</summary>
    private static long AddLong(long x, long y, ref FloatFlags flags)
    {
        long sum = unchecked(x + y);
        if (((x ^ sum) & (y ^ sum)) < 0)
            flags |= FloatFlags.Over;
        return sum;
    }

    /// <summary>Умножение long по кругу, как у NumPy; переполнение отмечается во <paramref name="flags"/>.</summary>
    private static long MultiplyLong(long x, long y, ref FloatFlags flags)
    {
        long high = Math.BigMul(x, y, out long low);
        if (high != (low >> 63))
            flags |= FloatFlags.Over;
        return low;
    }

    /// <summary>
    /// Номера чисел в порядке сортировки: NaN в конце, у равных — по номеру. С номером при равенстве порядок полный,
    /// и любая сортировка даёт то же, что устойчивая сортировка NumPy.
    /// </summary>
    private static int[] Order<T>(Vector<T> v, bool descending)
        where T : unmanaged, INumber<T>
    {
        ArgumentNullException.ThrowIfNull(v);
        T[] values = v.ToArray();
        int[] order = new int[values.Length];
        for (int i = 0; i < order.Length; i++)
            order[i] = i;
        Array.Sort(order, (i, j) =>
        {
            T x = values[i], y = values[j];
            bool nanX = T.IsNaN(x), nanY = T.IsNaN(y);
            int c;
            if (nanX || nanY)
                c = nanX == nanY ? 0 : nanX ? 1 : -1;
            else
                c = descending ? y.CompareTo(x) : x.CompareTo(y);
            return c != 0 ? c : i.CompareTo(j);
        });
        return order;
    }

    private static Vector<T> Take<T>(Vector<T> v, int[] order)
        where T : unmanaged
    {
        var result = new T[order.Length];
        for (int i = 0; i < result.Length; i++)
            result[i] = v[order[i]];
        return new Vector<T>(result, 0, result.Length, 1);
    }

    private static Vector<int> Indices(int[] order) => new(order, 0, order.Length, 1);
}
