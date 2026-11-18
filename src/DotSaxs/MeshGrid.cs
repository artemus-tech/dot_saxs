using System;

namespace DotSaxs;

public static partial class Np
{
    /// <summary>
    /// Сетка точек — как <c>np.meshgrid(x, y)</c>: две матрицы <c>y.Count</c> × <c>x.Count</c>; в <c>X</c> каждая строка —
    /// <paramref name="x"/>, в <c>Y</c> каждый столбец — <paramref name="y"/>. Так считают функцию двух переменных на
    /// сетке: <c>X.Select((x, row, column) => x * Y[row, column])</c>.
    /// </summary>
    /// <param name="x">Значения по столбцам.</param>
    /// <param name="y">Значения по строкам.</param>
    /// <typeparam name="T">Вид чисел.</typeparam>
    /// <returns>Матрицы <c>X</c> и <c>Y</c>.</returns>
    /// <remarks>
    /// Оригинал: numpy.meshgrid — NumPy v2.5.4, numpy/lib/_function_base_impl.py, строка 5064
    ///   https://github.com/numpy/numpy/blob/v2.5.4/numpy/lib/_function_base_impl.py#L5064
    /// </remarks>
    [Original("numpy.meshgrid", "numpy/lib/_function_base_impl.py", 5064)]
    public static (Matrix<T> X, Matrix<T> Y) MeshGrid<T>(Vector<T> x, Vector<T> y)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        // ref: numpy/lib/_function_base_impl.py#L5199-L5206 — indexing='xy': строки — y, столбцы — x; копии, как
        // copy=True по умолчанию
        Matrix<T> gridX = Matrix<T>.Create(y.Count, x.Count, (row, column) => x[column]);
        Matrix<T> gridY = Matrix<T>.Create(y.Count, x.Count, (row, column) => y[row]);
        return (gridX, gridY);
    }
}
