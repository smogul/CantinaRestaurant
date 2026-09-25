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

The API applies database migrations on startup and, under Docker Compose, seeds 10 dishes and 10 drinks into an empty database, so no manual setup is needed. Seeding is controlled by `Seed:Enabled` (`Seed__Enabled` as an environment variable). If `.env` is missing, Compose falls back to the same placeholder values as `.env.example`.

Change `POSTGRES_PASSWORD` in `.env` before the first run. Postgres only reads it when the `postgres-data` volume is created, so after changing it later, run `docker compose down -v` to recreate the volume. This deletes the local data.

To run in Production mode, where `/openapi/v1.json` and `/scalar` are not served, set `ASPNETCORE_ENVIRONMENT=Production` in `.env`.

## Running tests

```bash
dotnet test
```

Docker must be running because the tests start a throwaway PostgreSQL container with Testcontainers. The first run pulls the `postgres:16-alpine` image, so it takes longer.

## Architecture

CantinaApi uses a vertical slice layout: each feature under `Features/` owns its endpoints, request and response types, validation and data access, while `Common/` holds cross-cutting helpers and `Data/` holds the `DbContext`, migrations and seeding. All endpoints stay in the API assembly because the .NET 10 validation source generator only discovers types in the assembly that calls `AddValidation`. The request pipeline runs in this order: correlation id, exception handler, status code pages, Serilog request logging, routing, rate limiter, authentication, authorization, then endpoints. The exception handler wraps everything so every failure becomes an RFC 7807 ProblemDetails response, and status code pages give bare error codes, such as an unreadable request body, the same ProblemDetails shape. Request logging sits inside both so it records failures and timings. Routing runs before rate limiting and auth so they can read endpoint metadata.

## Endpoints

All routes live under `/api/menu-items`. Bodies are JSON with camelCase fields, and `type` is `"Dish"` or `"Drink"`. Every error is an RFC 7807 ProblemDetails body: 400 for validation, 404 for a missing item and 409 for a duplicate name.

| Method | Route | Purpose |
| --- | --- | --- |
| `POST` | `/api/menu-items` | Create a menu item. Returns 201 with a `Location` header. |
| `GET` | `/api/menu-items` | List items ordered by name. Optional `type`, `page` (default 1) and `pageSize` (default 20, max 100). |
| `GET` | `/api/menu-items/search` | Case-insensitive search of names and descriptions. Requires `q` (1 to 100 characters) and takes the same `type`, `page` and `pageSize` as the list. |
| `GET` | `/api/menu-items/{id}` | View an item with its `averageRating` (1 decimal, `null` when unrated) and `ratingCount`. |
| `PUT` | `/api/menu-items/{id}` | Replace every field of an item. |
| `DELETE` | `/api/menu-items/{id}` | Soft delete an item. Returns 204. |
| `POST` | `/api/menu-items/{id}/ratings` | Rate an item from 1 to 5 stars with an optional comment. Returns 201. |
| `GET` | `/api/menu-items/{id}/ratings` | List an item's ratings, newest first, with `page` and `pageSize`. |

List endpoints return `{ "items": [...], "page", "pageSize", "totalCount", "totalPages" }`.

Things to know:

- Prices are in republic credits, with at most 2 decimal places. The API rounds anything finer.
- Names are unique per type, ignoring case, so a dish and a drink can share a name.
- Deleting is a soft delete. The item stops appearing in view, list and search, its name becomes free to reuse, and its reviews are kept in the database.
- Ratings are anonymous for now. Rating ownership arrives with authentication.

Create an item:

```bash
curl -i -X POST http://localhost:8080/api/menu-items \
  -H "Content-Type: application/json" \
  -d '{"name":"Blue Milk Shake","description":"Blue milk blended with Endorian berries.","price":6.50,"imageUrl":"https://placehold.co/600x400?text=Blue+Milk+Shake","type":"Drink"}'
```

List the second page of drinks, 5 per page:

```bash
curl "http://localhost:8080/api/menu-items?type=Drink&page=2&pageSize=5"
```

Search names and descriptions:

```bash
curl "http://localhost:8080/api/menu-items/search?q=bantha&pageSize=10"
```

Rate an item, using an `id` from one of the responses above:

```bash
curl -i -X POST http://localhost:8080/api/menu-items/{id}/ratings \
  -H "Content-Type: application/json" \
  -d '{"stars":5,"comment":"Worth the trip to Mos Eisley."}'
```

## Security

_Documented in a later phase._

## Performance

_Documented in a later phase._

## Trade-offs

_Documented in a later phase._
