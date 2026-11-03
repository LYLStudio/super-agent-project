using Xunit;
using SuperAgent.Core;
using System.Text.Json;
using System.Threading;

namespace SuperAgent.Core.Tests;

public class PerformanceMonitorTests
{
    [Fact]
    public void PerformanceMonitor_RecordsOperation()
    {
        var tool = new PerformanceMonitor();
        var args = JsonSerializer.SerializeToElement(new
        {
            operation = "test_operation",
            iterations = 3
        });

        var result = tool.ExecuteAsync(args, CancellationToken.None).Result;
        Assert.NotNull(result);
        Assert.Contains("效能監控報告", result);
        Assert.Contains("test_operation", result);
        Assert.Contains("執行次數: 3", result);
    }

    [Fact]
    public void PerformanceMonitor_ReportsTimeMetrics()
    {
        var tool = new PerformanceMonitor();
        var args = JsonSerializer.SerializeToElement(new
        {
            operation = "test_op",
            metricType = "time"
        });

        var result = tool.ExecuteAsync(args, CancellationToken.None).Result;
        Assert.Contains("時間效能指標", result);
        Assert.Contains("總執行時間", result);
        Assert.Contains("平均執行時間", result);
    }

    [Fact]
    public void PerformanceMonitor_ReportsMemoryMetrics()
    {
        var tool = new PerformanceMonitor();
        var args = JsonSerializer.SerializeToElement(new
        {
            operation = "test_op",
            metricType = "memory"
        });

        var result = tool.ExecuteAsync(args, CancellationToken.None).Result;
        Assert.Contains("記憶體使用量", result);
        Assert.Contains("目前記憶體使用", result);
    }

    [Fact]
    public void PerformanceMonitor_HandlesEmptyOperation()
    {
        var tool = new PerformanceMonitor();
        var args = JsonSerializer.SerializeToElement(new
        {
            operation = ""
        });

        var result = tool.ExecuteAsync(args, CancellationToken.None).Result;
        Assert.NotNull(result);
        Assert.Contains("效能監控報告", result);
    }

    [Fact]
    public void PerformanceMonitor_ProvidesSuggestions()
    {
        var tool = new PerformanceMonitor();
        var args = JsonSerializer.SerializeToElement(new
        {
            operation = "test_op"
        });

        var result = tool.ExecuteAsync(args, CancellationToken.None).Result;
        Assert.Contains("改善建議", result);
    }

    [Fact]
    public void PerformanceMonitor_GetMetricsReturnsList()
    {
        // Clear any existing metrics
        PerformanceMonitor.ClearMetrics();

        // First call a tool to generate metrics
        var tool = new PerformanceMonitor();
        var args = JsonSerializer.SerializeToElement(new
        {
            operation = "test_op"
        });
        tool.ExecuteAsync(args, CancellationToken.None).Wait();

        var metrics = PerformanceMonitor.GetMetrics();
        Assert.NotNull(metrics);
        Assert.Single(metrics);
    }
}
