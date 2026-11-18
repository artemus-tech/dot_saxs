using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using DotSaxs.Testing;
using Xunit;
using static DotSaxs.Tests.ReferenceChecks;

namespace DotSaxs.Tests;

/// <summary>
/// Файлы .npy (DECISIONS.md п. 24): Np.LoadVector и Np.LoadMatrix читают всё, что пишет NumPy, — числа сверяются с
/// независимым читателем из DotSaxs.Testing; Np.Save пишет файлы байт в байт как np.save — сверяется со всеми файлами
/// эталонов; испорченные файлы — те же ошибки и тексты, что у np.load.
/// </summary>
public sealed class FilesTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("dotsaxs-files-").FullName;

    public static TheoryData<string> Fixtures() =>
        new(Directory.GetFiles(Path.Combine(References.Root, "npy-format"), "*.npy").Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal).ToArray());

    /// <summary>Файлы эталонов, записанные np.save: одно- и двумерные, по строкам, версия 1.0.</summary>
    public static TheoryData<string> SavedByNumPy()
    {
        var files = new List<string>();
        foreach (string path in Directory.GetFiles(References.Root, "*.npy", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            NpyArray a = Npy.Read(path);
            if (a.Shape.Count is 1 or 2 && !a.FortranOrder && a.Version == (1, 0) && a.Descr[0] != '>' && a.Type != "c8")
                files.Add(Path.GetRelativePath(References.Root, path));
        }
        return new TheoryData<string>(files.ToArray());
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ReadsEveryNumPyFileLikeIndependentReader(string file)
    {
        string path = Path.Combine(References.Root, "npy-format", file);
        NpyArray expected = Npy.Read(path);
        int[] shape = Np.GetFileShape(path);
        Assert.Equal(expected.Shape, shape);
        if (expected.Type == "c8")
        {
            // complex64 не переносим (DECISIONS.md п. 37).
            Assert.Equal("numbers of kind <c8 are not supported", Assert.Throws<ValueError>(() => Np.LoadVector<Complex>(path)).Message);
            return;
        }
        switch (shape.Length)
        {
            case 1:
                Assert.Empty(Compare.Arrays(expected, LoadVector(path, expected.Type), MatchLevel.Bitwise));
                break;
            case 2:
                Assert.Empty(Compare.Arrays(expected, LoadMatrix(path, expected.Type), MatchLevel.Bitwise));
                break;
            default:
                string text = expected.Shape.Count == 0 ? "()" : "(" + string.Join(", ", expected.Shape) + ")";
                Assert.Equal($"the file holds an array of shape {text}, not a vector", Assert.Throws<ValueError>(() => Np.LoadVector(path)).Message);
                Assert.Equal($"the file holds an array of shape {text}, not a matrix", Assert.Throws<ValueError>(() => Np.LoadMatrix(path)).Message);
                break;
        }
    }

    [Theory]
    [MemberData(nameof(SavedByNumPy))]
    public void SaveWritesSameBytesAsNumPy(string file)
    {
        string original = Path.Combine(References.Root, file);
        NpyArray a = Npy.Read(original);
        string copy = Path.Combine(_folder, "copy.npy");
        if (a.Shape.Count == 1)
            SaveVector(original, copy, a.Type);
        else
            SaveMatrix(original, copy, a.Type);
        Assert.Equal(File.ReadAllBytes(original), File.ReadAllBytes(copy));
    }

    [Fact]
    public void WithoutTypeNumbersBecomeDoublesLikeAstype()
    {
        string path = Path.Combine(References.Root, "npy-format", "int64.npy");
        long[] longs = Npy.Read(path).Get<long>();
        Assert.Equal(longs.Select(x => (double)x).ToArray(), Np.LoadVector(path).ToArray());
        string matrix = Path.Combine(References.Root, "npy-format", "matrix-c-order.npy");
        Assert.Equal(Npy.Read(matrix).Get<long>().Select(x => (double)x).ToArray(), Np.LoadMatrix(matrix).Flatten().ToArray());
        ValueError error = Assert.Throws<ValueError>(() => Np.LoadVector(Path.Combine(References.Root, "npy-format", "bool.npy")));
        Assert.Equal("the file holds numbers of kind |b1, but double were requested", error.Message);
        error = Assert.Throws<ValueError>(() => Np.LoadVector<long>(Path.Combine(References.Root, "npy-format", "float64.npy")));
        Assert.Equal("the file holds numbers of kind <f8, but long were requested", error.Message);
    }

    [Fact]
    public void SaveAddsExtensionLikeNumPy()
    {
        Vector<double> v = new[] { 0.1, 0.2 }.ToVector();
        Np.Save(Path.Combine(_folder, "data"), v);
        Assert.True(File.Exists(Path.Combine(_folder, "data.npy")));
        Assert.Equal(v.ToArray(), Np.LoadVector(Path.Combine(_folder, "data.npy")).ToArray());
        // Транспонированная матрица пишется по строкам: в файле — то, что видно, а не то, как лежат числа.
        Matrix<double> m = Matrix<double>.Create(2, 3, (row, column) => (10 * row) + column).Transpose();
        Np.Save(Path.Combine(_folder, "t.npy"), m);
        Assert.True(MatrixComparer<double>.ByValues.Equals(m, Np.LoadMatrix(Path.Combine(_folder, "t.npy"))));
    }

    [Fact]
    public void BrokenFilesGiveNumPyErrors()
    {
        byte[] good = File.ReadAllBytes(Path.Combine(References.Root, "npy-format", "float64.npy"));
        Assert.Equal("This file contains pickled (object) data. If you trust the file you can load it unsafely using the `allow_pickle=` keyword argument or `pickle.load()`.",
            Assert.Throws<ValueError>(() => Np.LoadVector(Write("text.npy", "hello world!"u8.ToArray()))).Message);
        Assert.Equal("No data left in file", Assert.Throws<EOFError>(() => Np.LoadVector(Write("empty.npy", []))).Message);
        Assert.Equal("the file is an .npz archive: read it with Np.LoadNpz",
            Assert.Throws<ValueError>(() => Np.LoadVector(Write("zip.npy", "PK\u0003\u0004rest"u8.ToArray()))).Message);
        Assert.Equal("Failed to read all data for array. Expected (14,) = 14 elements, could only read 11 elements. (file seems not fully written?)",
            Assert.Throws<ValueError>(() => Np.LoadVector(Write("short.npy", good[..^20]))).Message);
        byte[] version = (byte[])good.Clone();
        version[6] = 4;
        Assert.Equal("we only support format version (1,0), (2,0), and (3,0), not (4, 0)",
            Assert.Throws<ValueError>(() => Np.LoadVector(Write("v4.npy", version))).Message);
        byte[] length = (byte[])good.Clone();
        length[8] = 0xff;
        length[9] = 0xff;
        Assert.Equal($"EOF: reading array header, expected 65535 bytes got {good.Length - 10}",
            Assert.Throws<ValueError>(() => Np.LoadVector(Write("length.npy", length))).Message);
        byte[] keys = [.. good[..10], .. "{'descr': '<f8', 'fortran_order': False}".PadRight(117).Select(c => (byte)c), (byte)'\n', .. good[128..]];
        Assert.Equal("Header does not contain the correct keys: ['descr', 'fortran_order']",
            Assert.Throws<ValueError>(() => Np.LoadVector(Write("keys.npy", keys))).Message);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Write(string name, byte[] bytes)
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static NpyArray LoadVector(string path, string type) => type switch
    {
        "f8" => ToNpy(Np.LoadVector<double>(path)),
        "f4" => ToNpy(Np.LoadVector<float>(path)),
        "f2" => ToNpy(Np.LoadVector<Half>(path)),
        "i8" => ToNpy(Np.LoadVector<long>(path)),
        "i4" => ToNpy(Np.LoadVector<int>(path)),
        "i2" => ToNpy(Np.LoadVector<short>(path)),
        "i1" => ToNpy(Np.LoadVector<sbyte>(path)),
        "u8" => ToNpy(Np.LoadVector<ulong>(path)),
        "u4" => ToNpy(Np.LoadVector<uint>(path)),
        "u2" => ToNpy(Np.LoadVector<ushort>(path)),
        "u1" => ToNpy(Np.LoadVector<byte>(path)),
        "b1" => ToNpy(Np.LoadVector<bool>(path)),
        "c16" => ToNpy(Np.LoadVector<Complex>(path)),
        _ => throw new NotSupportedException(type),
    };

    private static NpyArray LoadMatrix(string path, string type) => type switch
    {
        "f8" => ToNpy(Np.LoadMatrix<double>(path)),
        "f4" => ToNpy(Np.LoadMatrix<float>(path)),
        "i8" => ToNpy(Np.LoadMatrix<long>(path)),
        "i4" => ToNpy(Np.LoadMatrix<int>(path)),
        "b1" => ToNpy(Np.LoadMatrix<bool>(path)),
        "c16" => ToNpy(Np.LoadMatrix<Complex>(path)),
        _ => throw new NotSupportedException(type),
    };

    private static void SaveVector(string from, string to, string type)
    {
        switch (type)
        {
            case "f8": Np.Save(to, Np.LoadVector<double>(from)); break;
            case "f4": Np.Save(to, Np.LoadVector<float>(from)); break;
            case "f2": Np.Save(to, Np.LoadVector<Half>(from)); break;
            case "i8": Np.Save(to, Np.LoadVector<long>(from)); break;
            case "i4": Np.Save(to, Np.LoadVector<int>(from)); break;
            case "i2": Np.Save(to, Np.LoadVector<short>(from)); break;
            case "i1": Np.Save(to, Np.LoadVector<sbyte>(from)); break;
            case "u8": Np.Save(to, Np.LoadVector<ulong>(from)); break;
            case "u4": Np.Save(to, Np.LoadVector<uint>(from)); break;
            case "u2": Np.Save(to, Np.LoadVector<ushort>(from)); break;
            case "u1": Np.Save(to, Np.LoadVector<byte>(from)); break;
            case "b1": Np.Save(to, Np.LoadVector<bool>(from)); break;
            case "c16": Np.Save(to, Np.LoadVector<Complex>(from)); break;
            default: throw new NotSupportedException(type);
        }
    }

    private static void SaveMatrix(string from, string to, string type)
    {
        switch (type)
        {
            case "f8": Np.Save(to, Np.LoadMatrix<double>(from)); break;
            case "f4": Np.Save(to, Np.LoadMatrix<float>(from)); break;
            case "i8": Np.Save(to, Np.LoadMatrix<long>(from)); break;
            case "i4": Np.Save(to, Np.LoadMatrix<int>(from)); break;
            case "b1": Np.Save(to, Np.LoadMatrix<bool>(from)); break;
            case "c16": Np.Save(to, Np.LoadMatrix<Complex>(from)); break;
            default: throw new NotSupportedException(type);
        }
    }
}
