using System;
using System.Collections.Generic;
using System.Text;

namespace SuperAgent.Core;

/// <summary>
/// 錯誤分類列舉
/// </summary>
public enum ErrorClassification
{
    /// <summary>網路錯誤 - 連接問題、超時、DNS 失敗</summary>
    Network,

    /// <summary>API 錯誤 - 認證失敗、配額超限、無效請求</summary>
    Api,

    /// <summary>工具錯誤 - 工具執行失敗、參數錯誤</summary>
    Tool,

    /// <summary>設定錯誤 - 設定缺失、無效值</summary>
    Configuration,

    /// <summary>未知錯誤</summary>
    Unknown
}

/// <summary>
/// ErrorClassifier - 錯誤分類器
/// 根據異常類型與訊息將錯誤分類
/// </summary>
public class ErrorClassifier
{
    /// <summary>
    /// 對例外進行分類
    /// </summary>
    /// <param name="ex">例外</param>
    /// <returns>錯誤分類</returns>
    public static ErrorClassification Classify(Exception ex)
    {
        if (ex is null)
        {
            return ErrorClassification.Unknown;
        }

        var message = ex.Message ?? string.Empty;
        var typeName = ex.GetType().Name;

        // 檢查網路相關錯誤
        if (IsNetworkError(ex, message))
        {
            return ErrorClassification.Network;
        }

        // 檢查 API 相關錯誤
        if (IsApiError(ex, message, typeName))
        {
            return ErrorClassification.Api;
        }

        // 檢查工具相關錯誤
        if (IsToolError(ex, message))
        {
            return ErrorClassification.Tool;
        }

        // 檢查設定相關錯誤
        if (IsConfigurationError(ex, message))
        {
            return ErrorClassification.Configuration;
        }

        return ErrorClassification.Unknown;
    }

    /// <summary>
    /// 取得錯誤的顯示名稱
    /// </summary>
    /// <param name="classification">錯誤分類</param>
    /// <returns>顯示名稱</returns>
    public static string GetDisplayName(ErrorClassification classification)
    {
        return classification switch
        {
            ErrorClassification.Network => "網路錯誤",
            ErrorClassification.Api => "API 錯誤",
            ErrorClassification.Tool => "工具錯誤",
            ErrorClassification.Configuration => "設定錯誤",
            _ => "未知錯誤"
        };
    }

    /// <summary>
    /// 取得錯誤的建議操作
    /// </summary>
    /// <param name="classification">錯誤分類</param>
    /// <returns>建議操作</returns>
    public static string GetSuggestedAction(ErrorClassification classification)
    {
        return classification switch
        {
            ErrorClassification.Network => "檢查網路連接與 API 端點",
            ErrorClassification.Api => "檢查 API 密鑰與配額限制",
            ErrorClassification.Tool => "檢查工具參數與權限",
            ErrorClassification.Configuration => "檢查設定檔案與環境變數",
            _ => "檢查錯誤訊息與日誌"
        };
    }

    private static bool IsNetworkError(Exception ex, string message)
    {
        // 檢查例外類型
        if (ex is System.Net.Sockets.SocketException ||
            ex is System.Net.WebException ||
            ex is System.Net.Http.HttpRequestException ||
            ex is System.TimeoutException ||
            ex is System.Threading.Tasks.TaskCanceledException)
        {
            return true;
        }

        // 檢查訊息關鍵字
        var lowerMessage = message.ToLowerInvariant();
        return lowerMessage.Contains("connection") ||
               lowerMessage.Contains("timeout") ||
               lowerMessage.Contains("network") ||
               lowerMessage.Contains("dns") ||
               lowerMessage.Contains("連接") ||
               lowerMessage.Contains("超時");
    }

    private static bool IsApiError(Exception ex, string message, string typeName)
    {
        // 檢查例外類型
        if (ex is System.IO.IOException)
        {
            return true;
        }

        // 檢查訊息關鍵字
        var lowerMessage = message.ToLowerInvariant();
        return lowerMessage.Contains("401") ||
               lowerMessage.Contains("403") ||
               lowerMessage.Contains("429") ||
               lowerMessage.Contains("500") ||
               lowerMessage.Contains("unauthorized") ||
               lowerMessage.Contains("forbidden") ||
               lowerMessage.Contains("rate limit") ||
               lowerMessage.Contains("認證") ||
               lowerMessage.Contains("配額");
    }

    private static bool IsToolError(Exception ex, string message)
    {
        // 檢查訊息關鍵字
        var lowerMessage = message.ToLowerInvariant();
        return lowerMessage.Contains("tool") ||
               lowerMessage.Contains("engine") ||
               lowerMessage.Contains("file") ||
               lowerMessage.Contains("path") ||
               lowerMessage.Contains("permission") ||
               lowerMessage.Contains("工具") ||
               lowerMessage.Contains("檔案") ||
               lowerMessage.Contains("路徑");
    }

    private static bool IsConfigurationError(Exception ex, string message)
    {
        // 檢查訊息關鍵字
        var lowerMessage = message.ToLowerInvariant();
        return lowerMessage.Contains("config") ||
               lowerMessage.Contains("setting") ||
               lowerMessage.Contains("environment") ||
               lowerMessage.Contains("variable") ||
               lowerMessage.Contains("設定") ||
               lowerMessage.Contains("環境變數");
    }
}

/// <summary>
/// ErrorInfo - 錯誤信息包
/// </summary>
public class ErrorInfo
{
    public ErrorClassification Classification { get; set; }
    public string Message { get; set; } = string.Empty;
    public string DisplayName => ErrorClassifier.GetDisplayName(Classification);
    public string SuggestedAction => ErrorClassifier.GetSuggestedAction(Classification);
    public Exception? InnerException { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, string> Context { get; set; } = new();
}

/// <summary>
/// RetryStrategy 介面 - 重試策略介面
/// </summary>
public interface IRetryStrategy
{
    /// <summary>
    /// 檢查是否應該重試
    /// </summary>
    /// <param name="classification">錯誤分類</param>
    /// <param name="attempt">當前嘗試次數</param>
    /// <returns>是否應該重試</returns>
    bool ShouldRetry(ErrorClassification classification, int attempt);

    /// <summary>
    /// 取得重試延遲時間
    /// </summary>
    /// <param name="attempt">當前嘗試次數</param>
    /// <returns>延遲時間</returns>
    TimeSpan GetDelay(int attempt);
}

/// <summary>
/// NetworkRetryStrategy - 網路錯誤重試策略 (指數退避)
/// </summary>
public class NetworkRetryStrategy : IRetryStrategy
{
    public bool ShouldRetry(ErrorClassification classification, int attempt)
    {
        return classification == ErrorClassification.Network && attempt < 3;
    }

    public TimeSpan GetDelay(int attempt)
    {
        return TimeSpan.FromSeconds(Math.Pow(3, attempt - 1));
    }
}

/// <summary>
/// ApiRetryStrategy - API 錯誤重試策略 (固定間隔)
/// </summary>
public class ApiRetryStrategy : IRetryStrategy
{
    public bool ShouldRetry(ErrorClassification classification, int attempt)
    {
        return classification == ErrorClassification.Api && attempt < 2;
    }

    public TimeSpan GetDelay(int attempt)
    {
        return TimeSpan.FromSeconds(5);
    }
}

/// <summary>
/// ToolRetryStrategy - 工具錯誤重試策略 (不重試)
/// </summary>
public class ToolRetryStrategy : IRetryStrategy
{
    public bool ShouldRetry(ErrorClassification classification, int attempt)
    {
        return false;
    }

    public TimeSpan GetDelay(int attempt)
    {
        return TimeSpan.Zero;
    }
}

/// <summary>
/// ConfigurationRetryStrategy - 設定錯誤重試策略 (不重試)
/// </summary>
public class ConfigurationRetryStrategy : IRetryStrategy
{
    public bool ShouldRetry(ErrorClassification classification, int attempt)
    {
        return false;
    }

    public TimeSpan GetDelay(int attempt)
    {
        return TimeSpan.Zero;
    }
}

/// <summary>
/// RetryStrategyFactory - 重試策略工廠
/// 根據錯誤分類返回適當的重試策略
/// </summary>
public class RetryStrategyFactory
{
    private readonly Dictionary<ErrorClassification, IRetryStrategy> _strategies = new();

    public RetryStrategyFactory()
    {
        _strategies[ErrorClassification.Network] = new NetworkRetryStrategy();
        _strategies[ErrorClassification.Api] = new ApiRetryStrategy();
        _strategies[ErrorClassification.Tool] = new ToolRetryStrategy();
        _strategies[ErrorClassification.Configuration] = new ConfigurationRetryStrategy();
    }

    /// <summary>
    /// 取得錯誤分類的重試策略
    /// </summary>
    /// <param name="classification">錯誤分類</param>
    /// <returns>重試策略</returns>
    public IRetryStrategy GetStrategy(ErrorClassification classification)
    {
        return _strategies.TryGetValue(classification, out var strategy) 
            ? strategy 
            : new ToolRetryStrategy();
    }
}
