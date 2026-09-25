# CantinaRestaurant

CantinaApi is an ASP.NET Core 10 Minimal API for the Mos Eisley Cantina menu: menu items, customer ratings, and JWT authentication.
It runs on PostgreSQL through EF Core, and the API and database start together with Docker Compose.

## Run it

```bash
git clone https://github.com/smogul/CantinaRestaurant.git
cd CantinaRestaurant
docker compose up --build
```

Then open <http://localhost:8080/scalar> for the API reference and <http://localhost:8080/health> for the health check. No other setup is needed. On first start the API applies its migrations and seeds 20 menu items, one admin, one customer and a few sample ratings. Settings come from `.env.example`; copy it to `.env` to override any of them.

## Log in

| Role | Email | Password |
| --- | --- | --- |
| Admin | `admin@cantina.example` | `ChangeMe-Admin-1` |
| Customer | `customer@cantina.example` | `ChangeMe-Customer-1` |

```bash
TOKEN=$(curl -s -X POST http://localhost:8080/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"customer@cantina.example","password":"ChangeMe-Customer-1"}' \
  | sed -E 's/.*"accessToken":"([^"]+)".*/\1/')

curl http://localhost:8080/api/menu-items -H "Authorization: Bearer $TOKEN"
```

In Scalar, open **Authentication**, choose Bearer and paste the `accessToken`.

## Endpoints

| Method | Route | Who can call it |
| --- | --- | --- |
| `POST` | `/api/auth/register` | Anyone. Always creates a Customer. |
| `POST` | `/api/auth/login` | Anyone |
| `GET` | `/api/menu-items` | Any signed-in user. `type`, `page`, `pageSize` |
| `GET` | `/api/menu-items/search?q=` | Any signed-in user. Same filters as the list |
| `GET` | `/api/menu-items/{id}` | Any signed-in user. Includes `averageRating` and `ratingCount` |
| `POST` | `/api/menu-items` | Admin |
| `PUT` | `/api/menu-items/{id}` | Admin |
| `DELETE` | `/api/menu-items/{id}` | Admin. Soft delete; ratings are kept |
| `GET` | `/api/menu-items/{id}/ratings` | Any signed-in user. Newest first, shows reviewer names but no emails |
| `POST` | `/api/menu-items/{id}/ratings` | Customer. Rating the same item again updates the earlier rating |
| `GET` | `/health` | Anyone |

All errors are RFC 7807 ProblemDetails with a `correlationId`: 400 validation, 401 missing or bad token, 403 wrong role, 404 not found, 409 duplicate, 429 rate limited.

## Run the tests

```bash
dotnet test
```

This needs Docker running and the .NET 10 SDK. The tests start a throwaway PostgreSQL container with Testcontainers; the first run pulls the `postgres:16-alpine` image.

## How the tasks were met

**Task 1: menu and ratings.** Vertical slices under `Features/`, one file per endpoint, all grouped under `/api/menu-items`. Menu item names are unique per type ignoring case, enforced by a partial unique index on `lower("Name")`. Delete is a soft delete behind a global query filter, so deleted items return 404 but their ratings stay. Search is a case-insensitive `ILIKE` on name and description, with `%`, `_` and `\` escaped so they match literally. Lists are paginated and validated, and responses are DTOs, never EF entities.

**Task 2: authentication and authorisation.** Accounts register and log in through `/api/auth` and receive a 60-minute HMAC-SHA256 JWT, created with `JsonWebTokenHandler`. A fallback policy protects every endpoint by default. `AdminOnly` covers menu writes and `CustomerOnly` covers rating, so staff cannot rate their own menu. Passwords use BCrypt, work factor 12. Brute force is handled in four layers:
- **Account lockout:** 5 wrong passwords lock the account for 15 minutes. The counter is updated with one atomic SQL statement, so parallel guesses cannot race past it.
- **Per-IP rate limits:** 10 logins and 5 registrations per minute from each IP, as separate budgets. Rejections return 429 with `Retry-After`. `X-Forwarded-For` is ignored unless `ForwardedHeaders:KnownProxies` lists the proxy.
- **Identical responses and timing:** an unknown email, a wrong password and a locked account all return the same body, and each runs the same bcrypt and database work.
- **Logging:** warnings for unknown emails, locked accounts, new lockouts and rate limit rejections. Passwords are never logged.

**Task 3: performance on the same hardware.** Query plans for every read endpoint were captured before and after the changes ([Docs/Performance](Docs/Performance)), and indexes were only added where a plan justified them. The changes:
- **Indexes:** trigram GIN indexes on `Name` and `Description` for search, and a partial `(Name, Id)` index so lists and searches read pages in order instead of sorting.
- **Stored rating stats:** `AverageRating` and `RatingCount` on each item, recalculated in the same transaction as every rating write, under a row lock so concurrent ratings cannot be lost.
- **HybridCache:** list, search, view and ratings responses are cached. Entries are invalidated by tag after each write commits, and not-found results are never cached.
- **Async logging:** Serilog writes to the console through `Serilog.Sinks.Async`.

The load-test harness simulates a 1 CPU / 512 MB server. `docker-compose.loadtest.yml` sets those limits, `Scripts/LoadTestSeed.sql` adds 5,000 items, 200 users and 10,000 ratings, and `Scripts/LoadTest.sh <Label>` measures five endpoints with `hey`.

**Task 4:** not implemented. See Trade-offs and next steps.

## Performance

1 CPU / 512 MB, 5,020 items, 50 concurrent, Docker Desktop on macOS; figures are relative.

| Endpoint | Baseline req/s | Baseline p95 | After req/s | After p95 |
| --- | --- | --- | --- | --- |
| List page 1 | 862 | 100 ms | 8,735 | 14 ms |
| List page 200 | 889 | 101 ms | 8,490 | 15 ms |
| Search milk | 595 | 146 ms | 8,446 | 16 ms |
| View item | 2,747 | 53 ms | 10,905 | 11 ms |
| Item ratings | 1,526 | 69 ms | 9,912 | 13 ms |

After is a 30-second run per endpoint, with every response a 200 (`LoadTestResults/After.txt`). Most of the gain comes from caching, because the load test repeats the same requests; the query plans show what the indexes and stored stats do on a cache miss. To reproduce:

```bash
docker compose -f docker-compose.yml -f docker-compose.loadtest.yml up -d --build
docker compose exec -T postgres sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB"' < Scripts/LoadTestSeed.sql
Scripts/LoadTest.sh After
```

## Beyond this hardware

- **Horizontal scaling:** run several API instances behind a load balancer. JWTs are stateless, so any instance can serve any request.
- **Redis as the HybridCache second level:** gives all instances one shared cache. It is a registration change only.
- **Distributed rate limiting:** today's limits are counted per instance and need a shared store once there are several.
- **PgBouncer and read replicas:** connection pooling for many instances, and replicas for the read-heavy menu traffic.
- **A CDN for images:** serve menu images from a CDN, not from the API.
- **Keyset pagination:** makes deep pages as cheap as the first page.

## Trade-offs and next steps

- **Deep pages:** a cache miss on a deep page still scans and sorts, because Postgres's default `random_page_cost` makes it prefer that over the index. Keyset pagination or tuning that setting would fix it.
- **No response compression:** it was left out on purpose because of the CRIME and BREACH risks with authenticated JSON over HTTPS; compression belongs at the reverse proxy.
- **Local cache only:** the cache and rate limits live in each instance's memory; there is no Redis second level yet.
- **Task 4 not implemented:** per-user rate limiting and a reviews dashboard.
- **No OAuth2 or SSO:** the API issues its own tokens instead of using an identity provider.
- **No refresh tokens or logout:** a token stays valid for its full 60 minutes.
- **No email verification:** accounts are active as soon as they register.
- **Placeholder JWT key accepted everywhere:** the key from `.env.example` is not rejected outside Development, so set a real `Jwt__SigningKey` for any real deployment.
- **Lockout can be abused:** anyone who knows an email can keep that account locked. Progressive delays, a CAPTCHA or notifying the owner would mitigate this.
