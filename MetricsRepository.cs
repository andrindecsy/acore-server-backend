using Dapper;
using MySqlConnector;
using ServerMetricsApi.Models;

namespace ServerMetricsApi.Data;

public class MetricsRepository
{
    private readonly string _connectionString;

    public MetricsRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    private MySqlConnection GetConnection() => new(_connectionString);

    public async Task<StatusSummary?> GetCurrentStatusAsync()
    {
        using var connection = GetConnection();

        const string sql = """
            SELECT timestamp        AS Timestamp,
                   worldserver_rss_mb   AS WorldserverRssMb,
                   authserver_rss_mb    AS AuthserverRssMb,
                   characters_online    AS CharactersOnline,
                   worldserver_uptime_sec AS WorldserverUptimeSec
            FROM memory_log
            ORDER BY timestamp DESC
            LIMIT 1
            """;

        var latest = await connection.QuerySingleOrDefaultAsync(sql);
        if (latest is null)
        {
            return null;
        }

        DateTime lastTimestamp = latest.Timestamp;
        bool isOnline = DateTime.UtcNow - lastTimestamp < TimeSpan.FromMinutes(3);

        return new StatusSummary
        {
            IsOnline = isOnline,
            LastMeasurement = lastTimestamp,
            WorldserverRssMb = latest.WorldserverRssMb,
            AuthserverRssMb = latest.AuthserverRssMb,
            CharactersOnline = latest.CharactersOnline,
            WorldserverUptimeSec = latest.WorldserverUptimeSec
        };
    }

    public async Task<IEnumerable<MemoryLogEntry>> GetMetricsAsync(int hours)
    {
        using var connection = GetConnection();

        const string sql = """
            SELECT id                      AS Id,
                   timestamp                AS Timestamp,
                   total_mb                 AS TotalMb,
                   used_mb                  AS UsedMb,
                   free_mb                  AS FreeMb,
                   available_mb             AS AvailableMb,
                   swap_used_mb             AS SwapUsedMb,
                   worldserver_rss_mb       AS WorldserverRssMb,
                   worldserver_threads      AS WorldserverThreads,
                   worldserver_fds          AS WorldserverFds,
                   worldserver_uptime_sec   AS WorldserverUptimeSec,
                   authserver_rss_mb        AS AuthserverRssMb,
                   characters_online        AS CharactersOnline
            FROM memory_log
            WHERE timestamp >= @Since
            ORDER BY timestamp ASC
            """;

        DateTime since = DateTime.UtcNow.AddHours(-hours);
        return await connection.QueryAsync<MemoryLogEntry>(sql, new { Since = since });
    }

    public async Task<IEnumerable<ServerEvent>> GetEventsAsync(int days)
    {
        using var connection = GetConnection();

        const string sql = """
            SELECT id          AS Id,
                   timestamp    AS Timestamp,
                   event_type   AS EventType,
                   detail       AS Detail
            FROM server_events
            WHERE timestamp >= @Since
            ORDER BY timestamp DESC
            """;

        DateTime since = DateTime.UtcNow.AddDays(-days);
        return await connection.QueryAsync<ServerEvent>(sql, new { Since = since });
    }
}
