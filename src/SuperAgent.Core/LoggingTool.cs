using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
    private const int MaximumStoredEntries = 1000;
    private const int MaximumQueryResults = 200;
    private const int MaximumQueryOutputCharacters = 20000;
    private const int MaximumMessageCharacters = 8192;
    private const int MaximumSourceCharacters = 256;
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
            limit = new { type = "integer", description = "查詢結果數量限制 (預設 50，範圍 1 到 200)" }
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

        if (action.Equals("query", StringComparison.OrdinalIgnoreCase) &&
            limit is < 1 or > MaximumQueryResults)
        {
            return $"❌ 查詢 limit 必須介於 1 到 {MaximumQueryResults}";
        }

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
            var wasTruncated = message.Length > MaximumMessageCharacters ||
                               source.Length > MaximumSourceCharacters;
            var entry = new LogEntry
            {
                Id = ++_logCounter,
                Timestamp = DateTime.UtcNow.ToString("o"),
                Level = level.ToString(),
                Message = message.Length > MaximumMessageCharacters
                    ? message[..MaximumMessageCharacters]
                    : message,
                Source = source.Length > MaximumSourceCharacters
                    ? source[..MaximumSourceCharacters]
                    : source
            };

            _logEntries.Add(entry);
            if (_logEntries.Count > MaximumStoredEntries)
            {
                _logEntries.RemoveAt(0);
            }

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

            var truncationNotice = wasTruncated ? "，超出長度的內容已截斷" : string.Empty;
            return $"✅ 日誌已記錄 (ID: {entry.Id}, 級別: {entry.Level}){truncationNotice}";
        }
    }

    private static string QueryLogs(string query, int limit)
    {
        lock (_lock)
        {
            const int reportOverhead = 128;
            var details = new StringBuilder(Math.Min(MaximumQueryOutputCharacters, 4096));
            var resultCount = 0;
            var truncated = false;
            for (var index = _logEntries.Count - 1; index >= 0; index--)
            {
                var entry = _logEntries[index];
                if (!string.IsNullOrEmpty(query) &&
                    !entry.Message.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                    !entry.Source.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                    !entry.Level.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (resultCount >= limit)
                {
                    truncated = true;
                    break;
                }

                var detail = $"{resultCount + 1}. [{entry.Id}] {entry.Timestamp} [{entry.Level}] [{entry.Source}]\n    {entry.Message}\n";
                if (details.Length + detail.Length > MaximumQueryOutputCharacters - reportOverhead)
                {
                    truncated = true;
                    break;
                }

                details.Append(detail);
                resultCount++;
            }

            var lines = new List<string>();
            lines.Add("=== 日誌查詢結果 ===");
            lines.Add($"查詢條件: {query}");
            lines.Add($"結果數量: {resultCount}");
            lines.Add("");

            if (resultCount == 0)
            {
                lines.Add("未找到匹配的日誌");
                return string.Join("\n", lines);
            }

            var result = string.Join("\n", lines) + "\n" + details;
            if (truncated)
            {
                result += "查詢結果已達輸出上限，可能還有符合條件的日誌未列出。\n";
            }

            return result;
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
        lock (_lock)
        {
            return _logEntries.Select(CloneLogEntry).ToList();
        }
    }

    public static void ClearAllLogs()
    {
        lock (_lock)
        {
            _logEntries.Clear();
            _logCounter = 0;
        }
    }

    private static LogEntry CloneLogEntry(LogEntry entry)
    {
        return new LogEntry
        {
            Id = entry.Id,
            Timestamp = entry.Timestamp,
            Level = entry.Level,
            Message = entry.Message,
            Source = entry.Source
        };
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
