using System;
using System.Collections.Generic;
using System.Linq;

namespace DotSaxs;

/// <summary>
/// Двумерный массив чисел — как двумерный массив NumPy (DECISIONS.md п. 24). Строки и столбцы — словами:
/// <see cref="GetRow"/>, <see cref="GetColumn"/>, <see cref="GetRows"/> (п. 22); поэлементные вычисления —
/// <see cref="Select{TResult}(Func{T, int, int, TResult})"/>; суммы — свои, как в NumPy (<see cref="Sums"/>).
/// </summary>
/// <remarks>
/// Матрица не меняется после создания: методы возвращают новую. Новая матрица хранит числа по строкам, как в NumPy;
/// <see cref="Transpose"/> — вид на те же числа без копирования, как <c>a.T</c>, и суммы по нему идут в том же порядке,
/// что у NumPy.
/// </remarks>
/// <typeparam name="T">Вид чисел.</typeparam>
public sealed class Matrix<T>
    where T : unmanaged
{
    private readonly T[] _data;
    private readonly int _offset;
    private readonly int _rowStride;
    private readonly int _columnStride;

    /// <summary>Матрица поверх чисел <paramref name="data"/> по строкам без промежутков, начиная с <paramref name="offset"/>.</summary>
    internal Matrix(T[] data, int offset, int rows, int columns)
        : this(data, offset, rows, columns, columns, 1)
    {
    }

    /// <summary>Вид на числа <paramref name="data"/>: шаг между строками и между столбцами — любой, как у ndarray.</summary>
    internal Matrix(T[] data, int offset, int rows, int columns, int rowStride, int columnStride)
    {
        _data = data;
        _offset = offset;
        RowCount = rows;
        ColumnCount = columns;
        _rowStride = rowStride;
        _columnStride = columnStride;
    }

    /// <summary>
    /// Матрица из строк — как <c>np.vstack</c>: <c>Matrix&lt;double&gt;.FromRows(r1, r2, r3)</c> или
    /// <c>Matrix&lt;double&gt;.FromRows(m.GetRows().Where(r => r[0] > 0))</c>.
    /// </summary>
    /// <param name="rows">Строки одной длины.</param>
    /// <returns>Новая матрица; числа скопированы.</returns>
    /// <exception cref="ValueError">Строк нет или они разной длины.</exception>
    public static Matrix<T> FromRows(params IEnumerable<Vector<T>> rows)
    {
        Vector<T>[] list = Materialize(rows, nameof(rows));
        if (list.Length == 0)
            throw new ValueError(Messages.NoRows());
        int columns = list[0].Count;
        var data = new T[checked(list.Length * columns)];
        for (int i = 0; i < list.Length; i++)
        {
            if (list[i].Count != columns)
                throw new ValueError(Messages.RowLengthsDiffer(columns, i, list[i].Count));
            list[i].CopyTo(data, i * columns);
        }
        return new Matrix<T>(data, 0, list.Length, columns);
    }

    /// <summary>Матрица из столбцов — как <c>np.column_stack</c>: <c>Matrix&lt;double&gt;.FromColumns(q, i, err)</c>.</summary>
    /// <param name="columns">Столбцы одной длины.</param>
    /// <returns>Новая матрица; числа скопированы.</returns>
    /// <exception cref="ValueError">Столбцов нет или они разной длины.</exception>
    public static Matrix<T> FromColumns(params IEnumerable<Vector<T>> columns)
    {
        Vector<T>[] list = Materialize(columns, nameof(columns));
        if (list.Length == 0)
            throw new ValueError(Messages.NoColumns());
        int rows = list[0].Count;
        var data = new T[checked(rows * list.Length)];
        for (int j = 0; j < list.Length; j++)
        {
            Vector<T> column = list[j];
            if (column.Count != rows)
                throw new ValueError(Messages.ColumnLengthsDiffer(rows, j, column.Count));
            for (int i = 0; i < rows; i++)
                data[i * list.Length + j] = column[i];
        }
        return new Matrix<T>(data, 0, rows, list.Length);
    }

    /// <summary>Сколько строк.</summary>
    public int RowCount { get; }

    /// <summary>Сколько столбцов.</summary>
    public int ColumnCount { get; }

    /// <summary>Число в строке <paramref name="row"/> и столбце <paramref name="column"/>, с нуля.</summary>
    /// <param name="row">Номер строки.</param>
    /// <param name="column">Номер столбца.</param>
    /// <exception cref="IndexError">Строка или столбец за пределами матрицы.</exception>
    public T this[int row, int column]
    {
        get
        {
            CheckRow(row);
            CheckColumn(column);
            return _data[_offset + row * _rowStride + column * _columnStride];
        }
    }

    /// <summary>Строка с номером <paramref name="row"/> — как <c>a[row]</c> в NumPy; числа не копируются.</summary>
    /// <param name="row">Номер строки, с нуля.</param>
    /// <returns>Строка.</returns>
    /// <exception cref="IndexError">Строка за пределами матрицы.</exception>
    public Vector<T> GetRow(int row)
    {
        CheckRow(row);
        return new Vector<T>(_data, _offset + row * _rowStride, ColumnCount, _columnStride);
    }

    /// <summary>Столбец с номером <paramref name="column"/> — как <c>a[:, column]</c> в NumPy; числа не копируются.</summary>
    /// <param name="column">Номер столбца, с нуля.</param>
    /// <returns>Столбец.</returns>
    /// <exception cref="IndexError">Столбец за пределами матрицы.</exception>
    public Vector<T> GetColumn(int column)
    {
        CheckColumn(column);
        return new Vector<T>(_data, _offset + column * _columnStride, RowCount, _rowStride);
    }

    /// <summary>Все строки по порядку — для LINQ: <c>m.GetRows().Where(r => r.Sum() > 0)</c>.</summary>
    /// <returns>Строки.</returns>
    public IEnumerable<Vector<T>> GetRows()
    {
        for (int i = 0; i < RowCount; i++)
            yield return GetRow(i);
    }

    /// <summary>Все столбцы по порядку — для LINQ.</summary>
    /// <returns>Столбцы.</returns>
    public IEnumerable<Vector<T>> GetColumns()
    {
        for (int j = 0; j < ColumnCount; j++)
            yield return GetColumn(j);
    }

    /// <summary>Новое значение для каждого числа: <c>m.Select(Np.Exp)</c>.</summary>
    /// <param name="selector">Что сделать с числом.</param>
    /// <typeparam name="TResult">Вид чисел результата.</typeparam>
    /// <returns>Новая матрица тех же размеров.</returns>
    public Matrix<TResult> Select<TResult>(Func<T, TResult> selector)
        where TResult : unmanaged
    {
        ArgumentNullException.ThrowIfNull(selector);
        var result = new TResult[RowCount * ColumnCount];
        for (int i = 0, k = 0; i < RowCount; i++)
        {
            for (int j = 0; j < ColumnCount; j++, k++)
                result[k] = selector(_data[_offset + i * _rowStride + j * _columnStride]);
        }
        return new Matrix<TResult>(result, 0, RowCount, ColumnCount);
    }

    /// <summary>
    /// Новое значение для каждого числа с его строкой и столбцом — тот же цикл <c>for</c>: сложение двух матриц —
    /// <c>m.Select((x, row, column) => x + b[row, column])</c>, вычесть вектор из каждой строки —
    /// <c>m.Select((x, row, column) => x - v[column])</c> (DECISIONS.md п. 24).
    /// </summary>
    /// <param name="selector">Что сделать с числом, номером его строки и столбца.</param>
    /// <typeparam name="TResult">Вид чисел результата.</typeparam>
    /// <returns>Новая матрица тех же размеров.</returns>
    public Matrix<TResult> Select<TResult>(Func<T, int, int, TResult> selector)
        where TResult : unmanaged
    {
        ArgumentNullException.ThrowIfNull(selector);
        var result = new TResult[RowCount * ColumnCount];
        for (int i = 0, k = 0; i < RowCount; i++)
        {
            for (int j = 0; j < ColumnCount; j++, k++)
                result[k] = selector(_data[_offset + i * _rowStride + j * _columnStride], i, j);
        }
        return new Matrix<TResult>(result, 0, RowCount, ColumnCount);
    }

    /// <summary>
    /// Все числа одной строкой, строка за строкой — как <c>np.ravel</c>: без копирования, если числа идут с одним шагом
    /// (матрица по строкам, одна строка или один столбец), иначе — копия, как у NumPy.
    /// </summary>
    /// <returns>Вектор из <see cref="RowCount"/> · <see cref="ColumnCount"/> чисел.</returns>
    public Vector<T> Flatten()
    {
        int count = RowCount * ColumnCount;
        if (ColumnCount == 1)
            return new Vector<T>(_data, _offset, count, _rowStride);
        if (RowCount <= 1 || _rowStride == _columnStride * ColumnCount)
            return new Vector<T>(_data, _offset, count, _columnStride);
        var result = new T[count];
        for (int i = 0; i < RowCount; i++)
            GetRow(i).CopyTo(result, i * ColumnCount);
        return new Vector<T>(result, 0, count, 1);
    }

    /// <summary>
    /// Транспонированная матрица — как <c>a.T</c> и <c>np.transpose(a)</c>: строки становятся столбцами. Это вид на те же
    /// числа без копирования; суммы по нему совпадают с NumPy до бита, как у <c>a.T.sum(axis=…)</c>.
    /// </summary>
    /// <returns>Матрица <see cref="ColumnCount"/> × <see cref="RowCount"/>.</returns>
    public Matrix<T> Transpose() => new(_data, _offset, ColumnCount, RowCount, _columnStride, _rowStride);

    /// <summary>Числа, на которые смотрит матрица.</summary>
    internal T[] Data => _data;

    /// <summary>Как лежат числа матрицы в <see cref="Data"/> — для обхода ядра (Core).</summary>
    internal Core.Layout Layout => new(_offset, [RowCount, ColumnCount], [_rowStride, _columnStride]);

    private void CheckRow(int row)
    {
        if ((uint)row >= (uint)RowCount)
            throw new IndexError(Messages.RowOutOfMatrix(row, RowCount));
    }

    private void CheckColumn(int column)
    {
        if ((uint)column >= (uint)ColumnCount)
            throw new IndexError(Messages.ColumnOutOfMatrix(column, ColumnCount));
    }

    private static Vector<T>[] Materialize(IEnumerable<Vector<T>> vectors, string name)
    {
        ArgumentNullException.ThrowIfNull(vectors, name);
        Vector<T>[] list = vectors.ToArray();
        foreach (Vector<T> vector in list)
            ArgumentNullException.ThrowIfNull(vector, name);
        return list;
    }
}
