# ATM Application — Design Document

**Stack**: Blazor Server (.NET 10), EF Core, SQLite (local)
**Time budget**: 2–4 hours — prioritize per the notes below; document what's scoped out rather than building everything.

---

## 1. Solution Structure (Clean/Onion Architecture)

```
AtmApp.sln
├── src/
│   ├── AtmApp.Domain/           # Entities, enums, repository interfaces. No dependencies on anything else.
│   ├── AtmApp.Application/      # Use-case services, DTOs, idempotency logic. Depends on Domain only.
│   ├── AtmApp.Infrastructure/   # EF Core DbContext, repository implementations, migrations. Depends on Application + Domain.
│   └── AtmApp.Web/              # Blazor Server host (Program.cs, Components, wwwroot). Depends on Application + Infrastructure (composition root only).
└── tests/
    └── AtmApp.Application.Tests/  # (optional — see §7)
```

Key rule: **business logic never references EF Core types directly.** The Domain and Application layers only know about interfaces (`IAccountRepository`, `ITransactionRepository`). This is what makes the SQLite→MySQL swap a one-line change instead of a rewrite, and it's the thing to point to when asked about "technical choices & tradeoffs."

---

## 2. Domain Layer

**Entities:**

```
Account
- Id (Guid)
- Name (string)         // "Checking", "Savings" — just enough to distinguish the two
- CreatedAt (DateTime)
// NOTE: no mutable Balance field — see ledger pattern below

Transaction
- Id (Guid)
- AccountId (Guid)
- Type (TransactionType: Deposit, Withdrawal, TransferOut, TransferIn)
- Amount (decimal)
- BalanceAfter (decimal)      // snapshot for fast reads / history display
- RelatedTransactionId (Guid?)  // links the two legs of a transfer
- IdempotencyKey (Guid)       // see §4
- Timestamp (DateTime)
```

**Why no `Balance` column on `Account`:** treat the transaction table as the source of truth (double-entry ledger pattern) and derive balance from `SUM(Amount)` per account, or maintain `BalanceAfter` as a denormalized cache on each transaction for cheap reads. This is a real pattern from financial systems, gives you transaction history "for free," and is a strong talking point in the follow-up interview — worth calling out explicitly in the README.

**Repository interfaces** (implemented in Infrastructure):
- `IAccountRepository` — GetById, GetAll
- `ITransactionRepository` — Add, GetByAccountId, GetByIdempotencyKey

**Domain errors**: define specific exception types (`InsufficientFundsException`, `AccountNotFoundException`, `InvalidAmountException`) rather than generic exceptions — this maps directly to the "error handling" eval criterion.

---

## 3. Application Layer

**`IAtmService`** (or split into `IDepositService`/`IWithdrawalService`/`ITransferService` if you want finer separation of concerns — either is defensible, just be ready to explain the choice):

```
Task<Result<Transaction>> Deposit(Guid accountId, decimal amount, Guid idempotencyKey)
Task<Result<Transaction>> Withdraw(Guid accountId, decimal amount, Guid idempotencyKey)
Task<Result<TransferResult>> Transfer(Guid fromId, Guid toId, decimal amount, Guid idempotencyKey)
Task<IReadOnlyList<Transaction>> GetHistory(Guid accountId)
Task<decimal> GetBalance(Guid accountId)
```

**Result pattern**: return a `Result<T>` (success/failure + error message/type) from service methods instead of throwing for expected business failures (insufficient funds, invalid amount). Reserve exceptions for truly exceptional cases (account not found, DB failure). This is a deliberate, explainable choice for the "error handling" criterion.

---

## 4. Idempotency (your strongest differentiator — implement this for real)

**Pattern**: client generates a `Guid` per transaction attempt (a "submit" click), sends it with the request. Server checks whether a transaction with that `IdempotencyKey` already exists before applying the operation:

```
if (await _transactionRepo.GetByIdempotencyKey(key) is { } existing)
    return Result<Transaction>.Success(existing);  // already processed — return the same result, don't reapply

// otherwise, proceed with the operation
```

In Blazor Server, generate the key when the user opens the transaction form (or on button click, stored in component state) — this protects against double-clicks and accidental resubmission, which is the realistic failure mode for a single-page ATM UI.

Add a unique constraint on `IdempotencyKey` in the DB as a second line of defense against race conditions.

---

## 5. Concurrency / Transfer Atomicity

A transfer touches two accounts and must be all-or-nothing:

- Wrap the transfer in an EF Core `DbContext` transaction (`BeginTransactionAsync`)
- Always operate on accounts in a **consistent order** (e.g., sort by `AccountId`) to avoid deadlocks if you ever have concurrent transfers in both directions
- Consider a `RowVersion`/concurrency token column on `Account` if you want to demonstrate optimistic concurrency handling — nice-to-have, not essential given single-user scope, but cheap to mention as a known extension point in the README if you skip it

---

## 6. Infrastructure Layer

- `AtmDbContext : DbContext` with `DbSet<Account>`, `DbSet<Transaction>`
- Repository implementations of the Domain interfaces
- Provider selection via config, not hardcoded:

```csharp
// Program.cs
var provider = builder.Configuration["Database:Provider"]; // "Sqlite" or "MySql"
builder.Services.AddDbContext<AtmDbContext>(options =>
{
    if (provider == "MySql")
        options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
    else
        options.UseSqlite(connectionString);
});
```

This is the whole "database agnostic" story — don't build more than this. Validate against SQLite for the exercise; document the MySQL swap as a one-line config change in the README rather than standing up a real MySQL instance.

- Seed two accounts on startup (e.g., in `Program.cs` or a small seeder) since there's no account-creation flow in scope.

---

## 7. Blazor Server UI

- One page (`/`) with two account cards showing balance + a transaction form (deposit/withdraw/transfer) + history list
- Keep components thin — they call `IAtmService`, they don't contain business logic
- Client-side generates the idempotency key per submit attempt (see §4)
- Basic validation (amount > 0, sufficient funds) surfaced via the `Result<T>` pattern from the service layer — don't duplicate business rules in the UI

---

## 8. Security — scope deliberately, document what you skip

| Item | Decision |
|---|---|
| HTTPS (transport encryption) | **Implement** — free with `app.UseHttpsRedirection()` / dev cert |
| Secrets/connection strings | **Implement** — `IConfiguration` + user-secrets locally, env vars in deployment. Never hardcoded. |
| Auth | **Explicitly out of scope** per the spec — state this plainly in the README as a deliberate exclusion, not an oversight |
| Encryption at rest (SQLite) | **Scope out** — real encryption means SQLCipher, meaningful setup cost for a demo. Document as a known gap; note that on AWS this becomes a config-level concern (RDS encryption via KMS), not app code |

---

## 9. Deployment Story (design for it, don't necessarily build all of it)

- **Dockerfile** for the Blazor Server app — do build this, it's cheap and directly supports "single machine now, AWS later"
- **docker-compose.yml** for local run (app + optionally a local MySQL container if you want to actually prove the swap — optional, time permitting)
- **AWS path** (document in README, don't need to deploy): same image → ECS/Fargate, RDS MySQL, config via environment variables (12-factor style)
- **Known tradeoff to flag proactively**: Blazor Server holds a stateful SignalR circuit per user, which needs sticky sessions on the load balancer (or a backplane like Redis) to scale past one instance on AWS. Mention this explicitly — it shows you're thinking about deployment topology, not just code, and it's an honest tradeoff of the Blazor choice.

---

## 10. What to explicitly reject (and say so in the README)

- **CQRS** — natural next thought given the ledger/transaction framing, but real overkill for this scope. Mentioning you considered and passed on it is a good, cheap way to show judgment.
- **Full auth/identity system** — out of spec.
- **SQLCipher / at-rest encryption** — see §8.
- **Standing up real MySQL for this exercise** — design for it, don't necessarily build it.

---

## 11. README Outline (your actual deliverable alongside the code)

1. **Overview** — what the app does, one paragraph
2. **Architecture** — layer diagram/description, why Clean/Onion
3. **Setup instructions** — `dotnet restore`, `dotnet ef database update`, `dotnet run`, how to open in browser
4. **Design decisions & tradeoffs** — idempotency pattern, ledger vs. mutable balance, Result pattern for errors, DB-agnostic provider config
5. **Explicitly scoped out** — auth, at-rest encryption, real MySQL deployment, CQRS — one line each on why
6. **If I had more time** — 2–3 bullets (e.g., optimistic concurrency tokens, real MySQL validation, integration tests)

---

## 12. Suggested Time Allocation (2–4 hrs)

1. Domain + Application layer with idempotency and Result pattern — **highest priority, most scored**
2. Infrastructure (EF Core + SQLite) + seed data
3. Blazor UI — thin, functional, not polished
4. Dockerfile
5. README — don't shortchange this; it's where your architectural thinking becomes visible even where code doesn't cover it

---

## Patterns Reference (for the follow-up discussion)

- Repository pattern
- Idempotency-key pattern (Stripe-style)
- Double-entry ledger pattern
- Clean/Onion architecture
- Result pattern / typed domain exceptions
- Options pattern (`IOptions<T>`) for environment-driven config
- Optimistic concurrency (mentioned, optional to implement)
- CQRS (considered, deliberately rejected — know why)
