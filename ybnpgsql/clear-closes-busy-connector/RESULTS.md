# Validation results

Environment:

- Linux x64
- .NET 8 target
- `NpgsqlYugabyteDB` 9.0.2.2
- `yugabytedb/yugabyte:2025.2.3.0-b149`
- Docker 29.4.2

Results:

| Case | Command | Runs | Result |
| --- | --- | ---: | --- |
| Cluster-aware pool | `dotnet run -c Release` | 4 | 4/4 reproduced the Open/Closed invariant failure immediately after `dataSource.Clear()` |
| Non-cluster-aware control | `YB_REPRO_SMART=0 dotnet run -c Release` | 1 | Passed; busy connection remained `Open` and `SELECT 1` returned `1` |

The reduced reproduction uses one database node, no application schema, no Dapper, no concurrent operations, and no backend termination. It exercises the pool-clear behavior directly because critical PostgreSQL connector failures invoke that same clear path.
