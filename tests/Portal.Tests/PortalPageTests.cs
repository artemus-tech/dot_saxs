using System;
using System.Threading.Tasks;
using DotSaxs.Testing;
using Microsoft.Playwright;
using Xunit;

namespace DotSaxs.Portal.Tests;

/// <summary>
/// Сквозной срез (DECISIONS.md п. 18): эталоны NumPy в PostgreSQL → страница портала → браузер. Портал запущен в
/// docker compose (compose.yaml), адрес — переменная PORTAL_URL.
/// </summary>
public sealed class PortalPageTests
{
    [Fact]
    public async Task SumPageShowsCallOriginalAndReferenceCases()
    {
        string url = PortalUrl();
        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync();
        IPage page = await browser.NewPageAsync();
        IResponse? response = await page.GotoAsync(url + "/f/numpy.sum");
        Assert.Equal(200, response?.Status);
        Assert.Equal("numpy.sum", await page.Locator("h1").InnerTextAsync());
        Assert.Equal("a.Sum()", await page.Locator("#call").InnerTextAsync());
        Assert.Equal(
            "https://github.com/numpy/numpy/blob/v2.5.4/numpy/_core/fromnumeric.py#L2343",
            await page.Locator("#original").GetAttributeAsync("href"));
        Assert.Contains("double Sum(this Vector<double> v)", await page.Locator("#members").InnerTextAsync(), StringComparison.Ordinal);
        // Все кейсы эталона numpy.sum из базы — столько же, сколько в tests/references; один из них — ошибка NumPy (AxisError).
        Assert.Equal(References.Load("numpy.sum").Count, await page.Locator("#cases tbody tr").CountAsync());
        Assert.Contains("ошибка AxisError", await page.Locator("#cases").InnerTextAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task HomeListsPortedFunctions()
    {
        string url = PortalUrl();
        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync();
        IPage page = await browser.NewPageAsync();
        await page.GotoAsync(url + "/");
        Assert.Equal(1, await page.Locator("#functions a[href='/f/numpy.sum']").CountAsync());
        Assert.Equal(1, await page.Locator("#functions a[href='/f/numpy.errstate']").CountAsync());
    }

    /// <summary>Адрес портала. Без него тест пропускается, но в задании CI portal — ошибка.</summary>
    private static string PortalUrl()
    {
        string? url = Environment.GetEnvironmentVariable("PORTAL_URL");
        if (string.IsNullOrEmpty(url))
        {
            const string why = "PORTAL_URL не задан: портал не запущен (compose.yaml)";
            if (Environment.GetEnvironmentVariable("DOTSAXS_PORTAL_REQUIRED") == "true")
                Assert.Fail(why);
            Assert.Skip(why);
        }
        return url!.TrimEnd('/');
    }
}
