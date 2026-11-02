using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SuperAgent.Core;

/// <summary>
/// 重試策略 - 指數退避重試
/// </summary>
public class RetryPolicy
{
    private readonly int _maxRetries;
    private readonly TimeSpan _initialDelay;
    private readonly TimeSpan _maxDelay;
    private readonly double _backoffMultiplier;

    /// <summary>
    /// 建立重試策略
    /// </summary>
    public RetryPolicy(int maxRetries = 3, TimeSpan? initialDelay = null, TimeSpan? maxDelay = null, double backoffMultiplier = 2.0)
    {
        _maxRetries = maxRetries;
        _initialDelay = initialDelay ?? TimeSpan.FromSeconds(1);
        _maxDelay = maxDelay ?? TimeSpan.FromSeconds(30);
        _backoffMultiplier = backoffMultiplier;
    }

    /// <summary>
    /// 執行帶重試的操作
    /// </summary>
    public async Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        string operationName,
        CancellationToken cancellationToken)
    {
        var lastException = default(Exception);
        var currentDelay = _initialDelay;

        for (int attempt = 0; attempt <= _maxRetries; attempt++)
        {
            try
            {
                return await operation(cancellationToken);
            }
            catch (Exception ex)
            {
                lastException = ex;

                if (attempt < _maxRetries)
                {
                    // 記錄重試資訊
                    Console.WriteLine($"[重試] {operationName} 第 {attempt + 1} 次重試: {ex.Message}");
                    
                    // 等待指數退避時間
                    await Task.Delay(currentDelay, cancellationToken);
                    
                    // 計算下次延遲 (指數退避)
                    currentDelay = TimeSpan.FromMilliseconds(
                        Math.Min(currentDelay.TotalMilliseconds * _backoffMultiplier, _maxDelay.TotalMilliseconds));
                }
            }
        }

        // 所有重試都失敗，拋出最後一個例外
        throw new AggregateException($"操作 '{operationName}' 在 {_maxRetries} 次重試後失敗", lastException ?? new Exception("Unknown error"));
    }

    /// <summary>
    /// 執行帶重試的操作 (不返回結果)
    /// </summary>
    public async Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        string operationName,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync<object>(async (ct) =>
        {
            await operation(ct);
            return null!;
        }, operationName, cancellationToken);
    }
}

/// <summary>
/// 重試結果
/// </summary>
public class RetryResult
{
    public bool Success { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public TimeSpan TotalElapsed { get; set; }
}