using System;
using System.IO;
using System.Numerics;
using DotSaxs.Core;

namespace DotSaxs;

// Чтение и запись своих форматов NumPy (DECISIONS.md п. 24): .npy — Np.LoadVector, Np.LoadMatrix, Np.Save. Тип известен
// заранее — ошибка, если в файле другая размерность. Без параметра типа числа приводятся к double, как
// a.astype(np.float64); с параметром — вид чисел должен совпасть с файлом.
public static partial class Np
{
    /// <summary>
    /// Вектор из файла .npy — как <c>np.load(path)</c> с одномерным массивом. Числа любого вида (float32, int64…)
    /// приводятся к double, как <c>a.astype(np.float64)</c>; вид как в файле — <see cref="LoadVector{T}(string)"/>.
    /// </summary>
    /// <param name="path">Путь к файлу.</param>
    /// <returns>Вектор.</returns>
    /// <exception cref="ValueError">Файл испорчен (тексты NumPy) или в нём не вектор.</exception>
    /// <remarks>
    /// Оригинал: numpy.load — NumPy v2.5.4, numpy/lib/_npyio_impl.py, строка 312
    ///   https://github.com/numpy/numpy/blob/v2.5.4/numpy/lib/_npyio_impl.py#L312
    /// Вызывает: read_array — numpy/lib/_format_impl.py, строка 781
    /// </remarks>
    [Original("numpy.load", "numpy/lib/_npyio_impl.py", 312)]
    public static Vector<double> LoadVector(string path)
    {
        using FileStream stream = OpenNpy(path);
        NpyHeader header = NpyFormat.ReadHeader(stream);
        if (header.Shape.Length != 1)
            throw new ValueError(Messages.FileNotVector(header.ShapeText));
        double[] data = ReadDoubles(stream, header);
        return new Vector<double>(data, 0, data.Length, 1);
    }

    /// <summary>
    /// Вектор из файла .npy с числами того вида, что в файле: <c>Np.LoadVector&lt;long&gt;(path)</c> для int64.
    /// </summary>
    /// <param name="path">Путь к файлу.</param>
    /// <typeparam name="T">Вид чисел — как в файле.</typeparam>
    /// <returns>Вектор.</returns>
    /// <exception cref="ValueError">Файл испорчен, в нём не вектор или числа другого вида.</exception>
    /// <remarks>Оригинал: numpy.load — NumPy v2.5.4, numpy/lib/_npyio_impl.py, строка 312</remarks>
    [Original("numpy.load", "numpy/lib/_npyio_impl.py", 312)]
    public static Vector<T> LoadVector<T>(string path)
        where T : unmanaged
    {
        using FileStream stream = OpenNpy(path);
        NpyHeader header = NpyFormat.ReadHeader(stream);
        if (header.Shape.Length != 1)
            throw new ValueError(Messages.FileNotVector(header.ShapeText));
        T[] data = ReadExact<T>(stream, header);
        return new Vector<T>(data, 0, data.Length, 1);
    }

    /// <summary>
    /// Матрица из файла .npy — как <c>np.load(path)</c> с двумерным массивом. Числа приводятся к double, как
    /// <c>a.astype(np.float64)</c>. Числа в порядке Фортрана (по столбцам) не переставляются: матрица смотрит на них
    /// с нужными шагами, как и массив NumPy.
    /// </summary>
    /// <param name="path">Путь к файлу.</param>
    /// <returns>Матрица.</returns>
    /// <exception cref="ValueError">Файл испорчен (тексты NumPy) или в нём не матрица.</exception>
    /// <remarks>Оригинал: numpy.load — NumPy v2.5.4, numpy/lib/_npyio_impl.py, строка 312</remarks>
    [Original("numpy.load", "numpy/lib/_npyio_impl.py", 312)]
    public static Matrix<double> LoadMatrix(string path)
    {
        using FileStream stream = OpenNpy(path);
        NpyHeader header = NpyFormat.ReadHeader(stream);
        if (header.Shape.Length != 2)
            throw new ValueError(Messages.FileNotMatrix(header.ShapeText));
        return ToMatrix(ReadDoubles(stream, header), header);
    }

    /// <summary>Матрица из файла .npy с числами того вида, что в файле: <c>Np.LoadMatrix&lt;int&gt;(path)</c>.</summary>
    /// <param name="path">Путь к файлу.</param>
    /// <typeparam name="T">Вид чисел — как в файле.</typeparam>
    /// <returns>Матрица.</returns>
    /// <exception cref="ValueError">Файл испорчен, в нём не матрица или числа другого вида.</exception>
    /// <remarks>Оригинал: numpy.load — NumPy v2.5.4, numpy/lib/_npyio_impl.py, строка 312</remarks>
    [Original("numpy.load", "numpy/lib/_npyio_impl.py", 312)]
    public static Matrix<T> LoadMatrix<T>(string path)
        where T : unmanaged
    {
        using FileStream stream = OpenNpy(path);
        NpyHeader header = NpyFormat.ReadHeader(stream);
        if (header.Shape.Length != 2)
            throw new ValueError(Messages.FileNotMatrix(header.ShapeText));
        return ToMatrix(ReadExact<T>(stream, header), header);
    }

    /// <summary>
    /// Размеры массива в файле .npy — из заголовка, без чтения чисел: <c>[100]</c> у вектора, <c>[512, 512]</c> у
    /// матрицы (DECISIONS.md п. 24).
    /// </summary>
    /// <param name="path">Путь к файлу.</param>
    /// <returns>Размеры: по одному числу на измерение.</returns>
    /// <exception cref="ValueError">Файл испорчен (тексты NumPy).</exception>
    public static int[] GetFileShape(string path)
    {
        using FileStream stream = OpenNpy(path);
        return NpyFormat.ReadHeader(stream).Shape;
    }

    /// <summary>
    /// Записать вектор в файл .npy — как <c>np.save(path, v)</c>: файл байт в байт как у NumPy. Как и NumPy, без
    /// расширения <c>.npy</c> добавляет его к имени.
    /// </summary>
    /// <param name="path">Путь к файлу.</param>
    /// <param name="v">Вектор.</param>
    /// <typeparam name="T">Вид чисел.</typeparam>
    /// <remarks>
    /// Оригинал: numpy.save — NumPy v2.5.4, numpy/lib/_npyio_impl.py, строка 505
    ///   https://github.com/numpy/numpy/blob/v2.5.4/numpy/lib/_npyio_impl.py#L505
    /// Вызывает: write_array — numpy/lib/_format_impl.py, строка 703
    /// </remarks>
    [Original("numpy.save", "numpy/lib/_npyio_impl.py", 505)]
    public static void Save<T>(string path, Vector<T> v)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(v);
        var data = new T[v.Count];
        v.CopyTo(data, 0);
        using FileStream stream = File.Create(WithNpyExtension(path));
        NpyFormat.Write(stream, [v.Count], data);
    }

    /// <summary>
    /// Записать матрицу в файл .npy — как <c>np.save(path, m)</c>: числа по строкам, файл байт в байт как у NumPy. Без
    /// расширения <c>.npy</c> добавляет его к имени.
    /// </summary>
    /// <param name="path">Путь к файлу.</param>
    /// <param name="m">Матрица.</param>
    /// <typeparam name="T">Вид чисел.</typeparam>
    /// <remarks>Оригинал: numpy.save — NumPy v2.5.4, numpy/lib/_npyio_impl.py, строка 505</remarks>
    [Original("numpy.save", "numpy/lib/_npyio_impl.py", 505)]
    public static void Save<T>(string path, Matrix<T> m)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(m);
        Vector<T> flat = m.Flatten();
        var data = new T[flat.Count];
        flat.CopyTo(data, 0);
        using FileStream stream = File.Create(WithNpyExtension(path));
        NpyFormat.Write(stream, [m.RowCount, m.ColumnCount], data);
    }

    /// <summary>
    /// Открыть файл .npy, как <c>np.load</c> различает файлы по первым байтам: пустой — EOFError, архив .npz — к
    /// <c>Np.LoadNpz</c>, не .npy — текст NumPy про pickle (у NumPy это последняя попытка прочитать файл).
    /// </summary>
    private static FileStream OpenNpy(string path)
    {
        FileStream stream = File.OpenRead(path);
        try
        {
            // ref: numpy/lib/_npyio_impl.py#L456-L466 — первые 6 байт
            Span<byte> magic = stackalloc byte[6];
            int read = 0;
            while (read < magic.Length)
            {
                int n = stream.Read(magic[read..]);
                if (n == 0)
                    break;
                read += n;
            }
            if (read == 0)
                throw new EOFError("No data left in file");
            ReadOnlySpan<byte> start = magic[..read];
            // ref: numpy/lib/_npyio_impl.py#L458-L459, L467 — архив zip — это .npz
            if (start.StartsWith("PK\u0003\u0004"u8) || start.StartsWith("PK\u0005\u0006"u8))
                throw new ValueError(Messages.FileIsNpz());
            // ref: numpy/lib/_npyio_impl.py#L486-L491 — не .npy и не .npz: NumPy пробует pickle, а без allow_pickle — ошибка
            if (!start.SequenceEqual(NpyFormat.MagicPrefix))
            {
                throw new ValueError("This file contains pickled (object) data. If you trust the file you can load it " +
                                     "unsafely using the `allow_pickle=` keyword argument or `pickle.load()`.");
            }
            stream.Position = 0;
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    // ref: numpy/lib/_npyio_impl.py#L565-L567 — к имени без .npy расширение добавляется
    private static string WithNpyExtension(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.EndsWith(".npy", StringComparison.Ordinal) ? path : path + ".npy";
    }

    private static Matrix<T> ToMatrix<T>(T[] data, NpyHeader header)
        where T : unmanaged
    {
        int rows = header.Shape[0], columns = header.Shape[1];
        // ref: numpy/lib/_format_impl.py#L883-L887 — порядок Фортрана: массив перевёрнутых размеров, транспонированный
        return header.FortranOrder
            ? new Matrix<T>(data, 0, rows, columns, 1, rows)
            : new Matrix<T>(data, 0, rows, columns);
    }

    /// <summary>Числа того вида, что в файле; вид <typeparamref name="T"/> должен совпасть.</summary>
    private static T[] ReadExact<T>(Stream stream, NpyHeader header)
        where T : unmanaged
    {
        Type? type = NpyFormat.TypeOf(header.Descr);
        if (type is null)
            throw new ValueError(Messages.FileKindNotSupported(header.Descr));
        if (type != typeof(T))
            throw new ValueError(Messages.FileKindDiffers(header.Descr, KindName(typeof(T))));
        return NpyFormat.ReadData<T>(stream, header);
    }

    /// <summary>Числа файла, приведённые к double, как <c>a.astype(np.float64)</c>: дробные и целые; bool и комплексные — нет.</summary>
    private static double[] ReadDoubles(Stream stream, NpyHeader header)
    {
        Type? type = NpyFormat.TypeOf(header.Descr);
        if (type is null)
            throw new ValueError(Messages.FileKindNotSupported(header.Descr));
        if (type == typeof(double))
            return NpyFormat.ReadData<double>(stream, header);
        if (type == typeof(float))
            return Widen(NpyFormat.ReadData<float>(stream, header));
        if (type == typeof(Half))
            return Widen(NpyFormat.ReadData<Half>(stream, header));
        if (type == typeof(long))
            return Widen(NpyFormat.ReadData<long>(stream, header));
        if (type == typeof(int))
            return Widen(NpyFormat.ReadData<int>(stream, header));
        if (type == typeof(short))
            return Widen(NpyFormat.ReadData<short>(stream, header));
        if (type == typeof(sbyte))
            return Widen(NpyFormat.ReadData<sbyte>(stream, header));
        if (type == typeof(ulong))
            return Widen(NpyFormat.ReadData<ulong>(stream, header));
        if (type == typeof(uint))
            return Widen(NpyFormat.ReadData<uint>(stream, header));
        if (type == typeof(ushort))
            return Widen(NpyFormat.ReadData<ushort>(stream, header));
        if (type == typeof(byte))
            return Widen(NpyFormat.ReadData<byte>(stream, header));
        throw new ValueError(Messages.FileKindDiffers(header.Descr, "double"));
    }

    private static double[] Widen<T>(T[] values)
        where T : INumberBase<T>
    {
        var result = new double[values.Length];
        for (int i = 0; i < values.Length; i++)
            result[i] = double.CreateTruncating(values[i]);
        return result;
    }

    /// <summary>Имя вида чисел, как его пишут в C#: double, long, Complex…</summary>
    private static string KindName(Type type) => Type.GetTypeCode(type) switch
    {
        TypeCode.Double => "double",
        TypeCode.Single => "float",
        TypeCode.Int64 => "long",
        TypeCode.Int32 => "int",
        TypeCode.Int16 => "short",
        TypeCode.SByte => "sbyte",
        TypeCode.UInt64 => "ulong",
        TypeCode.UInt32 => "uint",
        TypeCode.UInt16 => "ushort",
        TypeCode.Byte => "byte",
        TypeCode.Boolean => "bool",
        _ => type.Name,
    };
}
