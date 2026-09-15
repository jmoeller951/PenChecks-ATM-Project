FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY AtmApp.slnx .
COPY src/AtmApp.Domain/AtmApp.Domain.csproj src/AtmApp.Domain/
COPY src/AtmApp.Application/AtmApp.Application.csproj src/AtmApp.Application/
COPY src/AtmApp.Infrastructure/AtmApp.Infrastructure.csproj src/AtmApp.Infrastructure/
COPY src/AtmApp.Web/AtmApp.Web.csproj src/AtmApp.Web/
RUN dotnet restore src/AtmApp.Web/AtmApp.Web.csproj

COPY src/ src/
RUN dotnet publish src/AtmApp.Web/AtmApp.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# SQLite database file lives here; mount a volume at this path to persist it across container
# restarts (see docker-compose.yml).
RUN mkdir -p /app/data
ENV ConnectionStrings__Sqlite="Data Source=/app/data/atm.db"

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "AtmApp.Web.dll"]
