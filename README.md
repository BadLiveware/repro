# Reproductions

Minimal, standalone projects for reproducible upstream bug reports.

## YBNpgsql

- [`clear-closes-busy-connector`](ybnpgsql/clear-closes-busy-connector/) — `NpgsqlYugabyteDB` 9.0.2.2 closes a checked-out connector when a cluster-aware datasource is cleared, leaving the logical connection open. Reported as [yugabyte-db#32654](https://github.com/yugabyte/yugabyte-db/issues/32654).
