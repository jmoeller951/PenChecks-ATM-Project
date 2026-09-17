FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY AtmApp.slnx .
COPY src/AtmApp.Domain/AtmApp.Domain.csproj src/AtmApp.Domain/
COPY src/AtmApp.Application/AtmApp.Application.csproj src/AtmApp.Application/
COPY src/AtmApp.Infrastructure/AtmApp.Infrastructure.csproj src/AtmApp.Infrastructure/
COPY src/AtmApp.Web/AtmApp.Web.csproj src/AtmApp.Web/
RUN dotnet restore src/AtmApp.Web/AtmApp.Web.csproj

COPY src/ src/
# Deliberately not passing --no-restore: the earlier restore (against .csproj files only, before
# component/wwwroot content existed) caches a static web assets manifest missing the
# framework-provided _framework/blazor.web.js. Restoring again here (fast; packages are already
# cached) lets publish recompute that manifest correctly against the full source tree.
RUN dotnet publish src/AtmApp.Web/AtmApp.Web.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# SQLite database file and ASP.NET Data Protection keys live here; mount a volume at this
# path to persist them across container restarts (see docker-compose.yml).
RUN mkdir -p /app/data/keys
ENV ConnectionStrings__Sqlite="Data Source=/app/data/atm.db"

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=5 \
  CMD wget --spider -q http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "AtmApp.Web.dll"]
