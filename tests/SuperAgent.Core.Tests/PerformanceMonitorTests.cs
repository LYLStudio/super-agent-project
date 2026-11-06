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
        Assert.Contains("分析次數: 3", result);
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
        Assert.Contains("分析耗時指標", result);
        Assert.Contains("總分析時間", result);
        Assert.Contains("平均分析時間", result);
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
        Assert.Contains("目前程序記憶體", result);
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
    public void PerformanceMonitor_ReportsMeasurementScope()
    {
        var tool = new PerformanceMonitor();
        var args = JsonSerializer.SerializeToElement(new
        {
            operation = "test_op"
        });

        var result = tool.ExecuteAsync(args, CancellationToken.None).Result;
        Assert.Contains("不代表輸入程式碼的實際執行效能", result);
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

    [Fact]
    public async Task PerformanceMonitor_ClarifiesThatCodeIsNotExecuted()
    {
        var tool = new PerformanceMonitor();
        var args = JsonSerializer.SerializeToElement(new
        {
            operation = "complexity",
            code = "if (ready && active) { Run(); }"
        });

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.Contains("不代表輸入程式碼的實際執行效能", result);
        Assert.Contains("複雜度分數: 2", result);
    }

    [Fact]
    public async Task PerformanceMonitor_RejectsUnboundedIterationCounts()
    {
        var tool = new PerformanceMonitor();
        var args = JsonSerializer.SerializeToElement(new
        {
            operation = "invalid",
            iterations = 0
        });

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.Contains("iterations 必須介於", result);
    }

    [Fact]
    public async Task PerformanceMonitor_RetainsOnlyRecentMetrics()
    {
        PerformanceMonitor.ClearMetrics();
        var tool = new PerformanceMonitor();
        var args = JsonSerializer.SerializeToElement(new
        {
            operation = "bounded",
            iterations = 100
        });

        for (var index = 0; index < 11; index++)
        {
            await tool.ExecuteAsync(args, CancellationToken.None);
        }

        Assert.Equal(1000, PerformanceMonitor.GetMetrics().Count);
    }
}
