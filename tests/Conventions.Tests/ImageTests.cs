using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace DotSaxs.ConventionsTests;

/// <summary>
/// Образы Docker закреплены по digest (DECISIONS.md п. 3), и у одного образа digest везде один: в CI, в проверке
/// окружения, в Dockerfile и compose. Иначе, обновив digest в одном месте, эталоны считали бы на другом образе.
/// </summary>
public sealed partial class ImageTests
{
    [Fact]
    public void EachImageHasOneDigestEverywhere()
    {
        var pins = Pins(NamesTable.RepoRoot()).ToArray();
        Assert.Contains(pins, p => p.Image.StartsWith("python:", StringComparison.Ordinal));
        Assert.Contains(pins, p => p.Image.StartsWith("postgres:", StringComparison.Ordinal));
        string[] conflicts = pins
            .GroupBy(p => p.Image, StringComparer.Ordinal)
            .Where(g => g.Select(p => p.Digest).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(g => $"{g.Key}: " + string.Join("; ", g.Select(p => $"{p.File} — {p.Digest[..12]}…")))
            .ToArray();
        if (conflicts.Length > 0)
            Assert.Fail("У образа разные digest:\n" + string.Join("\n", conflicts));
    }

    [Fact]
    public void FindsPinsInText()
    {
        string digest = new('a', 64);
        Assert.Equal(
            new[] { "python:3.13-slim-trixie", "postgres:17", "mcr.microsoft.com/dotnet/aspnet:10.0" },
            Pin().Matches($"ARG PYTHON_IMAGE=python:3.13-slim-trixie@sha256:{digest}\n" +
                          $"  image: postgres:17@sha256:{digest}\nFROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:{digest}")
                .Select(m => m.Groups["image"].Value));
    }

    /// <summary>Все «образ@sha256:digest» в workflow, Dockerfile и compose репозитория.</summary>
    private static IEnumerable<(string Image, string Digest, string File)> Pins(string root) =>
        from file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        let relative = Path.GetRelativePath(root, file).Replace('\\', '/')
        where !relative.Split('/').Any(part => part is ".git" or "bin" or "obj" or "node_modules")
        let name = Path.GetFileName(file)
        where name == "Dockerfile" || name.EndsWith(".yml", StringComparison.Ordinal) || name.EndsWith(".yaml", StringComparison.Ordinal)
        from Match m in Pin().Matches(File.ReadAllText(file))
        select (m.Groups["image"].Value, m.Groups["digest"].Value, relative);

    [GeneratedRegex(@"(?<image>[a-z0-9][a-z0-9._/-]*:[A-Za-z0-9._-]+)@sha256:(?<digest>[0-9a-f]{64})")]
    private static partial Regex Pin();
}
