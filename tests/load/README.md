# FinCore isolated k6 tests

## Requirements and isolation

- Docker Engine/Desktop with Compose v2
- .NET 10 SDK and access to the repository's pinned `dotnet-ef` tool
- PowerShell 7 or Windows PowerShell 5.1

The k6 service uses the explicitly pinned `grafana/k6:1.8.2` image. The API is built from the existing repository `Dockerfile`.

The load environment uses its own Compose project (`fincore-load`), PostgreSQL database (`fincore_load`), fixed named volume (`fincore_load_postgres_data`), and host ports `18080` (API) and `55432` (PostgreSQL). It does not use the normal Compose project or `fincore_postgres_data` volume. k6 talks to the API over the isolated Compose network at `http://api:8080`.

Copy `.env.load.example` to `.env.load`, then replace the placeholders with unique synthetic load-test-only values. Do not use real users, real credentials, secrets from another environment, or a production database. Keep `.env.load` private; it is gitignored. `TEST_USER_PASSWORD`, PostgreSQL values, and JWT secret must be synthetic values. Test emails use the reserved `example.test` domain.

`BASE_URL` is pinned to `http://api:8080` by the scripts; external and unknown URLs are rejected. Do not point either test at production.

## Prepare data

```powershell
Copy-Item tests/load/.env.load.example tests/load/.env.load
# Edit tests/load/.env.load with unique synthetic values.
./tests/load/prepare-load.ps1
```

`prepare-load.ps1` starts only the load PostgreSQL and API services, restores the existing pinned local dotnet tool, applies migrations to `fincore_load`, then registers or reuses synthetic sender/receiver pairs. `LOAD_TEST_USER_COUNT` controls the number of pairs and defaults to 25; values must be 25–500. `TEST_USER_PREFIX` defaults to `fincore-lt2`; generated addresses are distinct `example.test` addresses. Re-running with the same prefix and password reuses those users and resets sender fixture balances. If the password needs to change, choose a new prefix or explicitly use `-ResetLoadVolume`.

`data/accounts.generated.json` contains only `index`, `senderEmail`, `sourceAccountId`, and `destinationAccountId` for each pair. It contains no password, token, or connection string and is gitignored.

Sender balances are set directly in the isolated `fincore_load` database so each VU has enough funds. This is a ledger-external performance fixture, not a financial transaction. It is for this isolated performance environment only and must never be applied to normal FinCore or production data.

## LT-1 smoke test

```powershell
docker compose --project-name fincore-load --env-file tests/load/.env.load -f tests/load/compose.load.yaml --profile tools run --rm k6 run /scripts/smoke.js
```

Smoke uses one synthetic sender, one VU, and three iterations. It checks health, login, accounts, a 1.00 TRY transfer, and one idempotent replay.

## LT-2 normal load test

```powershell
docker compose --project-name fincore-load --env-file tests/load/.env.load -f tests/load/compose.load.yaml --profile tools run --rm k6 run /scripts/load.js
```

The normal load profile ramps to 5 VUs in 30 seconds, to 10 in 45 seconds, then to 25 in 60 seconds; it holds 25 VUs for 60 seconds and ramps down to zero over 30 seconds. The profile duration is 225 seconds (3 minutes 45 seconds), with 30-second `gracefulRampDown` and `gracefulStop`. This is a normal load test, not a stress, spike, or soak test.

Each VU uses its own sender/receiver pair and caches its JWT until it is near expiry. Every iteration requests `/api/accounts/me` and makes a unique-key 1.00 TRY transfer. Every tenth iteration replays the exact same key and payload and checks the same transaction ID plus `Idempotency-Replayed: true`. Iterations include 200–800 ms random think time.

The short console summary and machine-readable JSON are written to `tests/load/results/load-summary.json`. The results directory is gitignored; the JSON includes counts, rates, response-time aggregates, and p95 values only. It does not include request/response bodies, credentials, or tokens.

## Verify ledger invariants

After LT-2 completes, run:

```powershell
./tests/load/verify-load.ps1
```

The script reads only the isolated load database and checks ledger transaction/entry counts, debit and credit totals, duplicate idempotency scope/key records, and negative account balances. It exits non-zero if any invariant fails and does not print credentials or connection strings.

## Metrics and thresholds

- `successful_transfers` and `successful_replays`: requests whose successful response and transfer/replay assertions passed.
- `unexpected_statuses`, `unexpected_500s`, `transfer_conflicts`, and `authentication_failures`: unexpected responses and their relevant categories. A transfer `409` is an error for this unique-pair normal-load scenario.
- `http_req_failed`: failed HTTP request rate; `http_req_duration`: overall latency.
- `accounts_me_duration`, `transfer_new_duration`, `transfer_replay_duration`: per-operation latency trends.

Thresholds are checks above 99%, failed HTTP requests below 1%, no unexpected status/500/authentication failure/transfer conflict, p95 under 500 ms for accounts and under 1000 ms for new/replay transfers and overall requests. These are local starting observation thresholds, not production SLAs. Measured results depend on the machine running k6 and the Docker resources available to the API and PostgreSQL.

## Cleanup

Stop and remove only the isolated load-test containers and network:

```powershell
docker compose --project-name fincore-load --env-file tests/load/.env.load -f tests/load/compose.load.yaml down
```

The load volume is retained. Cleanup targets the `fincore-load` Compose project only; it does not remove `fincore_load_postgres_data` or touch any normal FinCore or DocumentFlow container/volume. Do not substitute another project or volume name.
