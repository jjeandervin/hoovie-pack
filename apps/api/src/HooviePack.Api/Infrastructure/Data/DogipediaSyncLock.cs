using Npgsql;

namespace HooviePack.Api.Infrastructure.Data;

public interface IDogipediaSyncLock
{
    Task<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken);
}

public sealed class DogipediaSyncLock(IConfiguration configuration) : IDogipediaSyncLock
{
    // Stable, application-specific key: ASCII "HPDOGIPC". Never use string.GetHashCode().
    public const long LockKey = 0x4850444F47495043;

    public async Task<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        // This dedicated physical session is never returned to a pool with a held lock,
        // even if explicit unlocking fails during shutdown or a network outage.
        var settings = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("DefaultConnection"))
        { Pooling = false, Multiplexing = false };
        var connection = new NpgsqlConnection(settings.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
            command.Parameters.AddWithValue("key", LockKey);
            if (await command.ExecuteScalarAsync(cancellationToken) is true)
                return new Lease(connection);
            await connection.DisposeAsync();
            return null;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class Lease(NpgsqlConnection connection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
                command.Parameters.AddWithValue("key", LockKey);
                await command.ExecuteScalarAsync(timeout.Token);
            }
            finally { await connection.DisposeAsync(); }
        }
    }
}
