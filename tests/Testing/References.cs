using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace DotSaxs.Testing;

/// <summary>Значение из эталона: аргумент, результат или их часть (tools/reference/README.md).</summary>
public abstract record ReferenceValue;

/// <summary>Массив из файла .npy; <paramref name="Scalar"/> — в Python это был скаляр NumPy (0-мерный массив).</summary>
public sealed record NpyValue(NpyArray Value, bool Scalar) : ReferenceValue;

/// <summary>Целое Python.</summary>
public sealed record IntValue(BigInteger Value) : ReferenceValue;

/// <summary>Число Python float (точно, по битам).</summary>
public sealed record FloatValue(double Value) : ReferenceValue;

/// <summary>Число Python complex (точно, по битам).</summary>
public sealed record ComplexValue(Complex Value) : ReferenceValue;

/// <summary>True или False.</summary>
public sealed record BoolValue(bool Value) : ReferenceValue;

/// <summary>Строка Python.</summary>
public sealed record StrValue(string Value) : ReferenceValue;

/// <summary>None.</summary>
public sealed record NoneValue : ReferenceValue;

/// <summary>Кортеж; у именованного кортежа (например, результат <c>eigh</c>) — имена полей.</summary>
public sealed record TupleValue(IReadOnlyList<ReferenceValue> Items, IReadOnlyList<string>? Fields) : ReferenceValue;

/// <summary>Список Python.</summary>
public sealed record ListValue(IReadOnlyList<ReferenceValue> Items) : ReferenceValue;

/// <summary>Вид чисел NumPy, например <c>&lt;f4</c>.</summary>
public sealed record DTypeValue(string Descr) : ReferenceValue;

/// <summary>Аргумент вызова: имя параметра в Python и значение.</summary>
public sealed record ReferenceArgument(string Name, ReferenceValue Value);

/// <summary>Исключение Python: тип, модуль и текст — для негативных тестов (SPEC.md п. 8).</summary>
public sealed record ReferenceError(string Type, string Module, string Message);

/// <summary>Предупреждение при вызове, например <c>RuntimeWarning: overflow encountered in exp</c>.</summary>
public sealed record ReferenceWarning(string Type, string Message);

/// <summary>Один вызов функции NumPy или SciPy: входы, результат или ошибка, предупреждения, уровень совпадения.</summary>
public sealed record ReferenceCase(
    string Python,
    string Name,
    IReadOnlyList<ReferenceArgument> Args,
    IReadOnlyDictionary<string, ReferenceValue> Kwargs,
    MatchLevel Match,
    ReferenceValue? Result,
    ReferenceError? Error,
    IReadOnlyList<ReferenceWarning> Warnings)
{
    /// <summary>Аргумент по имени параметра Python: позиционный или именованный.</summary>
    public ReferenceValue Argument(string name) =>
        Args.FirstOrDefault(a => a.Name == name)?.Value
        ?? Kwargs.GetValueOrDefault(name)
        ?? throw new KeyNotFoundException($"{this}: нет аргумента «{name}»");

    /// <summary>Массив-аргумент по имени параметра Python.</summary>
    public NpyArray Array(string name) =>
        Argument(name) is NpyValue npy ? npy.Value : throw new InvalidCastException($"{this}: аргумент «{name}» — не массив");

    /// <summary>Результат-массив (или скаляр NumPy).</summary>
    public NpyArray ResultArray() =>
        Result is NpyValue npy ? npy.Value : throw new InvalidCastException($"{this}: результат — не массив ({Result?.GetType().Name ?? "ошибка"})");

    /// <inheritdoc/>
    public override string ToString() => $"{Python}/{Name}";
}

/// <summary>Сведения о том, где посчитаны эталоны (environment.json).</summary>
public sealed record ReferenceEnvironment(
    string Python,
    string Numpy,
    string Scipy,
    IReadOnlyList<string> SimdFound,
    IReadOnlyList<OpenBlasInfo> OpenBlas,
    IReadOnlyDictionary<string, string> Params);

/// <summary>OpenBLAS в эталонном контейнере: из колеса numpy или scipy.</summary>
public sealed record OpenBlasInfo(string Library, string Version, string Architecture, int Threads);

/// <summary>Эталоны из tests/references (tools/reference/README.md).</summary>
public static class References
{
    /// <summary>Корень репозитория — каталог с DotSaxs.slnx выше каталога тестов.</summary>
    public static string RepoRoot { get; } = FindRepoRoot();

    /// <summary>Каталог эталонов: tests/references или каталог из переменной DOTSAXS_REFERENCES.</summary>
    public static string Root { get; } =
        Environment.GetEnvironmentVariable("DOTSAXS_REFERENCES") is { Length: > 0 } custom
            ? custom
            : Path.Combine(RepoRoot, "tests", "references");

    /// <summary>Функции, для которых есть эталоны: numpy.add, numpy.linalg.solve…</summary>
    public static IReadOnlyList<string> Functions() =>
        Directory.GetDirectories(Root)
            .Where(d => File.Exists(Path.Combine(d, "cases.json")))
            .Select(d => Path.GetFileName(d))
            .Order(StringComparer.Ordinal)
            .ToArray();

    /// <summary>Все кейсы функции вместе с массивами из файлов .npy.</summary>
    public static IReadOnlyList<ReferenceCase> Load(string python)
    {
        string folder = Path.Combine(Root, python);
        string file = Path.Combine(folder, "cases.json");
        if (!File.Exists(file))
            throw new FileNotFoundException($"эталонов для {python} нет: {file}");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));
        JsonElement root = doc.RootElement;
        string declared = root.GetProperty("python").GetString()!;
        if (declared != python)
            throw new InvalidDataException($"{file}: внутри эталоны для {declared}, а не для {python}");
        return root.GetProperty("cases").EnumerateArray().Select(c => Case(python, folder, c)).ToArray();
    }

    /// <summary>Один кейс по имени.</summary>
    public static ReferenceCase Case(string python, string name) =>
        Load(python).SingleOrDefault(c => c.Name == name)
        ?? throw new KeyNotFoundException($"у {python} нет кейса «{name}»");

    /// <summary>environment.json: версии и параметры процессора, с которыми посчитаны эталоны.</summary>
    public static ReferenceEnvironment ReadEnvironment()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "environment.json")));
        JsonElement e = doc.RootElement;
        return new ReferenceEnvironment(
            e.GetProperty("python").GetString()!,
            e.GetProperty("numpy").GetString()!,
            e.GetProperty("scipy").GetString()!,
            e.GetProperty("numpy_simd").GetProperty("found").EnumerateArray().Select(x => x.GetString()!).ToArray(),
            e.GetProperty("openblas").EnumerateArray().Select(b => new OpenBlasInfo(
                b.GetProperty("library").GetString()!,
                b.GetProperty("version").GetString()!,
                b.GetProperty("architecture").GetString()!,
                b.GetProperty("threads").GetInt32())).ToArray(),
            e.GetProperty("params").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal));
    }

    private static ReferenceCase Case(string python, string folder, JsonElement c)
    {
        string name = c.GetProperty("name").GetString()!;
        try
        {
            return new ReferenceCase(
                python,
                name,
                c.GetProperty("args").EnumerateArray()
                    .Select(a => new ReferenceArgument(a.GetProperty("name").GetString()!, Value(a.GetProperty("value"), folder)))
                    .ToArray(),
                c.GetProperty("kwargs").EnumerateObject()
                    .ToDictionary(p => p.Name, p => Value(p.Value, folder), StringComparer.Ordinal),
                MatchLevel.FromJson(c.GetProperty("match")),
                c.TryGetProperty("result", out JsonElement result) ? Value(result, folder) : null,
                c.TryGetProperty("error", out JsonElement error)
                    ? new ReferenceError(
                        error.GetProperty("type").GetString()!,
                        error.GetProperty("module").GetString()!,
                        error.GetProperty("message").GetString()!)
                    : null,
                c.GetProperty("warnings").EnumerateArray()
                    .Select(w => new ReferenceWarning(w.GetProperty("type").GetString()!, w.GetProperty("message").GetString()!))
                    .ToArray());
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException($"{python}/{name}: неверное описание в cases.json — {e.Message}", e);
        }
    }

    private static ReferenceValue Value(JsonElement e, string folder)
    {
        if (e.TryGetProperty("npy", out JsonElement npy))
            return new NpyValue(Npy.Read(Path.Combine(folder, npy.GetString()!)),
                e.TryGetProperty("scalar", out JsonElement scalar) && scalar.GetBoolean());
        if (e.TryGetProperty("int", out JsonElement i))
            return new IntValue(BigInteger.Parse(i.GetRawText(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture));
        if (e.TryGetProperty("float", out JsonElement f))
            return new FloatValue(Bits(f));
        if (e.TryGetProperty("complex", out JsonElement c))
            return new ComplexValue(new Complex(Bits(c[0]), Bits(c[1])));
        if (e.TryGetProperty("bool", out JsonElement b))
            return new BoolValue(b.GetBoolean());
        if (e.TryGetProperty("str", out JsonElement s))
            return new StrValue(s.GetString()!);
        if (e.TryGetProperty("none", out _))
            return new NoneValue();
        if (e.TryGetProperty("tuple", out JsonElement t))
            return new TupleValue(
                t.EnumerateArray().Select(x => Value(x, folder)).ToArray(),
                e.TryGetProperty("fields", out JsonElement names) ? names.EnumerateArray().Select(n => n.GetString()!).ToArray() : null);
        if (e.TryGetProperty("list", out JsonElement l))
            return new ListValue(l.EnumerateArray().Select(x => Value(x, folder)).ToArray());
        if (e.TryGetProperty("dtype", out JsonElement d))
            return new DTypeValue(d.GetString()!);
        throw new InvalidDataException($"неизвестное значение в cases.json: {e.GetRawText()}");
    }

    /// <summary>Биты double в шестнадцатеричном виде («3ff0000000000000») → число.</summary>
    private static double Bits(JsonElement hex) =>
        BitConverter.UInt64BitsToDouble(ulong.Parse(hex.GetString()!, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));

    private static string FindRepoRoot()
    {
        for (string? dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
            if (File.Exists(Path.Combine(dir, "DotSaxs.slnx")))
                return dir;
        throw new DirectoryNotFoundException("Не найден корень репозитория (DotSaxs.slnx) выше " + AppContext.BaseDirectory);
    }
}
