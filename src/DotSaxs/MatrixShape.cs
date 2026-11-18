using System;
using System.Collections.Generic;
using System.Globalization;

namespace DotSaxs;

// Работа с формой матрицы словами (DECISIONS.md п. 22, 24): первая и последняя строка, строки с шагом и по номерам,
// добавить, вставить и убрать строку или столбец, повернуть, собрать из блоков. Методы возвращают новую матрицу или
// вид на те же числа: матрица не меняется. У своих методов без оригинала атрибута [Original] нет (п. 25).
public sealed partial class Matrix<T>
    where T : unmanaged
{
    /// <summary>Первая строка — как <c>a[0]</c>; числа не копируются.</summary>
    /// <returns>Строка.</returns>
    /// <exception cref="IndexError">В матрице нет строк.</exception>
    public Vector<T> GetFirstRow() => RowCount > 0 ? GetRow(0) : throw new IndexError(Messages.NoRowsInMatrix());

    /// <summary>Последняя строка — как <c>a[-1]</c>; числа не копируются. Отсчёта с конца (<c>^1</c>) в API нет (п. 22).</summary>
    /// <returns>Строка.</returns>
    /// <exception cref="IndexError">В матрице нет строк.</exception>
    public Vector<T> GetLastRow() => RowCount > 0 ? GetRow(RowCount - 1) : throw new IndexError(Messages.NoRowsInMatrix());

    /// <summary>Первый столбец — как <c>a[:, 0]</c>; числа не копируются.</summary>
    /// <returns>Столбец.</returns>
    /// <exception cref="IndexError">В матрице нет столбцов.</exception>
    public Vector<T> GetFirstColumn() =>
        ColumnCount > 0 ? GetColumn(0) : throw new IndexError(Messages.NoColumnsInMatrix());

    /// <summary>Последний столбец — как <c>a[:, -1]</c>; числа не копируются.</summary>
    /// <returns>Столбец.</returns>
    /// <exception cref="IndexError">В матрице нет столбцов.</exception>
    public Vector<T> GetLastColumn() =>
        ColumnCount > 0 ? GetColumn(ColumnCount - 1) : throw new IndexError(Messages.NoColumnsInMatrix());

    /// <summary>
    /// Строки 0, <paramref name="step"/>, 2·<paramref name="step"/>… — как <c>a[::step]</c>, для LINQ. Матрица из них —
    /// <c>Matrix&lt;double&gt;.FromRows(m.GetRowsByStep(2))</c>.
    /// </summary>
    /// <param name="step">Шаг, больше нуля.</param>
    /// <returns>Строки; числа не копируются.</returns>
    /// <exception cref="ValueError">Шаг не больше нуля.</exception>
    public IEnumerable<Vector<T>> GetRowsByStep(int step)
    {
        CheckStep(step);
        return Rows(step);
    }

    /// <summary>Столбцы 0, <paramref name="step"/>, 2·<paramref name="step"/>… — как <c>a[:, ::step]</c>, для LINQ.</summary>
    /// <param name="step">Шаг, больше нуля.</param>
    /// <returns>Столбцы; числа не копируются.</returns>
    /// <exception cref="ValueError">Шаг не больше нуля.</exception>
    public IEnumerable<Vector<T>> GetColumnsByStep(int step)
    {
        CheckStep(step);
        return Columns(step);
    }

    /// <summary>Строки с номерами <paramref name="indices"/>, в том же порядке — как <c>a[[0, 2, 5]]</c>, для LINQ.</summary>
    /// <param name="indices">Номера строк, с нуля; могут повторяться.</param>
    /// <returns>Строки; числа не копируются.</returns>
    /// <exception cref="IndexError">Номер за пределами матрицы.</exception>
    public IEnumerable<Vector<T>> GetRowsByIndices(params int[] indices)
    {
        ArgumentNullException.ThrowIfNull(indices);
        var rows = new Vector<T>[indices.Length];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = GetRow(indices[i]);
        return rows;
    }

    /// <summary>Столбцы с номерами <paramref name="indices"/>, в том же порядке — как <c>a[:, [0, 2, 5]]</c>, для LINQ.</summary>
    /// <param name="indices">Номера столбцов, с нуля; могут повторяться.</param>
    /// <returns>Столбцы; числа не копируются.</returns>
    /// <exception cref="IndexError">Номер за пределами матрицы.</exception>
    public IEnumerable<Vector<T>> GetColumnsByIndices(params int[] indices)
    {
        ArgumentNullException.ThrowIfNull(indices);
        var columns = new Vector<T>[indices.Length];
        for (int i = 0; i < columns.Length; i++)
            columns[i] = GetColumn(indices[i]);
        return columns;
    }

    /// <summary>Новая матрица со строкой <paramref name="row"/> в конце — как <c>np.append(a, [row], axis=0)</c>.</summary>
    /// <param name="row">Строка; длина — число столбцов матрицы.</param>
    /// <returns>Новая матрица.</returns>
    /// <exception cref="ValueError">Длина строки не равна числу столбцов.</exception>
    public Matrix<T> AppendRow(Vector<T> row) => InsertRow(RowCount, row);

    /// <summary>Новая матрица со строкой <paramref name="row"/> в начале.</summary>
    /// <param name="row">Строка; длина — число столбцов матрицы.</param>
    /// <returns>Новая матрица.</returns>
    /// <exception cref="ValueError">Длина строки не равна числу столбцов.</exception>
    public Matrix<T> PrependRow(Vector<T> row) => InsertRow(0, row);

    /// <summary>
    /// Новая матрица со строкой <paramref name="row"/> на месте <paramref name="index"/> — как
    /// <c>np.insert(a, index, row, axis=0)</c>: прежняя строка с этим номером и дальше сдвигаются вниз.
    /// </summary>
    /// <param name="index">Место: от 0 (в начало) до <see cref="RowCount"/> (в конец).</param>
    /// <param name="row">Строка; длина — число столбцов матрицы.</param>
    /// <returns>Новая матрица.</returns>
    /// <exception cref="IndexError">Места с таким номером нет.</exception>
    /// <exception cref="ValueError">Длина строки не равна числу столбцов.</exception>
    public Matrix<T> InsertRow(int index, Vector<T> row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if ((uint)index > (uint)RowCount)
            throw new IndexError(Messages.RowInsertOutside(index, RowCount));
        if (row.Count != ColumnCount)
            throw new ValueError(Messages.RowLengthMismatch(row.Count, ColumnCount));
        T[] data = Core.Memory.Allocate<T>((long)(RowCount + 1) * ColumnCount);
        for (int i = 0; i <= RowCount; i++)
        {
            Vector<T> source = i == index ? row : GetRow(i < index ? i : i - 1);
            source.CopyTo(data, i * ColumnCount);
        }
        return new Matrix<T>(data, 0, RowCount + 1, ColumnCount);
    }

    /// <summary>Новая матрица со столбцом <paramref name="column"/> справа — как <c>np.append(a, column[:, None], axis=1)</c>.</summary>
    /// <param name="column">Столбец; длина — число строк матрицы.</param>
    /// <returns>Новая матрица.</returns>
    /// <exception cref="ValueError">Длина столбца не равна числу строк.</exception>
    public Matrix<T> AppendColumn(Vector<T> column) => InsertColumn(ColumnCount, column);

    /// <summary>Новая матрица со столбцом <paramref name="column"/> слева.</summary>
    /// <param name="column">Столбец; длина — число строк матрицы.</param>
    /// <returns>Новая матрица.</returns>
    /// <exception cref="ValueError">Длина столбца не равна числу строк.</exception>
    public Matrix<T> PrependColumn(Vector<T> column) => InsertColumn(0, column);

    /// <summary>
    /// Новая матрица со столбцом <paramref name="column"/> на месте <paramref name="index"/> — как
    /// <c>np.insert(a, index, column, axis=1)</c>: прежний столбец с этим номером и дальше сдвигаются вправо.
    /// </summary>
    /// <param name="index">Место: от 0 (слева) до <see cref="ColumnCount"/> (справа).</param>
    /// <param name="column">Столбец; длина — число строк матрицы.</param>
    /// <returns>Новая матрица.</returns>
    /// <exception cref="IndexError">Места с таким номером нет.</exception>
    /// <exception cref="ValueError">Длина столбца не равна числу строк.</exception>
    public Matrix<T> InsertColumn(int index, Vector<T> column)
    {
        ArgumentNullException.ThrowIfNull(column);
        if ((uint)index > (uint)ColumnCount)
            throw new IndexError(Messages.ColumnInsertOutside(index, ColumnCount));
        if (column.Count != RowCount)
            throw new ValueError(Messages.ColumnLengthMismatch(column.Count, RowCount));
        int columns = ColumnCount + 1;
        T[] data = Core.Memory.Allocate<T>((long)RowCount * columns);
        for (int i = 0; i < RowCount; i++)
        {
            for (int j = 0; j < columns; j++)
                data[(i * columns) + j] = j == index ? column[i] : At(i, j < index ? j : j - 1);
        }
        return new Matrix<T>(data, 0, RowCount, columns);
    }

    /// <summary>Новая матрица без строки <paramref name="index"/> — как <c>np.delete(a, index, axis=0)</c>.</summary>
    /// <param name="index">Номер строки, с нуля.</param>
    /// <returns>Новая матрица.</returns>
    /// <exception cref="IndexError">Строка за пределами матрицы.</exception>
    public Matrix<T> RemoveRow(int index)
    {
        CheckRow(index);
        int rows = RowCount - 1;
        T[] data = Core.Memory.Allocate<T>((long)rows * ColumnCount);
        for (int i = 0; i < rows; i++)
            GetRow(i < index ? i : i + 1).CopyTo(data, i * ColumnCount);
        return new Matrix<T>(data, 0, rows, ColumnCount);
    }

    /// <summary>Новая матрица без столбца <paramref name="index"/> — как <c>np.delete(a, index, axis=1)</c>.</summary>
    /// <param name="index">Номер столбца, с нуля.</param>
    /// <returns>Новая матрица.</returns>
    /// <exception cref="IndexError">Столбец за пределами матрицы.</exception>
    public Matrix<T> RemoveColumn(int index)
    {
        CheckColumn(index);
        int columns = ColumnCount - 1;
        T[] data = Core.Memory.Allocate<T>((long)RowCount * columns);
        for (int i = 0; i < RowCount; i++)
        {
            for (int j = 0; j < columns; j++)
                data[(i * columns) + j] = At(i, j < index ? j : j + 1);
        }
        return new Matrix<T>(data, 0, RowCount, columns);
    }

    /// <summary>
    /// Матрица, повёрнутая на четверть оборота против часовой стрелки, — как <c>np.rot90(a)</c>: первая строка
    /// результата — последний столбец. Это вид на те же числа без копирования, как у NumPy. Пол-оборота —
    /// <c>m.RotateCounterclockwise().RotateCounterclockwise()</c>, как <c>np.rot90(a, 2)</c>.
    /// </summary>
    /// <returns>Матрица <see cref="ColumnCount"/> × <see cref="RowCount"/>.</returns>
    /// <remarks>
    /// Оригинал: numpy.rot90 — NumPy v2.5.4, numpy/lib/_function_base_impl.py, строка 181
    ///   https://github.com/numpy/numpy/blob/v2.5.4/numpy/lib/_function_base_impl.py#L181
    /// </remarks>
    [Original("numpy.rot90", "numpy/lib/_function_base_impl.py", 181)]
    public Matrix<T> RotateCounterclockwise()
    {
        // ref: numpy/lib/_function_base_impl.py#L273-L274 — k = 1: transpose(flip(m, 1)) — столбцы задом наперёд,
        // потом строки и столбцы меняются местами
        int offset = RowCount == 0 || ColumnCount == 0 ? _offset : _offset + ((ColumnCount - 1) * _columnStride);
        return new Matrix<T>(_data, offset, ColumnCount, RowCount, -_columnStride, _rowStride);
    }

    /// <summary>
    /// Матрица, повёрнутая на четверть оборота по часовой стрелке, — как <c>np.rot90(a, -1)</c>: первая строка
    /// результата — первый столбец снизу вверх. Вид на те же числа без копирования.
    /// </summary>
    /// <returns>Матрица <see cref="ColumnCount"/> × <see cref="RowCount"/>.</returns>
    /// <remarks>
    /// Оригинал: numpy.rot90 — NumPy v2.5.4, numpy/lib/_function_base_impl.py, строка 181
    ///   https://github.com/numpy/numpy/blob/v2.5.4/numpy/lib/_function_base_impl.py#L181
    /// </remarks>
    [Original("numpy.rot90", "numpy/lib/_function_base_impl.py", 181)]
    public Matrix<T> RotateClockwise()
    {
        // ref: numpy/lib/_function_base_impl.py#L275-L277 — k = 3: flip(transpose(m), 1) — строки и столбцы меняются
        // местами, потом столбцы задом наперёд
        int offset = RowCount == 0 || ColumnCount == 0 ? _offset : _offset + ((RowCount - 1) * _rowStride);
        return new Matrix<T>(_data, offset, ColumnCount, RowCount, _columnStride, -_rowStride);
    }

    /// <summary>
    /// Матрица из блоков — как <c>np.block([[a, b], [c, d]])</c>: блоки одной строки ставятся рядом, строки блоков — друг
    /// под другом: <c>Matrix&lt;double&gt;.FromBlocks([a, b], [c, d])</c>. В ряду <see cref="FromRows"/> и
    /// <see cref="FromColumns"/>.
    /// </summary>
    /// <param name="blockRows">Строки блоков: у блоков одной строки — одно число строк, у строк блоков — одно число столбцов.</param>
    /// <returns>Новая матрица.</returns>
    /// <exception cref="ValueError">Строк блоков нет, строка блоков пустая или размеры не сходятся — тексты как у NumPy.</exception>
    /// <remarks>
    /// Оригинал: numpy.block — NumPy v2.5.4, numpy/_core/shape_base.py, строка 779
    ///   https://github.com/numpy/numpy/blob/v2.5.4/numpy/_core/shape_base.py#L779
    /// </remarks>
    [Original("numpy.block", "numpy/_core/shape_base.py", 779)]
    public static Matrix<T> FromBlocks(params Matrix<T>[][] blockRows)
    {
        ArgumentNullException.ThrowIfNull(blockRows);
        // ref: numpy/_core/shape_base.py#L959-L964 — пустой список блоков
        if (blockRows.Length == 0)
            throw new ValueError("List at arrays cannot be empty");
        long size = 0;
        for (int i = 0; i < blockRows.Length; i++)
        {
            ArgumentNullException.ThrowIfNull(blockRows[i], nameof(blockRows));
            if (blockRows[i].Length == 0)
                throw new ValueError(string.Create(CultureInfo.InvariantCulture, $"List at arrays[{i}] cannot be empty"));
            foreach (Matrix<T> block in blockRows[i])
            {
                ArgumentNullException.ThrowIfNull(block, nameof(blockRows));
                size += (long)block.RowCount * block.ColumnCount;
            }
        }
        // ref: numpy/_core/shape_base.py#L944-L947 — большие массивы NumPy собирает другим способом, и текст ошибки там
        // свой: «Mismatched array shapes in block along axis 1.»
        bool slicing = 2 * size > 2 * 512 * 512;

        // ref: numpy/_core/shape_base.py#L677-L684 — у блоков одной строки одно число строк, у строк блоков — одно число
        // столбцов; сначала проверяются строки блоков по порядку, потом всё вместе
        var rowHeights = new int[blockRows.Length];
        var rowWidths = new int[blockRows.Length];
        for (int i = 0; i < blockRows.Length; i++)
        {
            Matrix<T>[] row = blockRows[i];
            for (int j = 1; j < row.Length; j++)
            {
                if (row[j].RowCount != row[0].RowCount)
                    throw Mismatch(slicing, 1, 0, row[0].RowCount, j, row[j].RowCount);
            }
            rowHeights[i] = row[0].RowCount;
            foreach (Matrix<T> block in row)
                rowWidths[i] += block.ColumnCount;
        }
        for (int i = 1; i < blockRows.Length; i++)
        {
            if (rowWidths[i] != rowWidths[0])
                throw Mismatch(slicing, 0, 1, rowWidths[0], i, rowWidths[i]);
        }

        int rows = 0;
        foreach (int height in rowHeights)
            rows += height;
        int columns = rowWidths[0];
        T[] data = Core.Memory.Allocate<T>((long)rows * columns);
        int top = 0;
        for (int i = 0; i < blockRows.Length; i++)
        {
            int left = 0;
            foreach (Matrix<T> block in blockRows[i])
            {
                for (int r = 0; r < block.RowCount; r++)
                    block.GetRow(r).CopyTo(data, ((top + r) * columns) + left);
                left += block.ColumnCount;
            }
            top += rowHeights[i];
        }
        return new Matrix<T>(data, 0, rows, columns);
    }

    private static ValueError Mismatch(bool slicing, int axis, int dimension, int first, int index, int size) =>
        // ref: numpy/_core/src/multiarray/multiarraymodule.c#L440-L447 — PyArray_ConcatenateArrays: размеры, кроме оси склейки
        new(slicing
            ? string.Create(CultureInfo.InvariantCulture, $"Mismatched array shapes in block along axis {axis}.")
            : string.Create(CultureInfo.InvariantCulture,
                $"all the input array dimensions except for the concatenation axis must match exactly, but along dimension {dimension}, the array at index 0 has size {first} and the array at index {index} has size {size}"));

    private static void CheckStep(int step)
    {
        if (step <= 0)
            throw new ValueError(Messages.StepNotPositive(step));
    }

    private IEnumerable<Vector<T>> Rows(int step)
    {
        for (int i = 0; i < RowCount; i += step)
            yield return GetRow(i);
    }

    private IEnumerable<Vector<T>> Columns(int step)
    {
        for (int j = 0; j < ColumnCount; j += step)
            yield return GetColumn(j);
    }

    /// <summary>Число без проверки номеров — для своих циклов.</summary>
    private T At(int row, int column) => _data[_offset + (row * _rowStride) + (column * _columnStride)];
}
