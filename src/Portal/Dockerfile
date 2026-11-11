# Портал (DECISIONS.md п. 18): образ только с готовой программой — её заранее собирает
#   dotnet publish src/Portal/Portal.csproj -c Release -o publish/portal
# SDK в образ не попадает: сборка идёт тем же .NET, что и везде (global.json). Эталоны кладутся рядом, при запуске
# портал загружает их в PostgreSQL.
# Базовый образ закреплён по digest (DECISIONS.md п. 3).
FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:bfd912c83295ce66d4b706ebb8541b9f14259b0e76fbb88b2751f58ce2399d2e
WORKDIR /app
COPY publish/portal/ ./
COPY tests/references/ ./references/
ENTRYPOINT ["dotnet", "DotSaxs.Portal.dll"]
