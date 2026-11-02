using System;
using System.Collections.Generic;
using System.Text;

namespace SuperAgent.Core;

/// <summary>
/// 日誌層級
/// </summary>
public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

/// <summary>
/// ILogger 介面 - 日誌記錄介面
/// </summary>
public interface ILogger
{
    /// <summary>
    /// 記錄日誌訊息
    /// </summary>
    /// <param name="level">日誌層級</param>
    /// <param name="message">日誌訊息</param>
    /// <param name="context">額外上下文資訊</param>
    void Log(LogLevel level, string message, Dictionary<string, string>? context = null);
}

/// <summary>
/// ConsoleLogger - 控制台日誌實作
/// </summary>
public class ConsoleLogger : ILogger
{
    private readonly object _lock = new();

    public void Log(LogLevel level, string message, Dictionary<string, string>? context = null)
    {
        lock (_lock)
        {
            var prefix = level switch
            {
                LogLevel.Debug => "[DEBUG]",
                LogLevel.Info => "[INFO] ",
                LogLevel.Warning => "[WARN] ",
                LogLevel.Error => "[ERROR]",
                _ => "[INFO] "
            };

            Console.WriteLine($"{prefix} {message}");

            if (context != null && context.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (var kvp in context)
                {
                    sb.AppendLine($"  {kvp.Key}: {kvp.Value}");
                }
                Console.WriteLine(sb.ToString());
            }
        }
    }
}

/// <summary>
/// LoggerManager - 日誌管理器，支援多個日誌目標
/// </summary>
public class LoggerManager
{
    private readonly List<ILogger> _loggers = new();

    /// <summary>
    /// 新增日誌目標
    /// </summary>
    public void AddLogger(ILogger logger)
    {
        _loggers.Add(logger);
    }

    /// <summary>
    /// 記錄日誌到所有目標
    /// </summary>
    public void Log(LogLevel level, string message, Dictionary<string, string>? context = null)
    {
        foreach (var logger in _loggers)
        {
            logger.Log(level, message, context);
        }
    }
}