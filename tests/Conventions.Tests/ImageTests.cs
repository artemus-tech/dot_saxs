using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace DotSaxs.ConventionsTests;

/// <summary>
/// Образы Docker закреплены по digest (DECISIONS.md п. 3): каждая строка <c>image:</c> и <c>FROM</c> — с digest, и у
/// одного образа тег и digest везде одни — в CI, в проверке окружения, в Dockerfile и compose. Иначе, обновив образ в
/// одном месте, эталоны считали бы на другом.
/// </summary>
public sealed partial class ImageTests
{
    [Fact]
    public void EachImageIsPinnedTheSameEverywhere()
    {
        string root = NamesTable.RepoRoot();
        (string File, string Text)[] files = ImageFiles(root).ToArray();
        var pins = files.SelectMany(f => Pins(f.Text).Select(p => (p.Name, p.Reference, f.File))).ToArray();
        Assert.Contains(pins, p => p.Name == "python");
        Assert.Contains(pins, p => p.Name == "postgres");
        string[] conflicts = pins
            .GroupBy(p => p.Name, StringComparer.Ordinal)
            .Where(g => g.Select(p => p.Reference).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(g => $"{g.Key}: " + string.Join("; ", g.Select(p => $"{p.File} — {Short(p.Reference)}")))
            .ToArray();
        string[] unpinned = files.SelectMany(f => Unpinned(f.Text).Select(line => $"{f.File}: {line}")).ToArray();
        if (conflicts.Length > 0 || unpinned.Length > 0)
            Assert.Fail(string.Join("\n", conflicts.Select(c => "разные тег или digest — " + c).Concat(unpinned.Select(u => "без digest — " + u))));
    }

    [Fact]
    public void FindsPinsAndUnpinnedLines()
    {
        string digest = new('a', 64);
        string text = $"ARG PYTHON_IMAGE=python:3.13-slim-trixie@sha256:{digest}\nFROM ${{PYTHON_IMAGE}}\n" +
                      $"  image: postgres:17@sha256:{digest}\nFROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:{digest}\n" +
                      "    image: postgres:18\nFROM --platform=linux/amd64 python:3.14\n";
        Assert.Equal(
            new[] { "python", "postgres", "mcr.microsoft.com/dotnet/aspnet" },
            Pins(text).Select(p => p.Name));
        Assert.Equal(new[] { "image: postgres:18", "FROM --platform=linux/amd64 python:3.14" }, Unpinned(text));
    }

    /// <summary>Workflow, Dockerfile и compose репозитория (без bin, obj и .git).</summary>
    private static IEnumerable<(string File, string Text)> ImageFiles(string root) =>
        from file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        let relative = Path.GetRelativePath(root, file).Replace('\\', '/')
        where !relative.Split('/').Any(part => part is ".git" or "bin" or "obj" or "node_modules")
        let name = Path.GetFileName(file)
        where name == "Dockerfile" || name.EndsWith(".yml", StringComparison.Ordinal) || name.EndsWith(".yaml", StringComparison.Ordinal)
        select (relative, File.ReadAllText(file));

    /// <summary>Все «образ:тег@sha256:digest» в тексте: имя образа без тега и полная ссылка.</summary>
    private static IEnumerable<(string Name, string Reference)> Pins(string text) =>
        Pin().Matches(text).Select(m => (m.Groups["name"].Value, m.Value));

    /// <summary>Строки <c>image:</c> и <c>FROM</c> без digest; <c>FROM ${ARG}</c> — подстановка, её digest — в ARG.</summary>
    private static IEnumerable<string> Unpinned(string text) =>
        from line in text.Split('\n')
        let trimmed = line.Trim()
        let match = ImageLine().Match(trimmed)
        where match.Success && !match.Groups["image"].Value.StartsWith('$') && !match.Groups["image"].Value.Contains("@sha256:", StringComparison.Ordinal)
        select trimmed;

    private static string Short(string reference) => reference[..(reference.IndexOf("@sha256:", StringComparison.Ordinal) + 20)] + "…";

    [GeneratedRegex(@"(?<name>[a-z0-9][a-z0-9._/-]*):[A-Za-z0-9._-]+@sha256:[0-9a-f]{64}")]
    private static partial Regex Pin();

    [GeneratedRegex(@"^(?:FROM(?:\s+--\S+)*|-?\s*image:)\s+[""']?(?<image>[^\s""']+)")]
    private static partial Regex ImageLine();
}
