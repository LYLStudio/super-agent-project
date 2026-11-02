using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SuperAgent.Core;

/// <summary>
/// DatabaseTool - 資料庫查詢工具，支援 SQLite、MySQL、PostgreSQL
/// </summary>
public class DatabaseTool : ITool
{
    public string Name => "database_query";

    public string Description => "執行資料庫查詢。支援 SQLite、MySQL、PostgreSQL。適用於查詢資料、插入記錄與更新資料。";

    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            connection_string = new { type = "string", description = "資料庫連接字串" },
            query = new { type = "string", description = "SQL 查詢語句" },
            parameters = new { type = "object", description = "查詢參數 (預設: {})" },
            timeout_seconds = new { type = "number", description = "查詢超時秒數 (預設 30)" }
        },
        required = new[] { "connection_string", "query" }
    });

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        string connectionString = arguments.GetProperty("connection_string").GetString() ?? string.Empty;
        string query = arguments.GetProperty("query").GetString() ?? string.Empty;
        JsonElement parameters = arguments.TryGetProperty("parameters", out var paramsProp)
            ? paramsProp
            : JsonSerializer.SerializeToElement(new { });
        int timeoutSeconds = arguments.TryGetProperty("timeout_seconds", out var timeoutProp)
            ? (int)timeoutProp.GetDouble()
            : 30;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return "Error: 資料庫連接字串不能為空";
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return "Error: 查詢語句不能為空";
        }

        try
        {
            // 檢查是否為 SQLite (檔案路徑)
            if (connectionString.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
            {
                return await ExecuteSqliteQuery(connectionString, query, parameters, cancellationToken);
            }

            // MySQL / PostgreSQL - 使用 ADO.NET
            return await ExecuteGenericQuery(connectionString, query, parameters, cancellationToken, timeoutSeconds);
        }
        catch (Exception ex)
        {
            return $"Error executing database query: {ex.Message}";
        }
    }
    private async Task<string> ExecuteSqliteQuery(
        string connectionString,
        string query,
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = query;
            command.CommandTimeout = 30;

            // 填入參數
            if (parameters.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in parameters.EnumerateObject())
                {
                    var param = command.CreateParameter();
                    param.ParameterName = $"@{prop.Name}";
                    param.Value = prop.Value.GetString() ?? prop.Value.GetRawText();
                    command.Parameters.Add(param);
                }
            }

            var results = new List<string>();

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = new Dictionary<string, string>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? "NULL" : reader.GetString(i);
                }
                results.Add(JsonSerializer.Serialize(row));
            }

            if (results.Count == 0)
            {
                return "Query executed successfully. No rows returned.";
            }

            return $"Query executed successfully. {results.Count} row(s) returned:\n{string.Join("\n", results)}";
        }
        catch (Exception ex)
        {
            return $"Error executing SQLite query: {ex.Message}";
        }
    }

    private async Task<string> ExecuteGenericQuery(
        string connectionString,
        string query,
        JsonElement parameters,
        CancellationToken cancellationToken,
        int timeoutSeconds)
    {
        try
        {
            // 根據連接字串前缀決定提供者
            string providerName = connectionString.StartsWith("Server=", StringComparison.OrdinalIgnoreCase)
                ? "MySql.Data.MySqlClient"
                : "Npgsql";

            using var connection = System.Data.Common.DbProviderFactories.GetFactory(providerName).CreateConnection();
            if (connection is null)
            {
                return "Error: Unable to create database connection";
            }

            connection.ConnectionString = connectionString;
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = query;
            command.CommandTimeout = timeoutSeconds;

            // 填入參數
            if (parameters.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in parameters.EnumerateObject())
                {
                    var param = command.CreateParameter();
                    param.ParameterName = $"@{prop.Name}";
                    param.Value = prop.Value.GetString() ?? prop.Value.GetRawText();
                    command.Parameters.Add(param);
                }
            }

            var results = new List<string>();

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = new Dictionary<string, string>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? "NULL" : reader.GetString(i);
                }
                results.Add(JsonSerializer.Serialize(row));
            }

            if (results.Count == 0)
            {
                return "Query executed successfully. No rows returned.";
            }

            return $"Query executed successfully. {results.Count} row(s) returned:\n{string.Join("\n", results)}";
        }
        catch (Exception ex)
        {
            return $"Error executing database query: {ex.Message}";
        }
    }
}
