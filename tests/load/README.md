# FinCore LT-1: isolated smoke test

## Requirements

- Docker Engine/Desktop with Compose v2
- .NET 10 SDK and access to the repository's pinned `dotnet-ef` tool
- PowerShell 7 or Windows PowerShell 5.1
- The project restore/build prerequisites already available locally

The k6 service uses the explicitly pinned `grafana/k6:1.8.2` image. The load API is built from the repository's existing `Dockerfile`.

## Isolation and test data

This environment has its own Compose project (`fincore-load`), PostgreSQL database (`fincore_load`), fixed named volume (`fincore_load_postgres_data`), and host ports `18080` (API) and `55432` (PostgreSQL). It does not use the normal Compose project, `fincore_postgres_data` volume, normal database, or normal `.env`. Containers communicate on their isolated Compose network; k6's default URL is `http://api:8080`.

Copy `.env.load.example` to `.env.load`, then replace the placeholders with unique synthetic load-test-only values. Do not use real users, real credentials, secrets from another environment, or a production database. Keep `.env.load` private; it is gitignored. Set distinct `TEST_USER_EMAIL` and `TEST_RECEIVER_EMAIL` values under the reserved synthetic `example.test` domain and a test-only `TEST_USER_PASSWORD` meeting registration rules.

`prepare-load.ps1` starts only the load PostgreSQL and API services, restores the already-pinned local dotnet tool manifest, applies migrations through the load-only database/port, registers or reuses the synthetic sender and receiver, and creates `data/accounts.generated.json` with only their account IDs. It does not persist passwords, JWTs, refresh tokens, or connection strings. Re-running it reuses matching synthetic users and refreshes the sender fixture balance. If a password or email must change, use new synthetic emails or explicitly reset the load volume with `-ResetLoadVolume`.

The sender's starting balance is prepared by directly updating the load database. This is a performance fixture outside the ledger, not a real financial transaction. It is restricted to the isolated performance environment and must never be used against normal FinCore or production data. The script pins its database and volume names and refuses a base URL other than `http://api:8080`.

## Prepare and run

```powershell
Copy-Item tests/load/.env.load.example tests/load/.env.load
# Edit tests/load/.env.load with unique synthetic values.
./tests/load/prepare-load.ps1
docker compose --project-name fincore-load --env-file tests/load/.env.load -f tests/load/compose.load.yaml --profile tools run --rm k6
```

The smoke scenario runs 1 VU for 3 iterations. It checks liveness/readiness, logs in the synthetic sender, verifies the sender's own account, transfers `1.00` TRY to the synthetic receiver with a new UUID idempotency key per iteration, and replays the first iteration's same key and payload. The replay must return the same transaction ID and `Idempotency-Replayed: true`; the replay response itself confirms no second transaction was executed.

Do not override `BASE_URL`; `http://api:8080` is the only accepted target in this fixture. Never point this test at production or an unknown external environment.

## Results and thresholds

k6 prints its standard summary. `checks` is the pass rate for explicit HTTP/body/ownership/idempotency assertions; `http_req_failed` is the failed HTTP request rate; `unexpected_500` counts server errors; `http_req_duration` reports request latency, including p95. The machine-readable raw and summary reports are written under `tests/load/results/` and are gitignored.

Thresholds are `checks == 100%`, `http_req_failed < 1%`, `unexpected_500 == 0`, and `http_req_duration p95 < 1000 ms`. These are starting observation thresholds for this smoke run, not production SLAs.

## Cleanup

Stop and remove only the isolated load-test containers/network:

```powershell
docker compose --project-name fincore-load --env-file tests/load/.env.load -f tests/load/compose.load.yaml down
```

The load volume is retained by default. To deliberately erase only this environment's data, use the explicit reset switch (it first stops the load project and targets the fixed `fincore_load_postgres_data` volume):

```powershell
./tests/load/prepare-load.ps1 -ResetLoadVolume
```

The reset does not remove `fincore_postgres_data` or any normal FinCore database/volume. Never run cleanup commands with a different Compose project or volume name.
