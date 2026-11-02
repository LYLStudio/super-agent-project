using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;

namespace SuperAgent.Core;

/// <summary>
/// ConfigurationManager - 完整設定管理系統
/// 支援多環境設定檔案與熱重載
/// </summary>
public class ConfigurationManager
{
    private readonly IConfiguration _configuration;
    private readonly string _basePath;
    private readonly string _environment;

    /// <summary>
    /// 建立 ConfigurationManager
    /// </summary>
    public ConfigurationManager(string basePath, string environment = "Development")
    {
        _basePath = basePath;
        _environment = environment;

        var builder = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile($"appsettings.{_environment}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables();

        _configuration = builder.Build();
    }

    /// <summary>
    /// 取得設定值
    /// </summary>
    /// <param name="key">設定鍵名</param>
    /// <param name="defaultValue">預設值</param>
    /// <returns>設定值或預設值</returns>
    public string Get(string key, string? defaultValue = null)
    {
        return _configuration[key] ?? defaultValue ?? string.Empty;
    }

    /// <summary>
    /// 取得字串設定值 (別名)
    /// </summary>
    /// <param name="key">設定鍵名</param>
    /// <param name="defaultValue">預設值</param>
    /// <returns>設定值或預設值</returns>
    public string GetString(string key, string? defaultValue = null)
    {
        return Get(key, defaultValue);
    }

    /// <summary>
    /// 取得整數設定值
    /// </summary>
    /// <param name="key">設定鍵名</param>
    /// <param name="defaultValue">預設值</param>
    /// <returns>設定值或預設值</returns>
    public int GetInt(string key, int defaultValue = 0)
    {
        var value = _configuration[key];
        if (int.TryParse(value, out var result))
        {
            return result;
        }
        return defaultValue;
    }

    /// <summary>
    /// 取得布林設定值
    /// </summary>
    /// <param name="key">設定鍵名</param>
    /// <param name="defaultValue">預設值</param>
    /// <returns>設定值或預設值</returns>
    public bool GetBool(string key, bool defaultValue = false)
    {
        var value = _configuration[key];
        if (bool.TryParse(value, out var result))
        {
            return result;
        }
        return defaultValue;
    }

    /// <summary>
    /// 取得所有設定鍵
    /// </summary>
    /// <returns>設定鍵與值的字典</returns>
    public Dictionary<string, string> GetAllSettings()
    {
        var settings = new Dictionary<string, string>();
        foreach (var item in _configuration.AsEnumerable())
        {
            settings[item.Key] = item.Value ?? string.Empty;
        }
        return settings;
    }

    /// <summary>
    /// 重新載入設定 (熱重載)
    /// </summary>
    public void Reload()
    {
        // 注意: IConfiguration 在 .NET 10 中沒有 Refresh 方法
        // reloadOnChange 已自動處理，此處留為未來擴展
    }

    /// <summary>
    /// 檢查設定是否存在
    /// </summary>
    /// <param name="key">設定鍵名</param>
    /// <returns>是否存在</returns>
    public bool ContainsKey(string key)
    {
        return _configuration[key] != null;
    }

    /// <summary>
    /// 獲取目前環境
    /// </summary>
    public string Environment => _environment;
}