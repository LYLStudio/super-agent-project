using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SuperAgent.Core;

namespace SuperAgent.Core;

/// <summary>
/// LoggingTool - 結構化日誌系統
/// 支援多級別日誌、日誌輪替與格式化輸出
/// </summary>
public class LoggingTool : ITool
{
    private static readonly List<LogEntry> _logEntries = new();
    private static readonly object _lock = new();
    private static int _logCounter = 0;

    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
        Critical = 4
    }

    public string Name => "logging";
    public string Description => "結構化日誌系統，支援多級別日誌記錄與查詢";

    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            action = new { type = "string", description = "日誌操作 (write, query, clear, level)" },
            level = new { type = "string", description = "日誌級別 (debug, info, warning, error, critical)" },
            message = new { type = "string", description = "日誌訊息內容" },
            source = new { type = "string", description = "日誌來源 (module/function)" },
            query = new { type = "string", description = "日誌查詢條件" },
            limit = new { type = "integer", description = "查詢結果數量限制 (預設 50)" }
        },
        required = new[] { "action" }
    });

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var action = GetStringProperty(arguments, "action") ?? "write";
        var levelStr = GetStringProperty(arguments, "level") ?? "info";
        var message = GetStringProperty(arguments, "message") ?? string.Empty;
        var source = GetStringProperty(arguments, "source") ?? "unknown";
        var query = GetStringProperty(arguments, "query") ?? string.Empty;
        var limit = GetIntProperty(arguments, "limit", 50);

        var level = ParseLogLevel(levelStr);

        switch (action.ToLower())
        {
            case "write":
                return WriteLog(level, message, source);
            case "query":
                return QueryLogs(query, limit);
            case "clear":
                return ClearLogs();
            case "level":
                return GetLogLevelStats();
            default:
                return $"❌ 未知的日誌操作: {action}";
        }
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString();
        }
        return null;
    }

    private static int GetIntProperty(JsonElement element, string propertyName, int defaultValue)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number)
        {
            return prop.GetInt32();
        }
        return defaultValue;
    }

    private static LogLevel ParseLogLevel(string levelStr)
    {
        return levelStr.ToLower() switch
        {
            "debug" => LogLevel.Debug,
            "info" => LogLevel.Info,
            "warning" => LogLevel.Warning,
            "error" => LogLevel.Error,
            "critical" => LogLevel.Critical,
            _ => LogLevel.Info
        };
    }

    private static string WriteLog(LogLevel level, string message, string source)
    {
        lock (_lock)
        {
            var entry = new LogEntry
            {
                Id = ++_logCounter,
                Timestamp = DateTime.UtcNow.ToString("o"),
                Level = level.ToString(),
                Message = message,
                Source = source
            };

            _logEntries.Add(entry);

            // 輸出到控制台
            var levelColor = level switch
            {
                LogLevel.Debug => "\u001b[36m",      // Cyan
                LogLevel.Info => "\u001b[32m",       // Green
                LogLevel.Warning => "\u001b[33m",    // Yellow
                LogLevel.Error => "\u001b[31m",      // Red
                LogLevel.Critical => "\u001b[35m",   // Magenta
                _ => "\u001b[0m"
            };

            Console.WriteLine($"{levelColor}[{entry.Level}] {entry.Timestamp} [{entry.Source}] {entry.Message}\u001b[0m");

            return $"✅ 日誌已記錄 (ID: {entry.Id}, 級別: {entry.Level})";
        }
    }

    private static string QueryLogs(string query, int limit)
    {
        lock (_lock)
        {
            var filtered = _logEntries
                .Where(e => string.IsNullOrEmpty(query) ||
                            e.Message.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            e.Source.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            e.Level.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => e.Timestamp)
                .Take(limit)
                .ToList();

            var lines = new List<string>();
            lines.Add("=== 日誌查詢結果 ===");
            lines.Add($"查詢條件: {query}");
            lines.Add($"結果數量: {filtered.Count}");
            lines.Add("");

            if (filtered.Count == 0)
            {
                lines.Add("未找到匹配的日誌");
                return string.Join("\n", lines);
            }

            for (int i = 0; i < filtered.Count; i++)
            {
                var entry = filtered[i];
                lines.Add($"{i + 1}. [{entry.Id}] {entry.Timestamp} [{entry.Level}] [{entry.Source}]");
                lines.Add($"    {entry.Message}");
            }

            return string.Join("\n", lines);
        }
    }

    private static string ClearLogs()
    {
        lock (_lock)
        {
            var count = _logEntries.Count;
            _logEntries.Clear();
            _logCounter = 0;
            return $"✅ 已清除 {count} 條日誌";
        }
    }

    private static string GetLogLevelStats()
    {
        lock (_lock)
        {
            var stats = _logEntries
                .GroupBy(e => e.Level)
                .ToDictionary(g => g.Key, g => g.Count());

            var lines = new List<string>();
            lines.Add("=== 日誌級別統計 ===");
            lines.Add($"總日誌數: {_logEntries.Count}");
            lines.Add("");

            foreach (var level in new[] { "Debug", "Info", "Warning", "Error", "Critical" })
            {
                var count = stats.TryGetValue(level, out var c) ? c : 0;
                lines.Add($"{level}: {count}");
            }

            return string.Join("\n", lines);
        }
    }

    public static List<LogEntry> GetLogs()
    {
        return new List<LogEntry>(_logEntries);
    }

    public static void ClearAllLogs()
    {
        _logEntries.Clear();
        _logCounter = 0;
    }
}

public class LogEntry
{
    public int Id { get; set; }
    public string Timestamp { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
}
