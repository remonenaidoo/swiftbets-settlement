# swiftbets-settlement

[![ci](https://github.com/remonenaidoo/swiftbets-settlement/actions/workflows/ci.yml/badge.svg)](https://github.com/remonenaidoo/swiftbets-settlement/actions/workflows/ci.yml)

Settlement for SwiftBets: indexes open legs by fixture, evaluates legs when results arrive (priority-gated by result version), counts resolved legs with token-guarded Lua counters in Redis, and settles coupons in a second stage off Kafka. A reconciler repairs Redis/SQL divergence and raises stuck coupons; unprocessable messages park on an inbound DLQ while the partition keeps flowing.

## Hosts

- `SwiftBets.Settlement.Worker`: consumers, reconciler and the operator refresh command.
- `SwiftBets.Settlement.Migrator`: one-shot DbUp migrator.

## Data and events

- **Owns:** SQL Server `SbSettlement` (leg index, results, evaluations, settlements, outbox, inbox) and Redis (progress counters and token sets).
- **Events:** Consumes `placement.coupon-placed`, `offer.result-published`, `settlement.leg-evaluated`; produces `settlement.leg-evaluated`, `settlement.coupon-settled`, `settlement.stuck-coupon`.

## Layout

Clean Architecture, enforced by project references and `*.ArchitectureTests`:

```
src/*.Domain          pure domain, no references
src/*.Application     use cases and ports; depends on Domain and contracts only
src/*.Infrastructure  adapters (Dapper + embedded .sql, Kafka, Redis); implements Application ports
src/*.Api | *.Worker  composition root: observability, error envelope, health, metrics
src/*.Migrator        DbUp scripts under Migrations/, run once before the host starts
```

Every host exposes `/health/live`, `/health/ready` (checks its real dependencies), `/metrics` (Prometheus), logs compact JSON with correlation ids, and exports traces over OTLP.

## Build and test

```bash
../swiftbets-platform/scripts/fetch-shared-packages.sh .   # or pack-local.sh for unreleased shared changes
dotnet test SwiftBets.Settlement.slnx
```

Integration tests use Testcontainers and need Docker. The whole platform runs from `swiftbets-platform` with `make up`.

## Images

Multi-arch (amd64 + arm64), non-root, chiseled runtime:

- `ghcr.io/remonenaidoo/swiftbets-settlement`
- `ghcr.io/remonenaidoo/swiftbets-settlement-migrator`

## License

MIT
