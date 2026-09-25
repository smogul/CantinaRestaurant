# CantinaRestaurant

## Overview

Chalmun, a Wookiee who owns the Mos Eisley Cantina on Tatooine, wants to boost sales. The Cantina is very popular, but patrons can only order and consume its delicious offerings on site. CantinaApi is the ASP.NET Core Minimal API that will let patrons order from anywhere.

The solution has two projects:

- `src/CantinaApi`: the API, backed by PostgreSQL through EF Core.
- `tests/CantinaApi.Tests`: xUnit integration tests that run the API against a real PostgreSQL container.

## Prerequisites

- [Docker](https://docs.docker.com/get-docker/) with Docker Compose v2
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), needed only to build or test outside Docker

## Quickstart

```bash
cp .env.example .env
docker compose up --build
```

Then open <http://localhost:8080/scalar> for the interactive API reference, or <http://localhost:8080/health> for the health check.

The API applies database migrations on startup and, under Docker Compose, seeds 10 dishes and 10 drinks into an empty database, so no manual setup is needed. Seeding is controlled by `Seed:Enabled` (`Seed__Enabled` as an environment variable). Compose loads `.env.example` first and then `.env`, so any value you set in `.env` wins and a missing `.env` falls back to the placeholders.

Change `POSTGRES_PASSWORD` in `.env` before the first run. Postgres only reads it when the `postgres-data` volume is created, so after changing it later, run `docker compose down -v` to recreate the volume. This deletes the local data.

To run in Production mode, where `/openapi/v1.json` and `/scalar` are not served, set `ASPNETCORE_ENVIRONMENT=Production` in `.env`.

## Running tests

```bash
dotnet test
```

Docker must be running because the tests start a throwaway PostgreSQL container with Testcontainers. The first run pulls the `postgres:16-alpine` image, so it takes longer.

## Architecture

CantinaApi uses a vertical slice layout: each feature under `Features/` owns its endpoints, request and response types, validation and data access, while `Common/` holds cross-cutting helpers and `Data/` holds the `DbContext`, migrations and seeding. All endpoints stay in the API assembly because the .NET 10 validation source generator only discovers types in the assembly that calls `AddValidation`. The request pipeline runs in this order: forwarded headers (only for configured proxies), correlation id, exception handler, status code pages, Serilog request logging, routing, rate limiter, authentication, authorization, then endpoints. The exception handler wraps everything so every failure becomes an RFC 7807 ProblemDetails response, and status code pages give bare error codes, such as an unreadable request body, the same ProblemDetails shape. Request logging sits inside both so it records failures and timings. Routing runs before rate limiting and auth so they can read endpoint metadata.

## Authentication

Every endpoint needs a bearer token except `POST /api/auth/register`, `POST /api/auth/login`, `GET /health`, and the OpenAPI and Scalar pages in Development. Without a valid token the API returns 401; with a valid token but the wrong role it returns 403. Both come back as ProblemDetails.

Register a customer account. Passwords need 8 to 128 characters with at least one letter and one digit. Every new account is a Customer, and a `role` field in the body is ignored:

```bash
curl -i -X POST http://localhost:8080/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"name":"Rey","email":"rey@jakku.example","password":"Scavenger1"}'
```

Log in to get a token:

```bash
curl -X POST http://localhost:8080/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"rey@jakku.example","password":"Scavenger1"}'
```

The response looks like `{"accessToken":"eyJ...","tokenType":"Bearer","expiresAtUtc":"..."}`. Send the token on every other request:

```bash
TOKEN=$(curl -s -X POST http://localhost:8080/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"rey@jakku.example","password":"Scavenger1"}' | sed -E 's/.*"accessToken":"([^"]+)".*/\1/')

curl http://localhost:8080/api/menu-items -H "Authorization: Bearer $TOKEN"
```

In Scalar, choose **Authentication**, pick the Bearer scheme and paste the `accessToken`.

A wrong password and an unknown email both return the same 401 `Invalid credentials` response, so the API does not reveal which emails are registered.

### Seeded accounts

Docker Compose seeds one Admin and one Customer on first start, when the Users table is empty. Their names, emails and passwords come from the `Seed__*` entries in `.env`; see [.env.example](.env.example) for the placeholders. With the example values you can log in as `admin@cantina.example` / `ChangeMe-Admin-1` or `customer@cantina.example` / `ChangeMe-Customer-1`. Change the passwords in `.env` before the first run, because later changes do not update accounts that already exist. The seeded customer has already rated a few items.

### Roles

| Role | Can do |
| --- | --- |
| Admin | Create, update and delete menu items. List, view and search the menu, and read ratings. |
| Customer | List, view and search the menu, read ratings, and rate items. Rating the same item again updates the earlier rating. |

Admins cannot rate. Staff rating their own menu would skew the scores customers rely on, so `POST /api/menu-items/{id}/ratings` returns 403 for admins. The only way to become an Admin is through seeding or a direct database change; there is no admin sign-up.

## Endpoints

Menu routes live under `/api/menu-items` and account routes under `/api/auth`. All of them except register and login need a bearer token (see [Authentication](#authentication)). Bodies are JSON with camelCase fields, and `type` is `"Dish"` or `"Drink"`. Every error is an RFC 7807 ProblemDetails body: 400 for validation, 401 for a missing or invalid token, 403 for the wrong role, 404 for a missing item and 409 for a duplicate name or email.

| Method | Route | Purpose |
| --- | --- | --- |
| `POST` | `/api/auth/register` | Create a customer account. Anonymous. |
| `POST` | `/api/auth/login` | Exchange an email and password for a bearer token. Anonymous. |
| `POST` | `/api/menu-items` | Create a menu item. Admin only. Returns 201 with a `Location` header. |
| `GET` | `/api/menu-items` | List items ordered by name. Optional `type`, `page` (default 1) and `pageSize` (default 20, max 100). |
| `GET` | `/api/menu-items/search` | Case-insensitive search of names and descriptions. Requires `q` (1 to 100 characters) and takes the same `type`, `page` and `pageSize` as the list. |
| `GET` | `/api/menu-items/{id}` | View an item with its `averageRating` (1 decimal, `null` when unrated) and `ratingCount`. |
| `PUT` | `/api/menu-items/{id}` | Replace every field of an item. Admin only. |
| `DELETE` | `/api/menu-items/{id}` | Soft delete an item. Admin only. Returns 204. |
| `POST` | `/api/menu-items/{id}/ratings` | Rate an item from 1 to 5 stars with an optional comment. Customer only. Returns 201 the first time and 200 when it updates your earlier rating. |
| `GET` | `/api/menu-items/{id}/ratings` | List an item's ratings, newest first, with `page` and `pageSize`. Shows each reviewer's name but never their email. |

List endpoints return `{ "items": [...], "page", "pageSize", "totalCount", "totalPages" }`.

Things to know:

- Prices are in republic credits, with at most 2 decimal places. The API rounds anything finer.
- Names are unique per type, ignoring case, so a dish and a drink can share a name.
- Deleting is a soft delete. The item stops appearing in view, list and search, its name becomes free to reuse, and its reviews are kept in the database.
- A rating belongs to the customer in the token. Any `userId` in the request body is ignored, and each customer has one rating per item.

The examples below assume `$ADMIN_TOKEN` and `$TOKEN` hold tokens from logging in as the seeded admin and customer.

Create an item (admin):

```bash
curl -i -X POST http://localhost:8080/api/menu-items \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name":"Blue Milk Shake","description":"Blue milk blended with Endorian berries.","price":6.50,"imageUrl":"https://placehold.co/600x400?text=Blue+Milk+Shake","type":"Drink"}'
```

List the second page of drinks, 5 per page:

```bash
curl "http://localhost:8080/api/menu-items?type=Drink&page=2&pageSize=5" -H "Authorization: Bearer $TOKEN"
```

Search names and descriptions:

```bash
curl "http://localhost:8080/api/menu-items/search?q=bantha&pageSize=10" -H "Authorization: Bearer $TOKEN"
```

Rate an item (customer), using an `id` from one of the responses above:

```bash
curl -i -X POST http://localhost:8080/api/menu-items/{id}/ratings \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"stars":5,"comment":"Worth the trip to Mos Eisley."}'
```

## Security

Login and registration are protected by four layers. Each one covers a gap the others leave.

| Layer | What it does | Default | Configuration |
| --- | --- | --- | --- |
| Account lockout | 5 wrong passwords in a row lock the account for 15 minutes. While it is locked, every login fails, even with the right password, and failed attempts neither count nor extend the lockout. A successful login resets the counter. | 5 attempts, 15 minutes | `Auth:Lockout:MaxFailedAttempts`, `Auth:Lockout:DurationMinutes` |
| Per-IP rate limits | Each client IP gets its own fixed window for login and a separate one for registration, so spending one budget does not touch the other. Extra requests get 429 with a `Retry-After` header. | Login 10 per 60 s, register 5 per 60 s | `RateLimiting:Login:PermitLimit`, `RateLimiting:Login:WindowSeconds`, `RateLimiting:Register:PermitLimit`, `RateLimiting:Register:WindowSeconds` |
| Identical responses and timing | An unknown email, a wrong password and a locked account all return the same 401 `Invalid credentials` body. Every failure also runs a bcrypt check (against a dummy hash when there is no real one) and the same database statements, so neither the response nor its timing reveals which case happened. | Always on | `Auth:BcryptWorkFactor` (12) sets the hashing cost |
| Logging | Warnings for a login on an unknown email, on a locked account, when an account gets locked, and for every rate limit rejection, each with the client IP. Passwords are never logged. | Always on | Serilog levels in `appsettings.json` |

The failed-attempt counter is updated with a single SQL `UPDATE`, so a burst of parallel guesses cannot slip past the limit by racing each other.

### Client IPs and proxies

Rate limits and logs use the TCP connection's address. `X-Forwarded-For` is ignored unless the proxy's address is listed in `ForwardedHeaders:KnownProxies` (for example `ForwardedHeaders__KnownProxies__0=10.0.0.5`). Any client can send that header, so trusting it by default would let an attacker pick a new fake IP for every request and never hit a limit. When the API runs behind a load balancer or reverse proxy, list its address there so the real client IP is used.

### Known trade-offs

- **Lockout can be used against a real customer.** Anyone who knows an email can keep it locked by sending 5 wrong passwords every 15 minutes. A production system would add progressive delays, a CAPTCHA after a few failures, or email the owner instead of locking outright.
- **Rate limits are in memory, per instance.** Each API instance counts on its own and the counts reset on restart. A deployment with several instances needs a shared limiter, for example one backed by Redis.
- **IP limits can be dodged by a botnet.** Spreading guesses across many IPs gets past per-IP limits. Account lockout still caps guesses against any single account, whatever the source.

## Performance

The goal was more throughput from the same small server without changing any request or response. Every change below started from evidence rather than guesswork.

### What changed and why

- **Query plan investigation first.** Before touching anything, the SQL that EF Core generates for each read endpoint was run through `EXPLAIN (ANALYZE, BUFFERS)` against the load-test data. The plans and a note on each are in [Docs/Performance/QueryPlansBefore.md](Docs/Performance/QueryPlansBefore.md), and the same plans after the changes are in [Docs/Performance/QueryPlansAfter.md](Docs/Performance/QueryPlansAfter.md). Every index added had to be justified by a plan. Two candidates were rejected because the plans showed they would not help: a separate `(Type, Name)` index and a descending `(MenuItemId, CreatedAtUtc)` rating index.
- **Trigram and ordering indexes.** Search used to run `ILIKE '%term%'` against every row. The `pg_trgm` extension and GIN trigram indexes on `Name` and `Description` let Postgres find matching rows from the index instead. Lists and searches always sort by name, so a partial index on `(Name, Id)` for live items returns a page already in order without sorting the table. All three indexes skip soft-deleted rows. In the plans, list page 1 went from 1.7 ms to 0.08 ms and a search page from 5.2 ms to 0.16 ms.
- **Stored rating stats.** Viewing an item used to average and count its ratings on every request. `MenuItems` now stores `AverageRating` and `RatingCount`, and the migration fills them from existing ratings. Every rating write recalculates them from the `Ratings` table in the same transaction, after locking the item row, so concurrent ratings on one item take turns and none can be lost. Recalculating from source rather than adding to a running total means the stored values can never drift. The average is stored already rounded to one decimal, the precision the API returns.
- **HybridCache with tags and stampede protection.** List, search, view and ratings responses are cached in memory, for 60 seconds (`Caching:MenuSeconds`) or 30 seconds for search (`Caching:SearchSeconds`). Writes invalidate by tag: any menu change clears list, search and view entries (`menu-items`), and a rating clears that item's view and ratings pages (`menu-item:{id}`, `ratings:{id}`). When many requests miss the same key at once, HybridCache runs one database query and shares the result, so an expiring popular page cannot trigger a stampede. The cached records are sealed and marked `[ImmutableObject(true)]`, so every hit returns the stored instance instead of deserialising a copy.
- **Asynchronous logging.** Serilog now hands each log event to a background writer (`Serilog.Sinks.Async`) instead of making the request wait on the console. The JSON format and correlation ids are unchanged, and the buffer is flushed when the app shuts down.

### Cache safety rules

- **Shared data only.** Cached responses are identical for every signed-in user. Anything that depends on who is asking must never be cached this way. Authentication and authorisation run before the handler, so a cached entry is never served without a valid token.
- **Invalidate after commit.** Tags are cleared only after the database write has committed, so a concurrent read cannot cache the old data again in between.
- **Not-found results are not cached.** A 404 for an unknown id always goes to the database, so requests for random ids cannot fill memory with empty entries.
- **Bounded key growth.** Search keys include the trimmed, lowercased query, so each distinct search adds an entry. The query is capped at 100 characters and entries expire after 30 seconds. Per-user rate limiting, planned for Phase 5, will also bound how many distinct searches a single user can create.

### Limitations

- Trigram indexes only help search terms of 3 or more characters. Shorter terms still scan the live rows.
- Deep pages, such as page 200, still scan and sort: at Postgres's default `random_page_cost` the planner prefers that over walking the index, even though the forced index path was faster on this data. Lowering `random_page_cost` for SSD storage, or keyset pagination, would fix it.
- The cache lives in each API instance's memory, so separate instances each hold their own copy.

### Why no response compression

Compressing responses was deliberately left out. The API returns authenticated, dynamic JSON over HTTPS, and compressing secrets alongside attacker-influenced content, such as a search term reflected in the response, exposes it to the CRIME and BREACH family of attacks, which recover data from compressed sizes. Compression belongs at the reverse proxy, where it can be applied selectively to safe content.

### Beyond this hardware

When one server is no longer enough, these are the next steps, roughly in order:

- **Horizontal scaling.** Run several API instances behind a load balancer. JWT authentication is stateless, so any instance can serve any request.
- **Redis as the HybridCache second level.** Registering a Redis `IDistributedCache` gives every instance one shared cache. It is a registration change only; the handlers stay the same.
- **A distributed rate limiter.** The login and register limits count per instance today, so several instances need a shared store for them to hold.
- **PgBouncer and read replicas.** Connection pooling keeps many instances from exhausting Postgres connections, and read replicas take the read-heavy menu traffic.
- **Images on a CDN or blob storage.** Menu images should be served from a CDN, not by the API.
- **Keyset pagination.** Paging by "after this name and id" instead of an offset makes deep pages as cheap as the first.
- **A dedicated search engine.** If search grows beyond what trigram indexes handle well, move it to a search engine.
- **Separately scaled login instances.** BCrypt is deliberately CPU-heavy, so login traffic can run on its own instances without slowing the menu.
- **OpenTelemetry metrics.** Request rates, latencies and cache hit ratios show when to scale, rather than guessing.

### Load testing

The load test measures five read endpoints under steady pressure, so a "before" baseline can be compared with later changes. It needs [hey](https://github.com/rakyll/hey) and [jq](https://jqlang.org) on your machine.

- [docker-compose.loadtest.yml](docker-compose.loadtest.yml) layers on the normal compose file and limits the API to 1 CPU and 512 MB of memory, like the Cantina's old outer-rim server.
- [Scripts/LoadTestSeed.sql](Scripts/LoadTestSeed.sql) adds 5,000 menu items, 200 customers and 10,000 ratings. It is safe to run more than once, because it does nothing if the data is already there.
- [Scripts/LoadTest.sh](Scripts/LoadTest.sh) logs in as the seeded customer and runs each endpoint for 30 seconds with 50 concurrent workers, after a 5-second warm-up: list page 1, list page 200, search for `milk`, view one item, and that item's ratings. It prints requests per second, p95 latency and any non-200 responses, and saves hey's full output to `LoadTestResults/<Label>.txt`, which git ignores.

```bash
docker compose -f docker-compose.yml -f docker-compose.loadtest.yml up -d --build
docker compose exec -T postgres sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB"' < Scripts/LoadTestSeed.sql
Scripts/LoadTest.sh Baseline
```

The script uses `BASE` (default `http://localhost:8080`), `CUSTOMER_EMAIL` and `CUSTOMER_PASSWORD`, which default to the `.env.example` values. If you changed the seeded customer in `.env`, set these to match. A run takes about three minutes. If it reports any non-200 responses, fix those first, because the numbers are not meaningful otherwise.

| Endpoint | Baseline req/s | Baseline p95 ms | After req/s | After p95 ms |
| --- | --- | --- | --- | --- |
| List page 1 | | | | |
| List page 200 | | | | |
| Search milk | | | | |
| View item | | | | |
| Item ratings | | | | |

## Trade-offs

- **No refresh tokens or logout.** Access tokens are stateless and last 60 minutes (`Jwt:LifetimeMinutes`). A client logs in again when its token expires, and a token cannot be revoked before then. That keeps the API free of token storage, at the cost of a stolen token staying usable until it expires.
- **No email verification.** Anyone can register with any address they type. Registration only creates customers, so the damage is limited to fake reviews, and each account can rate an item only once.
- **The API issues its own tokens with a shared secret.** Tokens are signed with HMAC-SHA256 using `Jwt:SigningKey`. Microsoft recommends a standard identity provider (OpenID Connect) with asymmetric keys for production; a self-issued token keeps this project self-contained.
