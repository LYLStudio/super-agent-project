using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace SuperAgent.Core;

/// <summary>
/// 效能指標類別
/// </summary>
public class PerformanceMetric
{
    public string Name { get; set; } = string.Empty;
    public double DurationMs { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, string> Properties { get; set; } = new();
}

/// <summary>
/// PerformanceMonitor - 效能監控與指標記錄
/// 支援記錄 Agent Loop 執行時間與 API 請求延遲
/// </summary>
public class PerformanceMonitor
{
    private readonly List<PerformanceMetric> _metrics = new();
    private readonly object _lock = new();

    /// <summary>
    /// 記錄效能指標
    /// </summary>
    public void Record(PerformanceMetric metric)
    {
        lock (_lock)
        {
            _metrics.Add(metric);
        }
    }

    /// <summary>
    /// 開始計時
    /// </summary>
    /// <param name="name">指標名稱</param>
    /// <returns>計時器 ID</returns>
    public string Start(string name)
    {
        var id = Guid.NewGuid().ToString();
        var stopwatch = Stopwatch.StartNew();

        lock (_lock)
        {
            _activeTimers[id] = new TimerInfo
            {
                Name = name,
                Stopwatch = stopwatch,
                StartTime = DateTime.UtcNow
            };
        }

        return id;
    }

    /// <summary>
    /// 停止計時並記錄指標
    /// </summary>
    /// <param name="id">計時器 ID</param>
    /// <param name="properties">額外屬性</param>
    public void Stop(string id, Dictionary<string, string>? properties = null)
    {
        TimerInfo? timerInfo;

        lock (_lock)
        {
            if (_activeTimers.TryGetValue(id, out var info))
            {
                timerInfo = info;
                _activeTimers.Remove(id);
            }
            else
            {
                return;
            }
        }

        timerInfo.Stopwatch.Stop();
        var metric = new PerformanceMetric
        {
            Name = timerInfo.Name,
            DurationMs = timerInfo.Stopwatch.ElapsedMilliseconds,
            Timestamp = DateTime.UtcNow,
            Properties = properties ?? new()
        };

        Record(metric);
    }

    /// <summary>
    /// 取得效能摘要
    /// </summary>
    /// <returns>效能摘要字串</returns>
    public string GetSummary()
    {
        lock (_lock)
        {
            if (_metrics.Count == 0)
            {
                return "無效能數據";
            }

            var sb = new StringBuilder();
            sb.AppendLine("=== 效能摘要 ===");
            sb.AppendLine($"總記錄數: {_metrics.Count}");

            var grouped = _metrics.GroupBy(m => m.Name);
            foreach (var group in grouped)
            {
                var durations = group.Select(m => m.DurationMs).ToList();
                var avg = durations.Average();
                var max = durations.Max();
                var min = durations.Min();

                sb.AppendLine($"\n{group.Key}:");
                sb.AppendLine($"  執行次數: {group.Count()}");
                sb.AppendLine($"  平均耗時: {avg:F2} ms");
                sb.AppendLine($"  最小耗時: {min:F2} ms");
                sb.AppendLine($"  最大耗時: {max:F2} ms");
            }

            sb.AppendLine("\n=== 最近記錄 ===");
            foreach (var metric in _metrics.TakeLast(10))
            {
                sb.AppendLine($"{metric.Timestamp:HH:mm:ss} | {metric.Name} | {metric.DurationMs:F2} ms");
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// 清除所有指標
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _metrics.Clear();
        }
    }

    /// <summary>
    /// 取得最近 N 個指標
    /// </summary>
    /// <param name="count">指標數量</param>
    /// <returns>效能指標列表</returns>
    public List<PerformanceMetric> GetRecentMetrics(int count = 10)
    {
        lock (_lock)
        {
            return _metrics.TakeLast(count).ToList();
        }
    }

    private readonly Dictionary<string, TimerInfo> _activeTimers = new();

    private class TimerInfo
    {
        public string Name { get; set; } = string.Empty;
        public Stopwatch Stopwatch { get; set; } = Stopwatch.StartNew();
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
    }
}