using System;
using System.Linq;
using Xunit;

namespace DotSaxs.ConventionsTests;

/// <summary>Каждый проект из src — в одной категории <see cref="Projects"/>: ни одна сборка не выпадает из проверок.</summary>
public sealed class ProjectsTests
{
    [Fact]
    public void EveryProjectInSourceIsChecked()
    {
        string[] known = [.. Projects.Port, .. Projects.Library, .. Projects.Service];
        Assert.Equal(known.Length, known.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(known.Order(StringComparer.Ordinal), Projects.InSource().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void PortAndLibraryLoad()
    {
        Assert.All(Projects.Load([.. Projects.Port, .. Projects.Library]), a => Assert.NotNull(a.GetName().Name));
    }
}
