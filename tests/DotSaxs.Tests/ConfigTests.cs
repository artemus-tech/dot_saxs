using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotSaxs.Testing;
using Xunit;

namespace DotSaxs.Tests;

/// <summary>
/// Тесты, которые меняют конфиг программы (<c>AppContext.SetData</c>) — он общий на весь процесс, поэтому они идут
/// одни, после остальных.
/// </summary>
[CollectionDefinition(nameof(ConfigCollection), DisableParallelization = true)]
public sealed class ConfigCollection;

/// <summary>Блок Np.ErrState и конфиг программы: что сильнее чего (DECISIONS.md п. 32, 33).</summary>
[Collection(nameof(ConfigCollection))]
public sealed class ErrStateTests
{
    private static readonly Vector<double> InfMinusInf = new[] { double.PositiveInfinity, double.NegativeInfinity }.ToVector();
    private static readonly Vector<double> TooBig = new[] { double.MaxValue, double.MaxValue }.ToVector();

    [Fact]
    public void ByDefaultOverflowAndInvalidThrowWithNumPyText()
    {
        Assert.Equal("invalid value encountered in reduce", Assert.Throws<FloatingPointError>(() => InfMinusInf.Sum()).Message);
        Assert.Equal("overflow encountered in reduce", Assert.Throws<FloatingPointError>(() => TooBig.Sum()).Message);
        Assert.IsAssignableFrom<ArithmeticException>(Assert.Throws<FloatingPointError>(() => TooBig.Sum()));
    }

    [Fact]
    public void BlockIgnoresAndThenRestores()
    {
        using (Np.ErrState(all: "ignore"))
        {
            Assert.True(double.IsNaN(InfMinusInf.Sum()));
            Assert.Equal(double.PositiveInfinity, TooBig.Sum());
            using (Np.ErrState(invalid: "raise"))
            {
                Assert.Throws<FloatingPointError>(() => InfMinusInf.Sum());
                Assert.Equal(double.PositiveInfinity, TooBig.Sum());
            }
            Assert.True(double.IsNaN(InfMinusInf.Sum()));
        }
        Assert.Throws<FloatingPointError>(() => InfMinusInf.Sum());
    }

    [Fact]
    public void FirstErrorInNumPyOrderIsReported()
    {
        // max + max = inf (переполнение), потом inf + (−inf) = nan (недопустимое значение): NumPy называет переполнение.
        Vector<double> both = new[] { double.MaxValue, double.MaxValue, double.NegativeInfinity }.ToVector();
        Assert.Equal("overflow encountered in reduce", Assert.Throws<FloatingPointError>(() => both.Sum()).Message);
        using (Np.ErrState(over: "ignore"))
            Assert.Equal("invalid value encountered in reduce", Assert.Throws<FloatingPointError>(() => both.Sum()).Message);
    }

    [Fact]
    public void NanInDataIsNotAnError()
    {
        // nan во входе — не ошибка: процессор не отмечает nan + x, NumPy тоже молчит.
        Assert.True(double.IsNaN(new[] { 1.0, double.NaN }.ToVector().Sum()));
        Assert.Equal(double.PositiveInfinity, new[] { 1.0, double.PositiveInfinity }.ToVector().Sum());
    }

    [Fact]
    public void UnknownModeIsValueErrorLikeNumPy()
    {
        Assert.Equal("invalid error mode 'foo'", Assert.Throws<ValueError>(() => Np.ErrState(divide: "foo")).Message);
        Assert.Contains("'warn'", Assert.Throws<NotSupportedException>(() => Np.ErrState(all: "warn")).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigChangesDefaultsAndBlockIsStronger()
    {
        try
        {
            AppContext.SetData("DotSaxs.ErrState.Invalid", "ignore");
            Assert.True(double.IsNaN(InfMinusInf.Sum()));
            Assert.Throws<FloatingPointError>(() => TooBig.Sum());
            using (Np.ErrState(invalid: "raise"))
                Assert.Throws<FloatingPointError>(() => InfMinusInf.Sum());
        }
        finally
        {
            AppContext.SetData("DotSaxs.ErrState.Invalid", null);
        }
        Assert.Throws<FloatingPointError>(() => InfMinusInf.Sum());
    }

    [Fact]
    public void BadModeInConfigIsReported()
    {
        try
        {
            AppContext.SetData("DotSaxs.ErrState.Over", "loud");
            Assert.Equal("invalid error mode 'loud'", Assert.Throws<ValueError>(() => TooBig.Sum()).Message);
        }
        finally
        {
            AppContext.SetData("DotSaxs.ErrState.Over", null);
        }
    }
}

/// <summary>Свои тексты: оба языка у каждого, язык — из конфига (DECISIONS.md п. 32, 33).</summary>
[Collection(nameof(ConfigCollection))]
public sealed partial class MessagesTests
{
    [Fact]
    public void EveryTextHasBothLanguagesWithSamePlaceholders()
    {
        using JsonDocument file = JsonDocument.Parse(File.ReadAllText(Path.Combine(References.RepoRoot, "src", "DotSaxs", "Messages.json")));
        var errors = new List<string>();
        foreach (JsonProperty text in file.RootElement.EnumerateObject())
        {
            string languages = string.Join(", ", text.Value.EnumerateObject().Select(l => l.Name).Order(StringComparer.Ordinal));
            if (languages != "en, ru")
                errors.Add($"{text.Name}: языки {languages}, нужны en и ru");
            else if (!Placeholders(text.Value.GetProperty("en").GetString()!).SequenceEqual(Placeholders(text.Value.GetProperty("ru").GetString()!)))
                errors.Add($"{text.Name}: места для чисел в en и ru разные");
        }
        Assert.Empty(errors);
    }

    [Fact]
    public void EveryTextIsUsedAndEveryUseHasText()
    {
        string[] methods = typeof(Messages).GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(m => m.ReturnType == typeof(string) && m.Name is not ("Get" or "Translate") && !m.IsSpecialName)
            .Select(m => m.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(methods, Messages.All.Keys.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void LanguageComesFromConfig()
    {
        Vector<double> v = new[] { 1.0 }.ToVector();
        Assert.Equal("index 5 is outside the vector (length 1)", Assert.Throws<IndexError>(() => v[5]).Message);
        try
        {
            AppContext.SetData("DotSaxs.Language", "ru");
            Assert.Equal("номер 5 за пределами вектора (длина 1)", Assert.Throws<IndexError>(() => v[5]).Message);
            // Тексты перенесённых функций не переводятся: они как в NumPy.
            using (Np.ErrState(all: "raise"))
            {
                Vector<double> nan = new[] { double.PositiveInfinity, double.NegativeInfinity }.ToVector();
                Assert.Equal("invalid value encountered in reduce", Assert.Throws<FloatingPointError>(() => nan.Sum()).Message);
            }
            AppContext.SetData("DotSaxs.Language", "de");
            Assert.Equal("index 5 is outside the vector (length 1)", Assert.Throws<IndexError>(() => v[5]).Message);
        }
        finally
        {
            AppContext.SetData("DotSaxs.Language", null);
        }
    }

    private static IEnumerable<string> Placeholders(string text) =>
        Placeholder().Matches(text).Select(m => m.Value).Order(StringComparer.Ordinal);

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex Placeholder();
}
