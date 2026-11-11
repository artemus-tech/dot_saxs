using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Xunit;

namespace DotSaxs.Testing.Tests;

/// <summary>Чтение .npy: файлы, записанные NumPy, читаются точно до бита; испорченные — понятная ошибка.</summary>
public sealed class NpyTests
{
    private static string Folder => Path.Combine(References.Root, "npy-format");

    [Fact]
    public void FilesWrittenByNumPyReadExactly()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder, "fixtures.json")));
        var errors = new List<string>();
        var listed = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement fixture in doc.RootElement.GetProperty("fixtures").EnumerateArray())
        {
            string file = fixture.GetProperty("file").GetString()!;
            listed.Add(file);
            NpyArray array = Npy.Read(Path.Combine(Folder, file));
            errors.AddRange(Differences(fixture, array).Select(d => $"{file}: {d}"));
        }
        string[] files = Directory.GetFiles(Folder, "*.npy").Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(files, listed.Order(StringComparer.Ordinal).ToArray());
        if (errors.Count > 0)
            Assert.Fail("Прочитано не то, что записал NumPy:\n" + string.Join("\n", errors));
    }

    [Fact]
    public void ReadsHeaderWrittenByHand()
    {
        NpyArray array = Npy.Read(NpyStream("{'descr': '<f8', 'fortran_order': False, 'shape': (2, 3), }", 48));
        Assert.Equal("<f8", array.Descr);
        Assert.Equal(new[] { 2, 3 }, array.Shape);
        Assert.Equal(new double[6], array.Get<double>());
    }

    [Fact]
    public void ReadsPython2LongIntegers()
    {
        NpyArray array = Npy.Read(NpyStream("{'descr': '<i8', 'fortran_order': False, 'shape': (3L,), }", 24));
        Assert.Equal(new[] { 3 }, array.Shape);
    }

    [Fact]
    public void RejectsFileWithoutMagic() =>
        Assert.Throws<InvalidDataException>(() => Npy.Read(new MemoryStream(Encoding.ASCII.GetBytes("not a numpy file at all"))));

    [Fact]
    public void RejectsUnknownVersion() =>
        Assert.Throws<NotSupportedException>(() => Npy.Read(NpyStream("{'descr': '<f8', 'fortran_order': False, 'shape': (1,), }", 8, major: 4)));

    [Theory]
    [InlineData("{'descr': [('x', '<f8')], 'fortran_order': False, 'shape': (1,), }")]
    [InlineData("{'descr': '<U3', 'fortran_order': False, 'shape': (1,), }")]
    [InlineData("{'descr': '|O', 'fortran_order': False, 'shape': (1,), }")]
    [InlineData("{'descr': '<f16', 'fortran_order': False, 'shape': (1,), }")]
    public void RejectsUnsupportedKinds(string header) =>
        Assert.Throws<NotSupportedException>(() => Npy.Read(NpyStream(header, 16)));

    [Theory]
    [InlineData("{'descr': '<f8', 'fortran_order': False, 'shape': (4,), }", 16)]
    [InlineData("{'descr': '<f8', 'fortran_order': False, 'shape': (1,), }", 16)]
    [InlineData("{'descr': '<f8', 'shape': (1,), }", 8)]
    [InlineData("{'descr': '<f8', 'fortran_order': False, 'shape': (1,) ", 8)]
    public void RejectsBrokenFiles(string header, int dataBytes) =>
        Assert.Throws<InvalidDataException>(() => Npy.Read(NpyStream(header, dataBytes)));

    [Fact]
    public void WrongElementTypeIsExplained()
    {
        NpyArray array = Npy.Read(NpyStream("{'descr': '<f8', 'fortran_order': False, 'shape': (1,), }", 8));
        InvalidCastException e = Assert.Throws<InvalidCastException>(() => array.Get<float>());
        Assert.Contains("<f8", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SignedAndUnsignedAreNotMixedUp()
    {
        // Среда .NET считает uint[] совместимым с int[] (так же ulong/long, ushort/short, byte/sbyte): тип сверяется точно.
        Assert.Throws<InvalidCastException>(() => Npy.Read(Path.Combine(Folder, "uint32.npy")).Get<int>());
        Assert.Throws<InvalidCastException>(() => Npy.Read(Path.Combine(Folder, "uint64.npy")).Get<long>());
        Assert.Throws<InvalidCastException>(() => Npy.Read(Path.Combine(Folder, "int16.npy")).Get<ushort>());
        Assert.Throws<InvalidCastException>(() => Npy.Read(Path.Combine(Folder, "int8.npy")).Get<byte>());
        Assert.Equal("<u8", NpyArray.Of(new ulong[] { 1 }, 1).Descr);
        Assert.Equal("<u4", NpyArray.Of(new uint[] { 1 }, 1).Descr);
        Assert.Equal("|u1", NpyArray.Of(new byte[] { 1 }, 1).Descr);
        Assert.Equal("|i1", NpyArray.Of(new sbyte[] { 1 }, 1).Descr);
    }

    /// <summary>Расхождения прочитанного с тем, что записано в fixtures.json.</summary>
    private static IEnumerable<string> Differences(JsonElement fixture, NpyArray array)
    {
        string descr = fixture.GetProperty("descr").GetString()!;
        if (array.Descr != descr)
            yield return $"вид {array.Descr}, а записан {descr}";
        int[] shape = fixture.GetProperty("shape").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        if (!array.Shape.SequenceEqual(shape))
            yield return $"размеры ({string.Join(", ", array.Shape)}), а записаны ({string.Join(", ", shape)})";
        if (array.FortranOrder != fixture.GetProperty("fortran_order").GetBoolean())
            yield return $"fortran_order {array.FortranOrder}";
        int[] version = fixture.GetProperty("version").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        if (array.Version != (version[0], version[1]))
            yield return $"версия {array.Version}, а записана {version[0]}.{version[1]}";
        string[] expected = fixture.GetProperty("values").EnumerateArray().Select(Text).ToArray();
        string[] actual = Texts(array);
        if (expected.Length != actual.Length)
            yield return $"элементов {actual.Length}, а записано {expected.Length}";
        else
            foreach (int i in Enumerable.Range(0, expected.Length).Where(k => expected[k] != actual[k]).Take(5))
                yield return $"элемент {i}: прочитано {actual[i]}, а записано {expected[i]}";
    }

    /// <summary>Значение из fixtures.json как текст: целые — числом, дробные — битами, комплексные — «биты/биты».</summary>
    private static string Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.String => value.GetString()!,
        JsonValueKind.Array => string.Join("/", value.EnumerateArray().Select(p => p.GetString())),
        _ => throw new InvalidDataException(value.GetRawText()),
    };

    /// <summary>Прочитанные элементы в той же записи, что <see cref="Text"/>.</summary>
    private static string[] Texts(NpyArray array) => array.Data switch
    {
        bool[] x => x.Select(b => b ? "true" : "false").ToArray(),
        double[] x => x.Select(Bits).ToArray(),
        float[] x => x.Select(Bits).ToArray(),
        Half[] x => x.Select(h => BitConverter.HalfToUInt16Bits(h).ToString("x4", CultureInfo.InvariantCulture)).ToArray(),
        Complex[] x when array.Type == "c8" => x.Select(c => $"{Bits((float)c.Real)}/{Bits((float)c.Imaginary)}").ToArray(),
        Complex[] x => x.Select(c => $"{Bits(c.Real)}/{Bits(c.Imaginary)}").ToArray(),
        _ => array.Data.Cast<object>().Select(o => Convert.ToString(o, CultureInfo.InvariantCulture)!).ToArray(),
    };

    private static string Bits(double x) => BitConverter.DoubleToUInt64Bits(x).ToString("x16", CultureInfo.InvariantCulture);

    private static string Bits(float x) => BitConverter.SingleToUInt32Bits(x).ToString("x8", CultureInfo.InvariantCulture);

    /// <summary>Файл .npy в памяти: метка, версия, длина заголовка, заголовок с выравниванием до 64 байт, данные (нули).</summary>
    private static MemoryStream NpyStream(string header, int dataBytes, byte major = 1)
    {
        int prefix = major == 1 ? 10 : 12;
        var text = new StringBuilder(header);
        while ((prefix + text.Length + 1) % 64 != 0)
            text.Append(' ');
        text.Append('\n');
        byte[] headerBytes = Encoding.ASCII.GetBytes(text.ToString());
        var stream = new MemoryStream();
        stream.Write([0x93, (byte)'N', (byte)'U', (byte)'M', (byte)'P', (byte)'Y', major, 0]);
        stream.Write(major == 1 ? BitConverter.GetBytes((ushort)headerBytes.Length) : BitConverter.GetBytes((uint)headerBytes.Length));
        stream.Write(headerBytes);
        stream.Write(new byte[dataBytes]);
        stream.Position = 0;
        return stream;
    }
}
