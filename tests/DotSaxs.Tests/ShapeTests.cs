using System;
using System.Linq;
using DotSaxs.Testing;
using Xunit;
using static DotSaxs.Tests.ReferenceChecks;

namespace DotSaxs.Tests;

/// <summary>
/// Работа с формой против эталонов NumPy (DECISIONS.md п. 22, 24, 34): строки и столбцы — как np.append, np.insert,
/// np.delete; повороты — np.rot90; FromBlocks — np.block; Tri, Vandermonde, MeshGrid, GetWindows, CrossProduct.
/// </summary>
public sealed class ShapeTests
{
    private static readonly Matrix<double> Table = Matrix<double>.Create(3, 4, (row, column) => (10 * row) + column);

    public static TheoryData<string, string> EditCases() => Cases("numpy.append", "numpy.insert", "numpy.delete");

    public static TheoryData<string, string> RotateCases() => Cases("numpy.rot90");

    public static TheoryData<string, string> BlockCases() => Cases("numpy.block");

    public static TheoryData<string, string> TriCases() => Cases("numpy.tri");

    public static TheoryData<string, string> VandermondeCases() => Cases("numpy.vander");

    public static TheoryData<string, string> GridCases() => Cases("numpy.meshgrid");

    public static TheoryData<string, string> WindowCases() => Cases("numpy.lib.stride_tricks.sliding_window_view");

    public static TheoryData<string, string> CrossCases() => Cases("numpy.cross");

    [Theory]
    [MemberData(nameof(EditCases))]
    public void AppendInsertAndRemoveMatchNumPy(string python, string name)
    {
        ReferenceCase c = References.Case(python, name);
        Matrix<double> m = ToMatrix(c.Array("arr"));
        int axis = Int(c, "axis");
        Check(c, () => ToNpy((python, axis) switch
        {
            ("numpy.append", 0) => m.AppendRow(c.Array("values").Get<double>().ToVector()),
            ("numpy.append", 1) => m.AppendColumn(c.Array("values").Get<double>().ToVector()),
            ("numpy.insert", 0) => m.InsertRow(Int(c, "obj"), ToVector<double>(c.Array("values"))),
            ("numpy.insert", 1) => m.InsertColumn(Int(c, "obj"), ToVector<double>(c.Array("values"))),
            ("numpy.delete", 0) => m.RemoveRow(Int(c, "obj")),
            ("numpy.delete", 1) => m.RemoveColumn(Int(c, "obj")),
            var other => throw new NotSupportedException(other.ToString()),
        }));
    }

    [Theory]
    [MemberData(nameof(RotateCases))]
    public void RotationsMatchRot90(string python, string name)
    {
        ReferenceCase c = References.Case(python, name);
        Matrix<double> m = ToMatrix(c.Array("m"));
        int k = Int(c, "k", 1);
        Check(c, () => ToNpy(k switch
        {
            1 => m.RotateCounterClockWise(),
            -1 => m.RotateClockWise(),
            2 => m.RotateCounterClockWise().RotateCounterClockWise(),
            _ => throw new NotSupportedException($"{python}: k = {k}"),
        }));
    }

    [Theory]
    [MemberData(nameof(BlockCases))]
    public void FromBlocksMatchesBlock(string python, string name)
    {
        ReferenceCase c = References.Case(python, name);
        Matrix<double>[][] blocks = ((ListValue)c.Argument("arrays")).Items
            .Select(row => ((ListValue)row).Items.Select(block => ToMatrix(((NpyValue)block).Value)).ToArray())
            .ToArray();
        Check(c, () => ToNpy(Matrix<double>.FromBlocks(blocks)));
    }

    [Fact]
    public void LargeBlocksMismatchHasNumPyTextOfSlicing()
    {
        // Больше 2¹⁸ чисел np.block собирает по кускам, и текст ошибки там другой (numpy/_core/shape_base.py, строка 944).
        Matrix<double> square = Matrix<double>.Create(400, 400, (row, column) => 0.0);
        Matrix<double> taller = Matrix<double>.Create(401, 400, (row, column) => 0.0);
        ValueError error = Assert.Throws<ValueError>(() => Matrix<double>.FromBlocks([square, taller]));
        Assert.Equal("Mismatched array shapes in block along axis 1.", error.Message);
    }

    [Theory]
    [MemberData(nameof(TriCases))]
    public void TriMatchesNumPy(string python, string name)
    {
        ReferenceCase c = References.Case(python, name);
        int n = Int(c, "N");
        bool rectangle = c.Args.Any(a => a.Name == "M");
        Check(c, () => ToNpy(rectangle ? Matrix<double>.Tri(n, Int(c, "M"), Int(c, "k")) : Matrix<double>.Tri(n)));
    }

    [Theory]
    [MemberData(nameof(VandermondeCases))]
    public void VandermondeMatchesVander(string python, string name)
    {
        ReferenceCase c = References.Case(python, name);
        Vector<double> x = ToVector<double>(c.Array("x"));
        bool columns = c.Args.Any(a => a.Name == "N");
        bool increasing = Bool(c, "increasing", false);
        Check(c, () => ToNpy(columns ? Matrix<double>.Vandermonde(x, Int(c, "N"), increasing) : Matrix<double>.Vandermonde(x)));
    }

    [Theory]
    [MemberData(nameof(GridCases))]
    public void MeshGridMatchesNumPy(string python, string name)
    {
        ReferenceCase c = References.Case(python, name);
        (Matrix<double> x, Matrix<double> y) = Np.MeshGrid(ToVector<double>(c.Array("x")), ToVector<double>(c.Array("y")));
        var expected = ((TupleValue)c.Result!).Items.Select(item => ((NpyValue)item).Value).ToArray();
        Assert.Empty(Compare.Arrays(expected[0], ToNpy(x), c.Match));
        Assert.Empty(Compare.Arrays(expected[1], ToNpy(y), c.Match));
    }

    [Theory]
    [MemberData(nameof(WindowCases))]
    public void GetWindowsMatchesSlidingWindowView(string python, string name)
    {
        ReferenceCase c = References.Case(python, name);
        Vector<double> x = ToVector<double>(c.Array("x"));
        int length = Int(c, "window_shape");
        Check(c, () =>
        {
            Vector<double>[] windows = x.GetWindows(length).ToArray();
            return NpyArray.Of(windows.SelectMany(w => w).ToArray(), windows.Length, length);
        });
    }

    [Theory]
    [MemberData(nameof(CrossCases))]
    public void CrossProductMatchesCross(string python, string name)
    {
        ReferenceCase c = References.Case(python, name);
        Vector<double> a = ToVector<double>(c.Array("a")), b = ToVector<double>(c.Array("b"));
        Check(c, () => ToNpy(a.CrossProduct(b)));
    }

    [Fact]
    public void FirstLastAndByStep()
    {
        Assert.Equal(new[] { 0.0, 1.0, 2.0, 3.0 }, Table.GetFirstRow().ToArray());
        Assert.Equal(new[] { 20.0, 21.0, 22.0, 23.0 }, Table.GetLastRow().ToArray());
        Assert.Equal(new[] { 0.0, 10.0, 20.0 }, Table.GetFirstColumn().ToArray());
        Assert.Equal(new[] { 3.0, 13.0, 23.0 }, Table.GetLastColumn().ToArray());
        Assert.Equal(new[] { 0.0, 20.0 }, Table.GetRowsByStep(2).Select(r => r[0]).ToArray());
        Assert.Equal(new[] { 0.0, 3.0 }, Table.GetColumnsByStep(3).Select(c => c[0]).ToArray());
        Assert.Equal(new[] { 2.0, 0.0, 2.0 }, Table.GetColumnsByIndices(2, 0, 2).Select(c => c[0]).ToArray());
        Assert.Equal(new[] { 20.0, 0.0 }, Table.GetRowsByIndices(2, 0).Select(r => r[0]).ToArray());
        Assert.Equal(Table.InsertRow(0, Table.GetLastRow()).Flatten().ToArray(), Table.PrependRow(Table.GetLastRow()).Flatten().ToArray());
        Assert.Equal(Table.InsertColumn(0, Table.GetLastColumn()).Flatten().ToArray(), Table.PrependColumn(Table.GetLastColumn()).Flatten().ToArray());
        // Поворот — вид на те же числа, четыре поворота — исходная матрица.
        Matrix<double> round = Table.RotateClockWise().RotateClockWise().RotateClockWise().RotateClockWise();
        Assert.True(MatrixComparer<double>.ByValues.Equals(Table, round));
        Assert.True(MatrixComparer<double>.ByValues.Equals(Table.RotateCounterClockWise(), Table.RotateClockWise().RotateClockWise().RotateClockWise()));
    }

    [Fact]
    public void ShapeErrorsNameWhatDidNotFit()
    {
        Vector<double> three = new[] { 1.0, 2.0, 3.0 }.ToVector();
        Assert.Equal("the row has 3 numbers, but the matrix has 4 columns", Assert.Throws<ValueError>(() => Table.AppendRow(three)).Message);
        Assert.Equal("the column has 4 numbers, but the matrix has 3 rows",
            Assert.Throws<ValueError>(() => Table.AppendColumn(Table.GetRow(0))).Message);
        Assert.Equal("a row cannot be inserted at 5: the places are 0 to 3", Assert.Throws<IndexError>(() => Table.InsertRow(5, Table.GetRow(0))).Message);
        Assert.Equal("a column cannot be inserted at -1: the places are 0 to 4", Assert.Throws<IndexError>(() => Table.InsertColumn(-1, three)).Message);
        Assert.Equal("row 3 is outside the matrix (rows: 3)", Assert.Throws<IndexError>(() => Table.RemoveRow(3)).Message);
        Assert.Equal("the step must be greater than zero, but it is 0", Assert.Throws<ValueError>(() => Table.GetRowsByStep(0)).Message);
        Matrix<double> empty = Matrix<double>.Create(0, 2, (row, column) => 0.0);
        Assert.Equal("the matrix has no rows", Assert.Throws<IndexError>(() => empty.GetLastRow()).Message);
        Assert.Equal("column 7 is outside the matrix (columns: 4)", Assert.Throws<IndexError>(() => Table.GetColumnsByIndices(1, 7)).Message);
    }

    [Fact]
    public void BuildingMatrixRowByRowFromEmpty()
    {
        Matrix<double> m = Matrix<double>.Create(0, 2, (row, column) => 0.0)
            .AppendRow(new[] { 1.0, 2.0 }.ToVector())
            .AppendRow(new[] { 3.0, 4.0 }.ToVector());
        Assert.Equal((2, 2), (m.RowCount, m.ColumnCount));
        Assert.Equal(new[] { 1.0, 2.0, 3.0, 4.0 }, m.Flatten().ToArray());
        Assert.Equal(new[] { 2.0, 4.0 }, m.RemoveColumn(0).Flatten().ToArray());
    }
}
