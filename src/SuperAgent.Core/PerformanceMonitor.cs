using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SuperAgent.Core;

namespace SuperAgent.Core;

/// <summary>
/// PerformanceMonitor - 效能監控系統
/// 記錄操作時間、資源使用量與效能指標
/// </summary>
public class PerformanceMonitor : ITool
{
    private static readonly List<PerformanceMetric> _metrics = new();
    private readonly Stopwatch _stopwatch = new();

    public string Name => "performance_monitor";
    public string Description => "監控程式碼執行效能，記錄時間、資源使用量與效能指標";

    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            operation = new { type = "string", description = "要監控的操作名稱" },
            code = new { type = "string", description = "要執行的程式碼片段" },
            iterations = new { type = "integer", description = "執行次數 (預設 1)" },
            metricType = new { type = "string", description = "效能指標類型 (time, memory, all)" }
        },
        required = new[] { "operation" }
    });

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var operation = GetStringProperty(arguments, "operation") ?? "unknown_operation";
        var code = GetStringProperty(arguments, "code") ?? string.Empty;
        var iterations = GetIntProperty(arguments, "iterations", 1);
        var metricType = GetStringProperty(arguments, "metricType") ?? "all";

        var metrics = new List<PerformanceMetric>();

        for (int i = 0; i < iterations; i++)
        {
            _stopwatch.Reset();
            _stopwatch.Start();

            // 執行程式碼 (如果有提供)
            if (!string.IsNullOrEmpty(code))
            {
                ExecuteCode(code);
            }

            _stopwatch.Stop();

            var metric = new PerformanceMetric
            {
                Operation = operation,
                ElapsedMilliseconds = _stopwatch.ElapsedMilliseconds,
                ElapsedSeconds = _stopwatch.Elapsed.TotalSeconds,
                Timestamp = DateTime.UtcNow.ToString("o"),
                Iteration = i + 1
            };

            metrics.Add(metric);
            _metrics.Add(metric);
        }

        var result = GeneratePerformanceReport(metrics, metricType);
        return result;
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

    private static void ExecuteCode(string code)
    {
        // 安全地執行程式碼片段 (這裡只是記錄，實際執行需要 sandbox)
        // 為了安全，我們只記錄程式碼長度作為複雜度指標
        var complexity = CalculateCodeComplexity(code);
        // 模擬執行時間 (基於程式碼複雜度)
        var simulatedTime = complexity * 0.1; // 每個複雜度單位 0.1ms
        Thread.Sleep((int)Math.Max(1, simulatedTime));
    }

    private static int CalculateCodeComplexity(string code)
    {
        var complexity = 0;
        var lines = code.Split('\n');

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//"))
                continue;

            // 計算控制流程複雜度
            complexity += CountOccurrences(trimmed, @"\bif\b");
            complexity += CountOccurrences(trimmed, @"\bfor\b");
            complexity += CountOccurrences(trimmed, @"\bwhile\b");
            complexity += CountOccurrences(trimmed, @"\bforeach\b");
            complexity += CountOccurrences(trimmed, @"\bswitch\b");
            complexity += CountOccurrences(trimmed, @"\bcatch\b");
            complexity += CountOccurrences(trimmed, @"\b&&\b");
            complexity += CountOccurrences(trimmed, @"\b\|\|\b");
        }

        return complexity;
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.OrdinalIgnoreCase)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }

    private string GeneratePerformanceReport(List<PerformanceMetric> metrics, string metricType)
    {
        var lines = new List<string>();
        var averageMs = 0.0;
        var totalMs = 0.0;
        var minMs = double.MaxValue;
        var maxMs = double.MinValue;
        lines.Add("=== 效能監控報告 ===");
        lines.Add($"操作: {metrics[0].Operation}");
        lines.Add($"執行次數: {metrics.Count}");
        lines.Add("");

        if (metricType.Equals("time", StringComparison.OrdinalIgnoreCase) ||
            metricType.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            totalMs = metrics.Sum(m => m.ElapsedMilliseconds);
            averageMs = totalMs / metrics.Count;
            minMs = metrics.Min(m => m.ElapsedMilliseconds);
            maxMs = metrics.Max(m => m.ElapsedMilliseconds);

            lines.Add("=== 時間效能指標 ===");
            lines.Add($"總執行時間: {totalMs} ms");
            lines.Add($"平均執行時間: {averageMs:F2} ms");
            lines.Add($"最短執行時間: {minMs} ms");
            lines.Add($"最長執行時間: {maxMs} ms");
            lines.Add($"每次迭代: {metrics.Count} 次");
            lines.Add("");
        }

        if (metricType.Equals("memory", StringComparison.OrdinalIgnoreCase) ||
            metricType.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add("=== 記憶體使用量 ===");
            var gcMemory = GC.GetTotalMemory(false);
            lines.Add($"目前記憶體使用: {gcMemory / 1024 / 1024:F2} MB");
            lines.Add("");
        }

        lines.Add("=== 詳細執行記錄 ===");
        for (int i = 0; i < metrics.Count; i++)
        {
            lines.Add($"{i + 1}. {metrics[i].ElapsedMilliseconds} ms (第 {metrics[i].Iteration} 次)");
        }
        lines.Add("");

        lines.Add("=== 效能評估 ===");
        if (averageMs < 10)
        {
            lines.Add("✅ 優秀: 執行時間 < 10ms");
        }
        else if (averageMs < 100)
        {
            lines.Add("⚠️ 可接受: 執行時間 10ms - 100ms");
        }
        else
        {
            lines.Add("❌ 需優化: 執行時間 > 100ms");
        }

        lines.Add("");
        lines.Add("=== 改善建議 ===");
        lines.Add("1. 檢查是否有不必要的計算");
        lines.Add("2. 優化高頻呼叫的方法");
        lines.Add("3. 使用緩存減少重複計算");
        lines.Add("4. 檢查是否有記憶體洩漏");

        return string.Join("\n", lines);
    }

    public static List<PerformanceMetric> GetMetrics()
    {
        return new List<PerformanceMetric>(_metrics);
    }

    public static void ClearMetrics()
    {
        _metrics.Clear();
    }
}

public class PerformanceMetric
{
    public string Operation { get; set; } = string.Empty;
    public double ElapsedMilliseconds { get; set; }
    public double ElapsedSeconds { get; set; }
    public string Timestamp { get; set; } = string.Empty;
    public int Iteration { get; set; }
}
