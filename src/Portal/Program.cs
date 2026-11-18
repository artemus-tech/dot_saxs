// Портал документации — сквозной срез этапа 2 (DECISIONS.md п. 18): при запуске база получает схему и эталоны,
// страница функции показывает наш вызов, описание, ссылку на оригинал и эталоны из базы.
using System;
using System.IO;
using DotSaxs.Data;
using DotSaxs.Portal;
using DotSaxs.Portal.Components;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
string connection = builder.Configuration.GetConnectionString("DotSaxs")
    ?? throw new InvalidOperationException("нет строки подключения ConnectionStrings:DotSaxs (переменная ConnectionStrings__DotSaxs)");
builder.Services.AddDbContextFactory<DotSaxsDb>(options => options.UseNpgsql(connection));
builder.Services.AddSingleton(ApiCatalog.Load(AppContext.BaseDirectory));
builder.Services.AddRazorComponents();

WebApplication app = builder.Build();
await using (DotSaxsDb db = await app.Services.GetRequiredService<IDbContextFactory<DotSaxsDb>>().CreateDbContextAsync())
{
    await db.Database.MigrateAsync();
    string references = builder.Configuration["References"] ?? Path.Combine(AppContext.BaseDirectory, "references");
    await ReferenceImporter.ImportAsync(db, references);
}

app.UseAntiforgery();
app.MapGet("/health", () => "ok");
app.MapRazorComponents<App>();
await app.RunAsync();
