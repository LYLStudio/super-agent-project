using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SuperAgent.Core;

/// <summary>
/// DatabaseTool - 資料庫查詢工具，支援 SQLite、MySQL、PostgreSQL
/// </summary>
public class DatabaseTool : ITool
{
    private const int DefaultMaxRows = 100;
    private const int MaximumMaxRows = 1000;
    private const int DefaultMaxOutputCharacters = 20000;
    private const int MaximumOutputCharacters = 100000;
    private const int MaximumCellCharacters = 4096;

    public string Name => "database_query";

    public string Description => "執行資料庫查詢。支援 SQLite、MySQL、PostgreSQL。結果最多回傳 100 列與 20000 字元。";

    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            connection_string = new { type = "string", description = "資料庫連接字串" },
            query = new { type = "string", description = "SQL 查詢語句" },
            parameters = new { type = "object", description = "查詢參數 (預設: {})" },
            timeout_seconds = new { type = "integer", description = "查詢超時秒數 (預設 30，範圍 1 到 300)" },
            max_rows = new { type = "integer", description = "最大回傳資料列數 (預設 100，範圍 1 到 1000)" },
            max_output_characters = new { type = "integer", description = "最大回傳字元數 (預設 20000，範圍 256 到 100000)" }
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
        int timeoutSeconds = GetIntArgument(arguments, "timeout_seconds", 30);
        int maxRows = GetIntArgument(arguments, "max_rows", DefaultMaxRows);
        int maxOutputCharacters = GetIntArgument(arguments, "max_output_characters", DefaultMaxOutputCharacters);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return "Error: 資料庫連接字串不能為空";
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return "Error: 查詢語句不能為空";
        }

        if (timeoutSeconds is < 1 or > 300)
        {
            return "Error: timeout_seconds 必須介於 1 到 300";
        }

        if (maxRows is < 1 or > MaximumMaxRows)
        {
            return $"Error: max_rows 必須介於 1 到 {MaximumMaxRows}";
        }

        if (maxOutputCharacters is < 256 or > MaximumOutputCharacters)
        {
            return $"Error: max_output_characters 必須介於 256 到 {MaximumOutputCharacters}";
        }

        try
        {
            if (connectionString.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
            {
                return await ExecuteSqliteQuery(
                    connectionString, query, parameters, timeoutSeconds, maxRows, maxOutputCharacters, cancellationToken);
            }

            return await ExecuteGenericQuery(
                connectionString, query, parameters, timeoutSeconds, maxRows, maxOutputCharacters, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return $"Error executing database query: {ex.Message}";
        }
    }

    private static async Task<string> ExecuteSqliteQuery(
        string connectionString,
        string query,
        JsonElement parameters,
        int timeoutSeconds,
        int maxRows,
        int maxOutputCharacters,
        CancellationToken cancellationToken)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = query;
        command.CommandTimeout = timeoutSeconds;
        AddParameters(command, parameters);
        return await ReadResultsAsync(command, maxRows, maxOutputCharacters, cancellationToken);
    }

    private static async Task<string> ExecuteGenericQuery(
        string connectionString,
        string query,
        JsonElement parameters,
        int timeoutSeconds,
        int maxRows,
        int maxOutputCharacters,
        CancellationToken cancellationToken)
    {
        string providerName = connectionString.StartsWith("Server=", StringComparison.OrdinalIgnoreCase)
            ? "MySql.Data.MySqlClient"
            : "Npgsql";

        using var connection = DbProviderFactories.GetFactory(providerName).CreateConnection();
        if (connection is null)
        {
            return "Error: Unable to create database connection";
        }

        connection.ConnectionString = connectionString;
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = query;
        command.CommandTimeout = timeoutSeconds;
        AddParameters(command, parameters);
        return await ReadResultsAsync(command, maxRows, maxOutputCharacters, cancellationToken);
    }

    private static void AddParameters(DbCommand command, JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in parameters.EnumerateObject())
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = $"@{property.Name}";
            parameter.Value = property.Value.ValueKind switch
            {
                JsonValueKind.Null => DBNull.Value,
                JsonValueKind.String => property.Value.GetString()!,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number when property.Value.TryGetInt64(out var integer) => integer,
                JsonValueKind.Number when property.Value.TryGetDecimal(out var number) => number,
                _ => property.Value.GetRawText()
            };
            command.Parameters.Add(parameter);
        }
    }

    private static async Task<string> ReadResultsAsync(
        DbCommand command,
        int maxRows,
        int maxOutputCharacters,
        CancellationToken cancellationToken)
    {
        const int reportOverhead = 128;
        var rows = new List<string>(Math.Min(maxRows, 100));
        var body = new StringBuilder(Math.Min(maxOutputCharacters, 4096));
        var truncated = false;
        var bodyLimit = maxOutputCharacters - reportOverhead;

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (rows.Count >= maxRows)
            {
                truncated = true;
                break;
            }

            var row = new Dictionary<string, string>(reader.FieldCount);
            for (var index = 0; index < reader.FieldCount; index++)
            {
                row[reader.GetName(index)] = FormatValue(reader.GetValue(index));
            }

            var serializedRow = JsonSerializer.Serialize(row);
            if (body.Length + serializedRow.Length + Environment.NewLine.Length > bodyLimit)
            {
                truncated = true;
                break;
            }

            rows.Add(serializedRow);
            body.AppendLine(serializedRow);
        }

        if (rows.Count == 0 && !truncated)
        {
            return "Query executed successfully. No rows returned.";
        }

        var message = $"Query executed successfully. {rows.Count} row(s) returned:\n{body}";
        if (truncated)
        {
            message += $"Results truncated at the configured limit ({maxRows} rows or {maxOutputCharacters} characters).";
        }
        return message;
    }

    private static string FormatValue(object value)
    {
        if (value is DBNull)
        {
            return "NULL";
        }

        if (value is byte[] bytes)
        {
            return $"<binary data: {bytes.Length} bytes>";
        }

        var text = value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty
            : value.ToString() ?? string.Empty;
        return text.Length <= MaximumCellCharacters
            ? text
            : text[..MaximumCellCharacters] + "…";
    }

    private static int GetIntArgument(JsonElement arguments, string name, int defaultValue)
    {
        return arguments.TryGetProperty(name, out var property) && property.TryGetInt32(out var value)
            ? value
            : defaultValue;
    }
}
