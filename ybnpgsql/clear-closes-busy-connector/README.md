# NpgsqlYugabyteDB busy-connector clear reproduction

This standalone program demonstrates that `NpgsqlYugabyteDB` 9.0.2.2 can close a checked-out connector when a cluster-aware datasource is cleared. The logical `NpgsqlConnection` remains open, so reading its state throws:

```text
System.InvalidOperationException: Internal Npgsql bug: connection is in state Open but connector is in state Closed
```

The program starts one ephemeral `yugabytedb/yugabyte:2025.2.3.0-b149` node with Testcontainers. It does not require an application schema or external YugabyteDB deployment. The upstream `Npgsql` package is used only for the startup readiness probe; the datasource under test is built by `NpgsqlYugabyteDB`.

## Prerequisites

- Docker
- .NET 8 SDK

## Reproduce

```bash
dotnet run -c Release -f net8.0
```

The program:

1. Builds a YBNpgsql datasource with `LoadBalanceHosts.OnlyPrimary`.
2. Opens two physical connectors and returns both to the pool.
3. Checks one connector back out and leaves it busy.
4. Calls `dataSource.Clear()`.
5. Reads the busy connection's state.

`Clear()` should close idle connectors immediately and mark busy connectors to close when returned. Instead, the busy connector is physically closed while its logical connection remains open, producing the invariant failure above.

## Control

Disable cluster-aware balancing while keeping the same package, server, and sequence:

```bash
YB_REPRO_SMART=0 dotnet run -c Release -f net8.0
```

The control should report `Busy state after Clear: Open`, execute `SELECT 1`, and exit successfully.

## Likely driver boundary

`YBPoolingWrapperDataSource` keeps its own per-connection-string idle arrays and also calls the base pool bookkeeping. Its custom checkout removes a connector from the wrapper array but not from the base idle channel. The inherited `Clear()` can consequently drain the stale base-channel entry and close a connector that is currently checked out.

This also explains the backend-termination symptom: the Npgsql failure path calls `DataSource.Clear()` for critical PostgreSQL failures such as `57P01`, after which later connection cleanup observes the same logical-open/physical-closed mismatch.
