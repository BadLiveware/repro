using System.Data.Common;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using YBNpgsql;

const string image = "yugabytedb/yugabyte:2025.2.3.0-b149";
const string host = "127.0.0.2";
const string connectionString =
    $"Host={host};Port=5433;Database=yugabyte;Username=yugabyte;Include Error Detail=true;Timeout=15;Command Timeout=60";
var smartBalancing = Environment.GetEnvironmentVariable("YB_REPRO_SMART") != "0";

var node = new ContainerBuilder(image)
    .WithCommand("bin/yugabyted", "start", "--background=false", "--insecure", "--ui=false", $"--advertise_address={host}")
    .WithCreateParameterModifier(p => (p.HostConfig ??= new()).NetworkMode = "host")
    .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("YugabyteDB Started"))
    .Build();

try
{
    await node.StartAsync();
    await WaitForConnectableAsync(connectionString);

    var builder = new NpgsqlDataSourceBuilder(connectionString) { Name = "yb-clear-repro" };
    builder.ConnectionStringBuilder.LoadBalanceHosts = smartBalancing
        ? LoadBalanceHosts.OnlyPrimary
        : LoadBalanceHosts.False;
    builder.ConnectionStringBuilder.MaxPoolSize = 6;
    await using var dataSource = builder.Build();

    // Create two physical connectors, then return both to the smart-driver pool.
    var first = dataSource.CreateConnection();
    var second = dataSource.CreateConnection();
    await first.OpenAsync();
    await second.OpenAsync();
    Console.WriteLine($"Prewarmed: {await ReadBackendAsync(first)}, {await ReadBackendAsync(second)}");
    await first.DisposeAsync();
    await second.DisposeAsync();

    // Check one connector back out. Clear() must not close this busy connector: the documented
    // contract is to close it only when it is returned to the pool.
    var busy = dataSource.CreateConnection();
    await busy.OpenAsync();
    Console.WriteLine($"Busy before Clear: {await ReadBackendAsync(busy)}");

    dataSource.Clear();
    Console.WriteLine("Called dataSource.Clear() while the connection is busy.");

    // The failure is immediate: querying State evaluates YBNpgsql's logical/physical invariant.
    Console.WriteLine($"Busy state after Clear: {busy.State}");
    await using var command = busy.CreateCommand();
    command.CommandText = "SELECT 1";
    Console.WriteLine($"Query after Clear: {await command.ExecuteScalarAsync()}");
    await busy.DisposeAsync();
    Console.WriteLine($"No exception with smartBalancing={smartBalancing}.");
}
catch (Exception ex)
{
    Console.Error.WriteLine("REPRODUCED:");
    Console.Error.WriteLine(ex);
    Environment.ExitCode = 1;
}
finally
{
    await node.DisposeAsync();
}

static async Task WaitForConnectableAsync(string connectionString)
{
    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(120);
    Exception? last = null;
    while (DateTime.UtcNow < deadline)
    {
        try
        {
            await using var connection = new global::Npgsql.NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            return;
        }
        catch (Exception ex)
        {
            last = ex;
            await Task.Delay(500);
        }
    }
    throw new InvalidOperationException("YugabyteDB did not become connectable.", last);
}

static async Task<string> ReadBackendAsync(DbConnection connection)
{
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT host(inet_server_addr()) || ':' || pg_backend_pid()";
    return (string)(await command.ExecuteScalarAsync())!;
}
