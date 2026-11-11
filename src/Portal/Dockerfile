# Портал (DECISIONS.md п. 18): образ только с готовой программой — её заранее собирает
#   dotnet publish src/Portal/Portal.csproj -c Release -o publish/portal
# SDK в образ не попадает: сборка идёт тем же .NET, что и везде (global.json). Эталоны кладутся рядом, при запуске
# портал загружает их в PostgreSQL.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY publish/portal/ ./
COPY tests/references/ ./references/
ENTRYPOINT ["dotnet", "DotSaxs.Portal.dll"]
