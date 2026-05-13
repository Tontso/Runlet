# Runlet Project Notes

Runlet is a backend-focused distributed workflow execution engine built with .NET.
It is not primarily a CI/CD product; CI/CD is one possible workflow use case.

## Architecture

- `Runlet.Api`: ASP.NET Core API for creating runs and exposing run state.
- `Runlet.Worker`: Worker Service that polls, claims, executes, and reports runs.
- `Runlet.Shared`: Shared contracts, workflow models, and lifecycle enums.

## MVP Goal

Build the smallest useful execution loop:

1. API accepts a JSON workflow.
2. API stores a workflow run as `Pending`.
3. Worker polls pending runs.
4. Worker safely claims one run.
5. Worker executes steps sequentially.
6. Worker stores logs and statuses.
7. Run ends as `Succeeded` or `Failed`.

The first workflow format can stay JSON-based:

```json
{
  "image": "alpine:latest",
  "steps": ["echo hello", "sleep 2", "echo done"]
}
```

## Design Direction

- Avoid Hangfire for core workflow execution; implementing scheduling, claiming,
  retries, cancellation, and lifecycle management is the learning goal.
- Prefer boring, explicit domain models before introducing abstractions.
- Keep the MVP single-database and polling-based before adding Redis, SignalR,
  distributed agents, YAML, or Kubernetes.
- Use PostgreSQL row locking or equivalent atomic updates for safe worker claims.
- Docker execution belongs behind an executor abstraction once the basic lifecycle works.

## Near-Term Implementation Plan

1. Define shared workflow contracts and statuses.
2. Add persistence with EF Core and PostgreSQL.
3. Implement `POST /runs`.
4. Implement worker polling and safe claiming.
5. Execute steps locally for the first vertical slice.
6. Add Docker-based step execution.
7. Add logs, status endpoints, and basic failure handling.
