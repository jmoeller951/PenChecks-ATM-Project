# PenChecks ATM

A single-user ATM web app: two seeded accounts, deposit/withdraw/transfer, and a running
transaction history. Built to the brief in [`ATM_Exercise_Design_Doc.md`](ATM_Exercise_Design_Doc.md).

**Stack**: Blazor Server, EF Core, SQLite (local). Targets **.NET 10** rather than the .NET 8
in the original design doc — .NET 8 wasn't installed in the dev environment and .NET 10 is
otherwise a drop-in match for everything the doc describes (Blazor Web App's unified template
replaces the old `blazorserver` template; EF Core 10 is a straight upgrade).

## 1. Architecture

Clean/Onion architecture, four layers, dependencies point inward:

```
AtmApp.slnx
├── src/
│   ├── AtmApp.Domain/           Entities, enums, repository interfaces. No dependencies.
│   ├── AtmApp.Application/      IAtmService, Result<T>, DTOs. Depends on Domain only.
│   ├── AtmApp.Infrastructure/   EF Core DbContext, repositories, migrations. Depends on Application + Domain.
│   └── AtmApp.Web/              Blazor Server host. Depends on Application + Infrastructure (composition root).
└── tests/
    └── AtmApp.Application.Tests/  Integration tests for AtmService against a real (in-memory) SQLite DB.
```

Business logic (`AtmApp.Application`) never references EF Core types — it only knows
`IAccountRepository`, `ITransactionRepository`, `IUnitOfWork`. Swapping SQLite for MySQL is a
config change in `AtmApp.Infrastructure/DependencyInjection.cs`, not a rewrite.

## 2. Setup

```
dotnet tool install --global dotnet-ef   # if you don't already have it
dotnet restore
dotnet ef database update --project src/AtmApp.Infrastructure --startup-project src/AtmApp.Web
dotnet run --project src/AtmApp.Web
```

Open the URL printed in the console (e.g. `https://localhost:5001`). Two accounts —
**Checking** and **Savings** — are seeded automatically on first run (there's no
account-creation flow in scope); migrations also apply automatically on startup, so the
`dotnet ef database update` step above is optional for local runs and only useful if you want
the schema created ahead of time.

Run the tests with:

```
dotnet test
```

### Docker

```
docker compose up --build
```

Builds the app via the multi-stage `Dockerfile` and runs it on `http://localhost:8080`, with
the SQLite file persisted in a named volume (`atm-data`) so data survives container restarts.
*(Not run against a live Docker daemon in this environment — Docker Desktop wasn't running —
but it follows the standard ASP.NET Core multi-stage publish pattern.)*

## 3. Design decisions & tradeoffs

- **Double-entry ledger, no mutable `Balance` column.** `Account` has no balance field;
  `Transaction.BalanceAfter` is a denormalized running-balance snapshot, and the current
  balance is just the latest transaction's `BalanceAfter` (`ITransactionRepository.GetLatestByAccountId`).
  The transaction table is the source of truth — balance is derived, never stored independently
  of it — so history is "free" and there's no way for a cached balance to drift from the ledger.

- **Idempotency (Stripe-style key).** Every deposit/withdrawal/transfer takes a client-generated
  `Guid` idempotency key. `AtmService` checks `GetByIdempotencyKey` before applying the operation
  and returns the original result if the key was already used, rather than reapplying it. A
  unique DB index on `IdempotencyKey` (`TransactionConfiguration`) is the second line of defense
  against races. In the UI, `AccountCard` generates a fresh key per submit attempt and also
  guards with a synchronous `_isSubmitting` flag, so a double-click can't fire the request twice
  even before the key check would matter.

- **`Result<T>` for expected failures, exceptions for everything else.** Insufficient funds and
  invalid amounts are ordinary outcomes of using an ATM, so they come back as
  `Result<T>.Failure(...)` with an `ErrorType`. `AccountNotFoundException` is thrown and left
  unhandled by the service — it should never happen given a UI that only offers real account
  IDs, so if it does, that's a bug worth surfacing loudly rather than a `Result` the caller might
  silently ignore. `InsufficientFundsException` / `InvalidAmountException` still exist as typed
  domain errors (per the design doc), but `AtmService` constructs them only to reuse their
  message formatting — it never throws them; see the doc comment on `Result<T>`.

- **Transfer atomicity.** `Transfer` runs inside `IUnitOfWork.ExecuteInTransactionAsync`, which
  wraps an EF Core transaction in `Database.CreateExecutionStrategy()` so it retries as a whole
  unit under transient failures rather than leaving a half-applied transfer. The two accounts
  involved are read in a consistent order (sorted by `Guid`) before either is touched, to avoid
  deadlocking against a concurrent transfer running in the opposite direction.

- **State lives on the page, not the card.** `AccountCard` is presentational only — balance,
  history, and the last status message are parameters owned by `Home.razor`. A transfer mutates
  two accounts at once, and Blazor has no built-in way for one sibling component's button click
  to tell another sibling to refresh itself; centralizing the state in the parent and having
  every mutation call `RefreshAll()` sidesteps that entirely.

- **Provider selection via config** (`Infrastructure/DependencyInjection.cs`): `Database:Provider`
  in `appsettings.json` picks SQLite or MySQL at startup. The MySQL branch is real, compiling
  code (via `Pomelo.EntityFrameworkCore.MySql`), but only validated against SQLite for this
  exercise — no MySQL instance was stood up.

- **Currency is pinned to `en-US`, not the server's runtime culture.** Every monetary value —
  the amount input, balances, history, and the messages inside `InsufficientFundsException` /
  `InvalidAmountException` — formats with an explicit `en-US` `CultureInfo`
  (`AtmApp.Web/CurrencyFormat.cs` for the UI; the Domain exceptions inline it directly, since
  Domain can't depend on a Web-layer helper). Without this, `"C"` formatting depends on whatever
  culture the process happens to run under — a minimal-globalization Docker image renders it as
  `¤100.00` instead of `$100.00`, and the exercise wants USD specifically, not "whatever the
  server's locale is."

- **Error messages name the account, not its `Guid`.** `AtmService` now fetches the `Account`
  entity (for its `Name`) wherever it previously only checked existence, so
  `InsufficientFundsException` can say "Checking has insufficient funds..." instead of quoting
  the account ID. The one exception is `AccountNotFoundException`: when the account genuinely
  doesn't exist, there's no entity to read a name from, so it still reports the ID — that's
  inherent to the failure mode, and in practice this exception shouldn't surface through the UI
  at all (see §3 above on `Result<T>` vs. exceptions).

## 4. Security

- **HTTPS** — enabled via `app.UseHttpsRedirection()` (default template behavior), backed by the
  local dev cert.
- **Secrets/connection strings** — read via `IConfiguration` (`appsettings.json` /
  `ConnectionStrings`), never hardcoded in source. The `MySql` connection string in
  `appsettings.json` is an illustrative placeholder (`Password=changeme`), unused unless
  `Database:Provider` is switched to `MySql` — a real deployment would supply it via
  `dotnet user-secrets` locally or environment variables in production, not commit a real one.
- **Auth and at-rest encryption** are deliberate exclusions — see §5 below.

## 5. Explicitly scoped out

- **Auth** — out of spec for this exercise; there's no login and no per-user account ownership.
- **Encryption at rest for SQLite** — real encryption means SQLCipher, which is meaningful setup
  cost for a demo. On AWS this becomes a config-level concern (RDS encryption via KMS), not app
  code.
- **Standing up a real MySQL instance** — the provider swap is designed and compiles, but was
  only run against SQLite here.
- **CQRS** — a natural next thought given the ledger/transaction framing, but real overkill for
  two accounts and three operations. Noted and rejected rather than silently not considered.

## 6. If I had more time

- **Optimistic concurrency** (a `RowVersion` token on `Account`, or serializable isolation on the
  balance read in `Deposit`/`Withdraw`) — right now a `BalanceAfter` race between two concurrent
  deposits to the *same* account isn't fully closed the way `Transfer` is (which does hold both
  balance reads inside one transaction). Total funds are never wrong since `Amount` values are
  never overwritten, but the cached running-balance snapshot on a losing concurrent write could
  end up stale. Single-user scope makes this low-risk in practice.
- **Validate the MySQL path for real** — spin up a MySQL container via docker-compose and run the
  same test suite against it, instead of just compiling against Pomelo.
- **More edge-case tests**: concurrent double-submits racing past the idempotency check,
  very-large-decimal precision, and a couple of Blazor component tests (e.g. with bUnit) for
  `AccountCard`/`Home` rather than only integration-testing the service layer.

## 7. A note on how this was verified

Beyond `dotnet test` (12 integration tests against a real in-memory SQLite DB — deposit,
withdraw, insufficient funds, invalid amount, unknown account, idempotent replay for both
deposit and transfer, transfer atomicity, same-account guard, history ordering), the app was
actually run and driven manually through a real browser (Chrome and Opera) end to end: deposit,
withdraw, an over-withdrawal producing the expected error message, and a transfer — checking
that *both* accounts' balances and histories updated correctly.
