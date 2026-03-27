using Microsoft.Data.Sqlite;

namespace Chronos.Master;

public sealed class MasterStore
{
    private readonly string _connectionString;

    public MasterStore(IConfiguration configuration)
    {
        var dbPath = configuration["CHRONOS_MASTER_DB_PATH"] ?? Path.Combine(AppContext.BaseDirectory, "master.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        EnsureSchema();
    }

    public async Task UpsertAgentAsync(AgentRegistrationRequest request, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await using var con = new SqliteConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);

        await using var cmd = con.CreateCommand();
        cmd.CommandText = """
            INSERT INTO agents(agent_id, base_url, location, capabilities_json, registered_utc, last_heartbeat_utc)
            VALUES ($id, $url, $location, $caps, $registered, $heartbeat)
            ON CONFLICT(agent_id) DO UPDATE SET
              base_url = excluded.base_url,
              location = excluded.location,
              capabilities_json = excluded.capabilities_json,
              last_heartbeat_utc = excluded.last_heartbeat_utc;
            """;
        cmd.Parameters.AddWithValue("$id", request.AgentId);
        cmd.Parameters.AddWithValue("$url", request.BaseUrl);
        cmd.Parameters.AddWithValue("$location", (object?)request.Location ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$caps", System.Text.Json.JsonSerializer.Serialize(request.Capabilities ?? new Dictionary<string, string>()));
        cmd.Parameters.AddWithValue("$registered", now.ToString("O"));
        cmd.Parameters.AddWithValue("$heartbeat", now.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateHeartbeatAsync(string agentId, AgentHeartbeatRequest request, CancellationToken ct)
    {
        await using var con = new SqliteConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = """
            UPDATE agents
            SET last_heartbeat_utc = $heartbeat, cpu_percent = $cpu, memory_percent = $mem, disk_percent = $disk
            WHERE agent_id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", agentId);
        cmd.Parameters.AddWithValue("$heartbeat", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$cpu", request.CpuPercent);
        cmd.Parameters.AddWithValue("$mem", request.MemoryPercent);
        cmd.Parameters.AddWithValue("$disk", request.DiskPercent);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<AgentInfo>> ListAgentsAsync(CancellationToken ct)
    {
        var list = new List<AgentInfo>();
        await using var con = new SqliteConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = """
            SELECT agent_id, base_url, location, capabilities_json, registered_utc, last_heartbeat_utc,
                   cpu_percent, memory_percent, disk_percent
            FROM agents
            ORDER BY agent_id;
            """;
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new AgentInfo
            {
                AgentId = reader.GetString(0),
                BaseUrl = reader.GetString(1),
                Location = reader.IsDBNull(2) ? null : reader.GetString(2),
                CapabilitiesJson = reader.IsDBNull(3) ? "{}" : reader.GetString(3),
                RegisteredUtc = DateTimeOffset.Parse(reader.GetString(4)),
                LastHeartbeatUtc = DateTimeOffset.Parse(reader.GetString(5)),
                CpuPercent = reader.IsDBNull(6) ? 0 : reader.GetDouble(6),
                MemoryPercent = reader.IsDBNull(7) ? 0 : reader.GetDouble(7),
                DiskPercent = reader.IsDBNull(8) ? 0 : reader.GetDouble(8)
            });
        }

        return list;
    }

    public async Task DeleteStaleAgentsAsync(TimeSpan ttl, CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow - ttl;
        await using var con = new SqliteConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = """
            DELETE FROM agents
            WHERE last_heartbeat_utc < $cutoff;
            """;
        cmd.Parameters.AddWithValue("$cutoff", cutoff.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task AppendAuditAsync(AuditLogEntry entry, CancellationToken ct)
    {
        await using var con = new SqliteConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = """
            INSERT INTO audit_logs(utc_time, action, result, actor, client_ip, details)
            VALUES ($time, $action, $result, $actor, $clientIp, $details);
            """;
        cmd.Parameters.AddWithValue("$time", entry.UtcTime.ToString("O"));
        cmd.Parameters.AddWithValue("$action", entry.Action);
        cmd.Parameters.AddWithValue("$result", entry.Result);
        cmd.Parameters.AddWithValue("$actor", (object?)entry.Actor ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$clientIp", (object?)entry.ClientIp ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$details", (object?)entry.Details ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<AuditLogEntry>> ListAuditAsync(int limit, CancellationToken ct)
    {
        var list = new List<AuditLogEntry>();
        await using var con = new SqliteConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = """
            SELECT id, utc_time, action, result, actor, client_ip, details
            FROM audit_logs
            ORDER BY id DESC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$limit", Math.Max(1, limit));
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new AuditLogEntry
            {
                Id = reader.GetInt64(0),
                UtcTime = DateTimeOffset.Parse(reader.GetString(1)),
                Action = reader.GetString(2),
                Result = reader.GetString(3),
                Actor = reader.IsDBNull(4) ? null : reader.GetString(4),
                ClientIp = reader.IsDBNull(5) ? null : reader.GetString(5),
                Details = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }

        return list;
    }

    public async Task DeleteOldAuditAsync(TimeSpan retention, CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow - retention;
        await using var con = new SqliteConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = "DELETE FROM audit_logs WHERE utc_time < $cutoff;";
        cmd.Parameters.AddWithValue("$cutoff", cutoff.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpsertProjectPlacementAsync(string projectName, string agentId, string agentUrl, CancellationToken ct)
    {
        await using var con = new SqliteConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = """
            INSERT INTO project_placements(project_name, agent_id, agent_url, updated_utc)
            VALUES ($project, $agentId, $agentUrl, $updated)
            ON CONFLICT(project_name) DO UPDATE SET
              agent_id = excluded.agent_id,
              agent_url = excluded.agent_url,
              updated_utc = excluded.updated_utc;
            """;
        cmd.Parameters.AddWithValue("$project", projectName);
        cmd.Parameters.AddWithValue("$agentId", agentId);
        cmd.Parameters.AddWithValue("$agentUrl", agentUrl);
        cmd.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<ProjectPlacementInfo?> GetProjectPlacementAsync(string projectName, CancellationToken ct)
    {
        await using var con = new SqliteConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = """
            SELECT project_name, agent_id, agent_url, updated_utc
            FROM project_placements
            WHERE project_name = $project;
            """;
        cmd.Parameters.AddWithValue("$project", projectName);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;

        return new ProjectPlacementInfo
        {
            ProjectName = reader.GetString(0),
            AgentId = reader.GetString(1),
            AgentUrl = reader.GetString(2),
            UpdatedUtc = DateTimeOffset.Parse(reader.GetString(3))
        };
    }

    public async Task<List<ProjectPlacementInfo>> ListProjectPlacementsAsync(CancellationToken ct)
    {
        var list = new List<ProjectPlacementInfo>();
        await using var con = new SqliteConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = """
            SELECT project_name, agent_id, agent_url, updated_utc
            FROM project_placements
            ORDER BY project_name;
            """;
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new ProjectPlacementInfo
            {
                ProjectName = reader.GetString(0),
                AgentId = reader.GetString(1),
                AgentUrl = reader.GetString(2),
                UpdatedUtc = DateTimeOffset.Parse(reader.GetString(3))
            });
        }
        return list;
    }

    public async Task UpsertVolumePlacementAsync(VolumePlacementReport request, CancellationToken ct)
    {
        await using var con = new SqliteConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = """
            INSERT INTO volume_placements(project_name, volume_name, agent_id, role, bytes_used, updated_utc)
            VALUES ($project, $volume, $agent, $role, $bytes, $updated)
            ON CONFLICT(project_name, volume_name, agent_id) DO UPDATE SET
              role = excluded.role,
              bytes_used = excluded.bytes_used,
              updated_utc = excluded.updated_utc;
            """;
        cmd.Parameters.AddWithValue("$project", request.ProjectName);
        cmd.Parameters.AddWithValue("$volume", request.VolumeName);
        cmd.Parameters.AddWithValue("$agent", request.AgentId);
        cmd.Parameters.AddWithValue("$role", request.Role);
        cmd.Parameters.AddWithValue("$bytes", (object?)request.BytesUsed ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<VolumePlacementInfo>> ListVolumePlacementsAsync(string? projectName, CancellationToken ct)
    {
        var list = new List<VolumePlacementInfo>();
        await using var con = new SqliteConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = con.CreateCommand();
        if (string.IsNullOrWhiteSpace(projectName))
        {
            cmd.CommandText = """
                SELECT project_name, volume_name, agent_id, role, bytes_used, updated_utc
                FROM volume_placements
                ORDER BY project_name, volume_name, agent_id;
                """;
        }
        else
        {
            cmd.CommandText = """
                SELECT project_name, volume_name, agent_id, role, bytes_used, updated_utc
                FROM volume_placements
                WHERE project_name = $project
                ORDER BY volume_name, agent_id;
                """;
            cmd.Parameters.AddWithValue("$project", projectName);
        }

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new VolumePlacementInfo
            {
                ProjectName = reader.GetString(0),
                VolumeName = reader.GetString(1),
                AgentId = reader.GetString(2),
                Role = reader.GetString(3),
                BytesUsed = reader.IsDBNull(4) ? null : reader.GetInt64(4),
                UpdatedUtc = DateTimeOffset.Parse(reader.GetString(5))
            });
        }
        return list;
    }

    private void EnsureSchema()
    {
        using var con = new SqliteConnection(_connectionString);
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS agents (
              agent_id TEXT PRIMARY KEY,
              base_url TEXT NOT NULL,
              location TEXT NULL,
              capabilities_json TEXT NOT NULL,
              registered_utc TEXT NOT NULL,
              last_heartbeat_utc TEXT NOT NULL,
              cpu_percent REAL NULL,
              memory_percent REAL NULL,
              disk_percent REAL NULL
            );

            CREATE TABLE IF NOT EXISTS volume_placements (
              project_name TEXT NOT NULL,
              volume_name TEXT NOT NULL,
              agent_id TEXT NOT NULL,
              role TEXT NOT NULL,
              bytes_used INTEGER NULL,
              updated_utc TEXT NOT NULL,
              PRIMARY KEY (project_name, volume_name, agent_id)
            );

            CREATE TABLE IF NOT EXISTS audit_logs (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              utc_time TEXT NOT NULL,
              action TEXT NOT NULL,
              result TEXT NOT NULL,
              actor TEXT NULL,
              client_ip TEXT NULL,
              details TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS project_placements (
              project_name TEXT PRIMARY KEY,
              agent_id TEXT NOT NULL,
              agent_url TEXT NOT NULL,
              updated_utc TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }
}
