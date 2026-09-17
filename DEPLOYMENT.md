# Deployment

How to build and run the PenChecks ATM app as a container, via either `docker-compose.yml`
(recommended) or the `Dockerfile` directly.

## 1. Prerequisites

- Docker Desktop (or another Docker Engine) installed and running.
- Nothing else — the multi-stage `Dockerfile` builds the app from source using the .NET SDK
  image, so a local .NET install isn't required to deploy.

## 2. Option A: docker-compose (recommended)

```
docker compose up -d --build
```

- Builds the image from [`Dockerfile`](Dockerfile) and starts it in the background.
- Serves the app at **http://localhost:8080**.
- Persists the SQLite database and ASP.NET Data Protection keys in the named volume
  `atm-data`, mounted at `/app/data` inside the container, so data survives container
  restarts/recreation (but not `docker compose down -v`, which deletes named volumes).

Common follow-ups:

```
docker compose logs -f atm-app     # tail logs
docker compose up -d --build       # rebuild after a code change and swap the running container
docker compose down                # stop and remove the container (keeps the atm-data volume)
docker compose down -v             # stop and remove the container AND delete atm-data (data loss)
```

## 3. Option B: plain Dockerfile

```
docker build -t atmapp-web:latest .
docker run -d --name atm-app -p 8080:8080 -v atm-data:/app/data atmapp-web:latest
```

The `-v atm-data:/app/data` volume mount is what makes data persist across
`docker run`/`docker rm` cycles — omit it only for a throwaway/ephemeral run.

To rebuild after changing source and swap the running container:

```
docker build -t atmapp-web:latest .
docker rm -f atm-app
docker run -d --name atm-app -p 8080:8080 -v atm-data:/app/data atmapp-web:latest
```

## 4. Verifying a deployment

```
curl -I http://localhost:8080/health
```

Expect `200 OK`. The container also has a built-in `HEALTHCHECK` (`docker ps` shows
`healthy`/`unhealthy`) that hits this same endpoint every 30s.

To confirm the Blazor client script is actually being served (see §6 for why this matters):

```
curl -s http://localhost:8080/ | grep -o 'blazor\.web[^"]*\.js'
curl -I "http://localhost:8080/_framework/$(curl -s http://localhost:8080/ | grep -o 'blazor\.web[^"]*\.js')"
```

Expect the second command to return `200`, not `404`.

## 5. Configuration

| Setting | How | Default |
|---|---|---|
| Port | `ASPNETCORE_URLS` env var (baked into the image as `http://+:8080`) | `8080` |
| Database | `ConnectionStrings__Sqlite` env var | `Data Source=/app/data/atm.db` |
| Data Protection keys | Fixed at `/app/data/keys` in `Program.cs` | n/a |
| Database provider | `Database:Provider` in `appsettings.json` (`Sqlite` or `MySql`) | `Sqlite` |

Override any `ConnectionStrings__*` or other `appsettings.json` value with an environment
variable of the same name (double-underscore for nesting), e.g.:

```
docker run -d -p 8080:8080 -e ConnectionStrings__Sqlite="Data Source=/app/data/atm.db" atmapp-web:latest
```

## 6. Known Dockerfile gotcha (already fixed, keep in mind for future edits)

The `Dockerfile` restores NuGet packages against just the `.csproj` files first (before the
full `src/` is copied in), to get better Docker layer caching on rebuilds. That's fine for
package restore, but if the final `dotnet publish` step is ever changed to pass
`--no-restore`, the static web assets manifest gets cached from that early, source-less
restore and **silently omits the framework-provided `_framework/blazor.web.js`** — the app
builds and starts fine, but the Blazor client fails to load in the browser (404 on
`blazor.web.js`), and no amount of browser cache-clearing or image rebuilding fixes it,
because the file is genuinely missing from the published output.

**Do not add `--no-restore` to the `dotnet publish` line in the `Dockerfile`.** The extra
restore it triggers is fast (packages are already cached from the earlier restore layer) and
is what makes the static web assets manifest complete.
