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

CantinaApi uses a vertical slice layout: each feature under `Features/` owns its endpoints, request and response types, validation and data access, while `Common/` holds cross-cutting helpers and `Data/` holds the `DbContext`, migrations and seeding. All endpoints stay in the API assembly because the .NET 10 validation source generator only discovers types in the assembly that calls `AddValidation`. The request pipeline runs in this order: correlation id, exception handler, status code pages, Serilog request logging, routing, rate limiter, authentication, authorization, then endpoints. The exception handler wraps everything so every failure becomes an RFC 7807 ProblemDetails response, and status code pages give bare error codes, such as an unreadable request body, the same ProblemDetails shape. Request logging sits inside both so it records failures and timings. Routing runs before rate limiting and auth so they can read endpoint metadata.

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

_Documented in a later phase._

## Performance

_Documented in a later phase._

## Trade-offs

- **No refresh tokens or logout.** Access tokens are stateless and last 60 minutes (`Jwt:LifetimeMinutes`). A client logs in again when its token expires, and a token cannot be revoked before then. That keeps the API free of token storage, at the cost of a stolen token staying usable until it expires.
- **No email verification.** Anyone can register with any address they type. Registration only creates customers, so the damage is limited to fake reviews, and each account can rate an item only once.
- **The API issues its own tokens with a shared secret.** Tokens are signed with HMAC-SHA256 using `Jwt:SigningKey`. Microsoft recommends a standard identity provider (OpenID Connect) with asymmetric keys for production; a self-issued token keeps this project self-contained.
