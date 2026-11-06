using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SuperAgent.Core;

/// <summary>
/// PerformanceMonitor - 分析程式碼複雜度並量測分析操作
/// </summary>
public class PerformanceMonitor : ITool
{
    private const int MaximumIterations = 100;
    private const int MaximumStoredMetrics = 1000;
    private const int MaximumCodeCharacters = 1_048_576;
    private const int MaximumOperationCharacters = 256;
    private static readonly object MetricsLock = new();
    private static readonly Queue<PerformanceMetric> Metrics = new();
    private static readonly Regex ComplexityPattern = new(
        @"\b(if|for|while|foreach|switch|catch)\b|&&|\|\|",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string Name => "performance_monitor";
    public string Description => "分析程式碼複雜度並量測分析耗時；不會執行輸入的程式碼。";

    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            operation = new { type = "string", description = "要記錄的操作名稱" },
            code = new { type = "string", description = "要分析複雜度的程式碼片段，不會執行" },
            iterations = new { type = "integer", description = "分析次數 (範圍 1 到 100，預設 1)" },
            metricType = new { type = "string", description = "效能指標類型 (time, memory, all)" }
        },
        required = new[] { "operation" }
    });

    public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var operation = GetStringProperty(arguments, "operation") ?? "unknown_operation";
        var code = GetStringProperty(arguments, "code") ?? string.Empty;
        var iterations = GetIntProperty(arguments, "iterations", 1);
        var metricType = GetStringProperty(arguments, "metricType") ?? "all";

        if (iterations is < 1 or > MaximumIterations)
        {
            return Task.FromResult($"Error: iterations 必須介於 1 到 {MaximumIterations}");
        }

        if (code.Length > MaximumCodeCharacters)
        {
            return Task.FromResult($"Error: code 不可超過 {MaximumCodeCharacters} 個字元");
        }

        if (operation.Length > MaximumOperationCharacters)
        {
            return Task.FromResult($"Error: operation 不可超過 {MaximumOperationCharacters} 個字元");
        }

        if (!metricType.Equals("time", StringComparison.OrdinalIgnoreCase) &&
            !metricType.Equals("memory", StringComparison.OrdinalIgnoreCase) &&
            !metricType.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult("Error: metricType 必須是 time、memory 或 all");
        }

        var metrics = new List<PerformanceMetric>(iterations);
        for (var iteration = 1; iteration <= iterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stopwatch = Stopwatch.StartNew();
            var complexity = CalculateCodeComplexity(code);
            stopwatch.Stop();

            var metric = new PerformanceMetric
            {
                Operation = operation,
                ElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
                ElapsedSeconds = stopwatch.Elapsed.TotalSeconds,
                Timestamp = DateTime.UtcNow.ToString("o"),
                Iteration = iteration,
                ComplexityScore = complexity
            };
            metrics.Add(metric);

            lock (MetricsLock)
            {
                Metrics.Enqueue(metric);
                while (Metrics.Count > MaximumStoredMetrics)
                {
                    Metrics.Dequeue();
                }
            }
        }

        return Task.FromResult(GeneratePerformanceReport(metrics, metricType));
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static int GetIntProperty(JsonElement element, string propertyName, int defaultValue)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.TryGetInt32(out var value)
            ? value
            : defaultValue;
    }

    private static int CalculateCodeComplexity(string code)
    {
        var complexity = 0;
        foreach (Match match in ComplexityPattern.Matches(code))
        {
            complexity++;
        }

        return complexity;
    }

    private static string GeneratePerformanceReport(List<PerformanceMetric> metrics, string metricType)
    {
        var lines = new List<string>
        {
            "=== 效能監控報告 ===",
            $"操作: {metrics[0].Operation}",
            $"分析次數: {metrics.Count}",
            $"複雜度分數: {metrics[0].ComplexityScore}",
            "注意: 時間指標量測的是複雜度分析操作，不代表輸入程式碼的實際執行效能。",
            ""
        };

        if (metricType.Equals("time", StringComparison.OrdinalIgnoreCase) ||
            metricType.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var totalMs = 0d;
            var minMs = double.MaxValue;
            var maxMs = double.MinValue;
            foreach (var metric in metrics)
            {
                totalMs += metric.ElapsedMilliseconds;
                minMs = Math.Min(minMs, metric.ElapsedMilliseconds);
                maxMs = Math.Max(maxMs, metric.ElapsedMilliseconds);
            }

            lines.Add("=== 分析耗時指標 ===");
            lines.Add($"總分析時間: {totalMs:F3} ms");
            lines.Add($"平均分析時間: {totalMs / metrics.Count:F3} ms");
            lines.Add($"最短分析時間: {minMs:F3} ms");
            lines.Add($"最長分析時間: {maxMs:F3} ms");
            lines.Add("");
        }

        if (metricType.Equals("memory", StringComparison.OrdinalIgnoreCase) ||
            metricType.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add("=== 記憶體使用量 ===");
            lines.Add($"目前程序記憶體: {GC.GetTotalMemory(false) / 1024d / 1024d:F2} MB");
            lines.Add("");
        }

        lines.Add("=== 詳細分析記錄 ===");
        for (var index = 0; index < metrics.Count; index++)
        {
            lines.Add($"{index + 1}. {metrics[index].ElapsedMilliseconds:F3} ms (複雜度 {metrics[index].ComplexityScore})");
        }

        return string.Join("\n", lines);
    }

    public static List<PerformanceMetric> GetMetrics()
    {
        lock (MetricsLock)
        {
            return new List<PerformanceMetric>(Metrics);
        }
    }

    public static void ClearMetrics()
    {
        lock (MetricsLock)
        {
            Metrics.Clear();
        }
    }
}

public class PerformanceMetric
{
    public string Operation { get; set; } = string.Empty;
    public double ElapsedMilliseconds { get; set; }
    public double ElapsedSeconds { get; set; }
    public string Timestamp { get; set; } = string.Empty;
    public int Iteration { get; set; }
    public int ComplexityScore { get; set; }
}
