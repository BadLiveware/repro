# [YSQL] C# smart driver closes a busy connector during datasource Clear

## Description

`NpgsqlYugabyteDB` 9.0.2.2 can physically close a connector that is currently checked out when `NpgsqlDataSource.Clear()` is called with cluster-aware load balancing enabled. The logical `NpgsqlConnection` remains open, so its next state check throws the driver's internal invariant error:

```text
System.InvalidOperationException: Internal Npgsql bug: connection is in state Open but connector is in state Closed
   at YBNpgsql.NpgsqlConnection.get_FullState()
   at YBNpgsql.NpgsqlConnection.get_State()
```

### Environment

- `NpgsqlYugabyteDB` 9.0.2.2
- YugabyteDB `2025.2.3.0-b149`
- .NET 8
- Linux x64
- `LoadBalanceHosts.OnlyPrimary`
- Pooling enabled, `MaxPoolSize=6`

### Reproduction

This directory contains the complete standalone Testcontainers project. It requires only Docker and the .NET 8 SDK:

```bash
dotnet run -c Release -f net8.0
```

The program performs this sequence against one ephemeral YugabyteDB node:

1. Build a YBNpgsql datasource with `LoadBalanceHosts.OnlyPrimary`.
2. Open two physical connections simultaneously, then return both to the pool.
3. Check one connection back out and leave it open.
4. Call `dataSource.Clear()`.
5. Read the checked-out connection's `State`.

Observed output:

```text
Prewarmed: 127.0.0.2:401, 127.0.0.2:410
Busy before Clear: 127.0.0.2:401
Called dataSource.Clear() while the connection is busy.
REPRODUCED:
System.InvalidOperationException: Internal Npgsql bug: connection is in state Open but connector is in state Closed
```

### Expected behavior

`Clear()` should immediately close idle connectors. A checked-out connector should remain usable and be closed only when returned to the pool, matching the documented Npgsql pool-clear contract.

### Actual behavior

The checked-out connector is closed immediately while the logical connection remains open.

### Control

The same package, server, and sequence passes when cluster-aware load balancing is disabled:

```bash
YB_REPRO_SMART=0 dotnet run -c Release -f net8.0
```

Expected control output:

```text
Busy state after Clear: Open
Query after Clear: 1
No exception with smartBalancing=False.
```

### Operational impact

This is reachable without an application explicitly clearing pools. The connector failure path calls `DataSource.Clear()` for critical PostgreSQL failures such as SQLSTATE `57P01` (`terminating connection due to administrator command`). After a TServer/backend interruption, later operations can therefore receive a logical connection backed by a closed connector and fail during normal query or cleanup work.

### Likely driver boundary

`YBPoolingWrapperDataSource` maintains custom per-connection-string connector/idle arrays while also using the base `PoolingDataSource` bookkeeping:

- [custom checkout](https://github.com/yugabyte/npgsql/blob/8b0883a29988bec3d78d5532b890f65e680f8099/src/Npgsql/YBPoolingWrapperDataSource.cs#L79-L118) removes a connector from `connStringToIdleConnectorsMap`
- [custom return](https://github.com/yugabyte/npgsql/blob/8b0883a29988bec3d78d5532b890f65e680f8099/src/Npgsql/YBPoolingWrapperDataSource.cs#L120-L169) updates those arrays and then calls `base.Return(connector)`
- [inherited `Clear()`](https://github.com/yugabyte/npgsql/blob/8b0883a29988bec3d78d5532b890f65e680f8099/src/Npgsql/PoolingDataSource.cs#L350-L373) drains the base idle channel

The custom checkout does not remove the connector's stale entry from the base idle channel. `Clear()` can consequently drain that entry and close a connector currently checked out through the custom map. The release build's [`CheckIdleConnector`](https://github.com/yugabyte/npgsql/blob/8b0883a29988bec3d78d5532b890f65e680f8099/src/Npgsql/PoolingDataSource.cs#L223-L263) only rejects `Broken`, not `Closed`, before its debug-only `ConnectorState.Ready` assertion.

Issue type: `kind/bug`.
