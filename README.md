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

The API applies database migrations and seeds data on startup, so no manual database setup is needed. If `.env` is missing, Compose falls back to the same placeholder values as `.env.example`.

Change `POSTGRES_PASSWORD` in `.env` before the first run. Postgres only reads it when the `postgres-data` volume is created, so after changing it later, run `docker compose down -v` to recreate the volume. This deletes the local data.

To run in Production mode, where `/openapi/v1.json` and `/scalar` are not served, set `ASPNETCORE_ENVIRONMENT=Production` in `.env`.

## Running tests

```bash
dotnet test
```

Docker must be running because the tests start a throwaway PostgreSQL container with Testcontainers. The first run pulls the `postgres:16-alpine` image, so it takes longer.

## Architecture

CantinaApi uses a vertical slice layout: each feature under `Features/` owns its endpoints, request and response types, validation and data access, while `Common/` holds cross-cutting helpers and `Data/` holds the `DbContext`, migrations and seeding. All endpoints stay in the API assembly because the .NET 10 validation source generator only discovers types in the assembly that calls `AddValidation`. The request pipeline runs in this order: correlation id, exception handler, Serilog request logging, routing, rate limiter, authentication, authorization, then endpoints. The exception handler wraps everything so every failure becomes an RFC 7807 ProblemDetails response. Request logging sits inside it so it records failures and timings. Routing runs before rate limiting and auth so they can read endpoint metadata.

## Endpoints

_Documented as features are added._

## Security

_Documented in a later phase._

## Performance

_Documented in a later phase._

## Trade-offs

_Documented in a later phase._
