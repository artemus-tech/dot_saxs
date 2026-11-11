using System;
using System.Linq;
using Xunit;

namespace DotSaxs.Tests;

/// <summary>Vector и Matrix: создание, номера, строки и столбцы, Select, ошибки (DECISIONS.md п. 22, 24, 32).</summary>
public sealed class VectorMatrixTests
{
    private static readonly Matrix<double> Table = Matrix<double>.FromRows(
        Vector<double>.From(new[] { 1.0, 2.0, 3.0 }),
        Vector<double>.From(new[] { 4.0, 5.0, 6.0 }));

    [Fact]
    public void VectorKeepsNumbersInOrder()
    {
        Vector<double> v = Vector<double>.From(new[] { 0.5, -1.0, 2.0 });
        Assert.Equal(3, v.Count);
        Assert.Equal(-1.0, v[1]);
        Assert.Equal(new[] { 0.5, -1.0, 2.0 }, v.ToArray());
        Assert.Equal(new[] { 2.0 }, v.Where(x => x > 1).ToVector().ToArray());
    }

    [Fact]
    public void VectorIndexOutsideThrowsIndexErrorInEnglishByDefault()
    {
        Vector<double> v = Vector<double>.From(new[] { 1.0, 2.0, 3.0 });
        IndexError error = Assert.Throws<IndexError>(() => v[5]);
        Assert.Equal("index 5 is outside the vector (length 3)", error.Message);
        Assert.Throws<IndexError>(() => v[-1]);
    }

    [Fact]
    public void SelectGivesNumberAndIndex()
    {
        Vector<double> v = Vector<double>.From(new[] { 1.0, 2.0, 3.0 });
        Assert.Equal(new[] { 2.0, 4.0, 6.0 }, v.Select(x => x * 2).ToArray());
        Assert.Equal(new[] { 1.0, 3.0, 5.0 }, v.Select((x, i) => x + i).ToArray());
        Assert.Equal(new[] { true, false, true }, v.Select(x => x != 2.0).ToArray());
    }

    [Fact]
    public void VectorToMatrixGoesRowByRow()
    {
        Matrix<int> m = Vector<int>.From(Enumerable.Range(0, 6)).ToMatrix(2, 3);
        Assert.Equal((2, 3), (m.RowCount, m.ColumnCount));
        Assert.Equal(new[] { 3, 4, 5 }, m.GetRow(1).ToArray());
        ValueError error = Assert.Throws<ValueError>(() => Vector<int>.From(Enumerable.Range(0, 6)).ToMatrix(4, 2));
        Assert.Equal("a vector of length 6 cannot be laid out as a 4 × 2 matrix", error.Message);
    }

    [Fact]
    public void RowsAndColumnsOfMatrix()
    {
        Assert.Equal((2, 3), (Table.RowCount, Table.ColumnCount));
        Assert.Equal(6.0, Table[1, 2]);
        Assert.Equal(new[] { 4.0, 5.0, 6.0 }, Table.GetRow(1).ToArray());
        Assert.Equal(new[] { 2.0, 5.0 }, Table.GetColumn(1).ToArray());
        Assert.Equal(new[] { 2.0, 5.0 }, Table.GetColumns().ElementAt(1).ToArray());
        Assert.Equal(new[] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0 }, Table.Flatten().ToArray());
        Assert.Equal(5.0, Table.GetColumn(1)[1]);
    }

    [Fact]
    public void TransposeSwapsRowsAndColumnsWithoutCopying()
    {
        // Table — [[1, 2, 3], [4, 5, 6]]; Transpose() — [[1, 4], [2, 5], [3, 6]].
        Matrix<double> t = Table.Transpose();
        Assert.Equal((3, 2), (t.RowCount, t.ColumnCount));
        Assert.Equal(6.0, t[2, 1]);
        Assert.Equal(new[] { 1.0, 4.0 }, t.GetRow(0).ToArray());
        Assert.Equal(new[] { 1.0, 2.0, 3.0 }, t.GetColumn(0).ToArray());
        Assert.Equal(new[] { 1.0, 4.0, 2.0, 5.0, 3.0, 6.0 }, t.Flatten().ToArray());
        Assert.Equal(Table.Flatten().ToArray(), t.Transpose().Flatten().ToArray());
        Assert.Equal(new[] { 2.0, 8.0, 4.0, 10.0, 6.0, 12.0 }, t.Select(x => x * 2).Flatten().ToArray());
        Assert.Equal(new[] { 0.0, 3.0, 1.0, 4.0, 2.0, 5.0 }, t.Select((x, row, column) => (double)(row + 3 * column)).Flatten().ToArray());
        Assert.Equal(new[] { 1.0, 2.0, 3.0 }, t.GetColumns().First().ToArray());
        Assert.Equal(new[] { 5.0, 7.0, 9.0 }, t.GetRows().Select(r => r.Sum()).ToArray());
        Assert.Equal(new[] { 6.0, 15.0 }, t.GetColumns().Select(c => c.Sum()).ToArray());
    }

    [Fact]
    public void MatrixFromColumnsEqualsFromRows()
    {
        Matrix<double> byColumns = Matrix<double>.FromColumns(Table.GetColumns());
        Assert.Equal(Table.Flatten().ToArray(), byColumns.Flatten().ToArray());
        Matrix<double> byRows = Matrix<double>.FromRows(Table.GetRows().Where(r => r[0] > 2));
        Assert.Equal(new[] { 4.0, 5.0, 6.0 }, byRows.Flatten().ToArray());
    }

    [Fact]
    public void MatrixSelectGivesRowAndColumn()
    {
        Vector<double> shift = Vector<double>.From(new[] { 1.0, 2.0, 3.0 });
        Matrix<double> centered = Table.Select((x, row, column) => x - shift[column]);
        Assert.Equal(new[] { 0.0, 0.0, 0.0, 3.0, 3.0, 3.0 }, centered.Flatten().ToArray());
        Assert.Equal(new[] { 10.0, 20.0, 30.0, 40.0, 50.0, 60.0 }, Table.Select(x => x * 10).Flatten().ToArray());
    }

    [Fact]
    public void MatrixErrorsNameWhatDidNotFit()
    {
        Assert.Equal("row 2 is outside the matrix (rows: 2)", Assert.Throws<IndexError>(() => Table[2, 0]).Message);
        Assert.Equal("column 3 is outside the matrix (columns: 3)", Assert.Throws<IndexError>(() => Table.GetColumn(3)).Message);
        Assert.Equal("at least one row is required", Assert.Throws<ValueError>(() => Matrix<double>.FromRows()).Message);
        ValueError lengths = Assert.Throws<ValueError>(
            () => Matrix<double>.FromRows(Table.GetRow(0), Vector<double>.From(new[] { 1.0 })));
        Assert.Equal("rows differ in length: row 0 has length 3, row 1 has length 1", lengths.Message);
        Assert.IsAssignableFrom<ArgumentException>(lengths);
    }

    [Fact]
    public void SumsOfSmallMatrix()
    {
        Assert.Equal(21.0, Table.Flatten().Sum());
        Assert.Equal(new[] { 6.0, 15.0 }, Table.GetRows().Select(r => r.Sum()).ToArray());
        Assert.Equal(new[] { 5.0, 7.0, 9.0 }, Table.GetRows().ElementWiseSum().ToArray());
        Assert.Equal(15L, Vector<int>.From(new[] { 1, 2, 3, 4, 5 }).Sum());
        Assert.Equal(
            "vectors differ in length: vector 0 has length 3, vector 1 has length 1",
            Assert.Throws<ValueError>(() => new[] { Table.GetRow(0), Vector<double>.From(new[] { 1.0 }) }.ElementWiseSum()).Message);
    }

    [Fact]
    public void SumOfNegativeZerosIsPositiveZeroLikeNumPy()
    {
        // NumPy: np.sum(np.array([-0.0, -0.0])) → 0.0 — сумма начинается с +0 (единица сложения).
        double sum = Vector<double>.From(new[] { -0.0, -0.0 }).Sum();
        Assert.Equal(0UL, BitConverter.DoubleToUInt64Bits(sum));
    }
}
