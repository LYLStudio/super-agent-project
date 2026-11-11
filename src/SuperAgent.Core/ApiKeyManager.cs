using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SuperAgent.Core;

/// <summary>
/// API 密鑰管理系統
/// 支援密鑰加密儲存、輪替與有效性檢查，包括環境變數加密
/// </summary>
public class ApiKeyManager
{
    private readonly List<ApiKeyInfo> _keys = new();
    private readonly string _encryptionKey;
    private readonly object _lock = new();

    /// <summary>
    /// API 密鑰資訊
    /// </summary>
    public class ApiKeyInfo
    {
        public string Name { get; set; } = string.Empty;
        public string EncryptedValue { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastUsedAt { get; set; }
        public int UsageCount { get; set; }
        public bool IsActive { get; set; } = true;
        public string? EnvironmentVariable { get; set; }
    }

    /// <summary>
    /// 建立 ApiKeyManager
    /// </summary>
    public ApiKeyManager(string encryptionKey)
    {
        _encryptionKey = encryptionKey ?? GenerateDefaultEncryptionKey();
    }

    /// <summary>
    /// 新增 API 密鑰
    /// </summary>
    /// <param name="name">密鑰名稱</param>
    /// <param name="value">密鑰值</param>
    /// <param name="environmentVariable">環境變數名稱 (可選)</param>
    public void AddKey(string name, string value, string? environmentVariable = null)
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("密鑰名稱不能為空", nameof(name));
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("密鑰值不能為空", nameof(value));
            }

            var encryptedValue = Encrypt(value);

            _keys.Add(new ApiKeyInfo
            {
                Name = name,
                EncryptedValue = encryptedValue,
                EnvironmentVariable = environmentVariable,
                IsActive = true
            });
        }
    }

    /// <summary>
    /// 從環境變數載入密鑰
    /// </summary>
    /// <param name="environmentVariable">環境變數名稱</param>
    /// <param name="keyName">密鑰名稱 (可選，預設為環境變數名稱)</param>
    public void LoadFromEnvironment(string environmentVariable, string? keyName = null)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"環境變數 '{environmentVariable}' 未設定", nameof(environmentVariable));
        }

        var name = keyName ?? environmentVariable;
        AddKey(name, value, environmentVariable);
    }

    /// <summary>
    /// 從設定載入密鑰
    /// </summary>
    /// <param name="config">設定字典</param>
    public void LoadFromConfig(Dictionary<string, string> config)
    {
        lock (_lock)
        {
            foreach (var kvp in config)
            {
                if (kvp.Key.StartsWith("ApiKey:", StringComparison.OrdinalIgnoreCase))
                {
                    var name = kvp.Key.Substring("ApiKey:".Length);
                    AddKey(name, kvp.Value);
                }
            }
        }
    }

    /// <summary>
    /// 取得當前活用密鑰 (輪替策略)
    /// </summary>
    /// <param name="name">密鑰名稱 (可選，預設為第一個活用密鑰)</param>
    /// <returns>解密後的密鑰值，或 null 若無密鑰</returns>
    public string? GetCurrentKey(string? name = null)
    {
        var keyInfo = FindKey(name);

        if (keyInfo is null || !keyInfo.IsActive)
        {
            return null;
        }

        // 更新使用統計
        keyInfo.LastUsedAt = DateTime.UtcNow;
        keyInfo.UsageCount++;

        return Decrypt(keyInfo.EncryptedValue);
    }

    /// <summary>
    /// 輪換密鑰 (切換到下一個活用密鑰)
    /// </summary>
    /// <param name="name">密鑰名稱</param>
    /// <returns>新的當前密鑰，或 null 若無更多密鑰</returns>
    public string? RotateKey(string? name = null)
    {
        var keyInfo = FindKey(name);

        if (keyInfo is null || !keyInfo.IsActive)
        {
            return null;
        }

        // 如果有多個密鑰，切換到下一個
        var currentIndex = _keys.FindIndex(k => k.Name.Equals(keyInfo.Name, StringComparison.OrdinalIgnoreCase));
        if (currentIndex >= 0 && currentIndex < _keys.Count - 1)
        {
            var nextKey = _keys[currentIndex + 1];
            if (nextKey.IsActive)
            {
                // 停用當前密鑰，啟用下一個
                keyInfo.IsActive = false;
                nextKey.IsActive = true;
                return Decrypt(nextKey.EncryptedValue);
            }
        }

        return Decrypt(keyInfo.EncryptedValue);
    }

    /// <summary>
    /// 取得所有密鑰資訊 (不包含加密值)
    /// </summary>
    /// <returns>密鑰資訊列表</returns>
    public List<ApiKeyInfo> GetAllKeys()
    {
        return _keys.Select(k => new ApiKeyInfo
        {
            Name = k.Name,
            CreatedAt = k.CreatedAt,
            LastUsedAt = k.LastUsedAt,
            UsageCount = k.UsageCount,
            IsActive = k.IsActive,
            EnvironmentVariable = k.EnvironmentVariable
        }).ToList();
    }

    /// <summary>
    /// 檢查密鑰是否有效
    /// </summary>
    /// <param name="name">密鑰名稱</param>
    /// <returns>是否有效</returns>
    public bool IsKeyValid(string name)
    {
        var keyInfo = FindKey(name);
        return keyInfo is not null && keyInfo.IsActive && !string.IsNullOrEmpty(keyInfo.EncryptedValue);
    }

    /// <summary>
    /// 取得密鑰數量
    /// </summary>
    public int KeyCount => _keys.Count;

    /// <summary>
    /// 取得活用密鑰數量
    /// </summary>
    public int ActiveKeyCount => _keys.Count(k => k.IsActive);

    private ApiKeyInfo? FindKey(string? name = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return _keys.FirstOrDefault(k => k.IsActive);
        }

        return _keys.FirstOrDefault(k => k.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && k.IsActive);
    }

    private string Encrypt(string plainText)
    {
        using var aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes(_encryptionKey.PadRight(32, '0').Substring(0, 32));
        aes.IV = GenerateRandomIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // 組合 IV + 加密內容
        var combined = new byte[encryptedBytes.Length + 16];
        Buffer.BlockCopy(aes.IV, 0, combined, 0, 16);
        Buffer.BlockCopy(encryptedBytes, 0, combined, 16, encryptedBytes.Length);

        return Convert.ToBase64String(combined);
    }

    private string Decrypt(string encryptedText)
    {
        var combined = Convert.FromBase64String(encryptedText);
        var iv = new byte[16];
        Array.Copy(combined, iv, 16);

        using var aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes(_encryptionKey.PadRight(32, '0').Substring(0, 32));
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        var decryptedBytes = decryptor.TransformFinalBlock(combined, 16, combined.Length - 16);

        return Encoding.UTF8.GetString(decryptedBytes);
    }

    private byte[] GenerateRandomIV()
    {
        using var rng = RandomNumberGenerator.Create();
        var iv = new byte[16];
        rng.GetBytes(iv);
        return iv;
    }

    private string GenerateDefaultEncryptionKey()
    {
        // 生成默認加密密鑰 (在生產環境中應從安全位置載入)
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    /// <summary>
    /// 加密環境變數值並儲存
    /// 支援環境變數加密，防止敏感資訊明文暴露
    /// </summary>
    /// <param name="environmentVariable">環境變數名稱</param>
    /// <param name="value">要加密的值</param>
    /// <returns>加密後的值</returns>
    public string EncryptEnvironmentVariable(string environmentVariable, string value)
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(environmentVariable))
            {
                throw new ArgumentException("環境變數名稱不能為空", nameof(environmentVariable));
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("環境變數值不能為空", nameof(value));
            }

            var encryptedValue = Encrypt(value);
            return encryptedValue;
        }
    }

    /// <summary>
    /// 解密環境變數值
    /// </summary>
    /// <param name="environmentVariable">環境變數名稱</param>
    /// <param name="encryptedValue">加密後的值</param>
    /// <returns>解密後的值</returns>
    public string DecryptEnvironmentVariable(string environmentVariable, string encryptedValue)
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(environmentVariable))
            {
                throw new ArgumentException("環境變數名稱不能為空", nameof(environmentVariable));
            }

            if (string.IsNullOrWhiteSpace(encryptedValue))
            {
                throw new ArgumentException("加密值不能為空", nameof(encryptedValue));
            }

            return Decrypt(encryptedValue);
        }
    }

    /// <summary>
    /// 從環境變數載入並加密儲存密鑰
    /// 讀取環境變數中的敏感值，加密後儲存到 ApiKeyManager 中
    /// </summary>
    /// <param name="environmentVariable">環境變數名稱</param>
    /// <param name="keyName">密鑰名稱 (可選，預設為環境變數名稱)</param>
    public void LoadFromEnvironmentEncrypted(string environmentVariable, string? keyName = null)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"環境變數 '{environmentVariable}' 未設定", nameof(environmentVariable));
        }

        var name = keyName ?? environmentVariable;
        AddKey(name, value, environmentVariable);

        // 記錄環境變數來源，以便後續從環境變數同步
        lock (_lock)
        {
            var keyInfo = _keys.LastOrDefault(k => k.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (keyInfo != null)
            {
                keyInfo.EnvironmentVariable = environmentVariable;
            }
        }
    }

    /// <summary>
    /// 同步所有已註冊環境變數的密鑰
    /// 從環境變數重新載入並更新密鑰值，支援動態輪替
    /// </summary>
    public void SyncEnvironmentKeys()
    {
        lock (_lock)
        {
            var envKeys = _keys.Where(k => !string.IsNullOrEmpty(k.EnvironmentVariable)).ToList();

            foreach (var keyInfo in envKeys)
            {
                var envValue = Environment.GetEnvironmentVariable(keyInfo.EnvironmentVariable!);
                if (!string.IsNullOrWhiteSpace(envValue))
                {
                    // 如果環境變數值有變化，更新密鑰
                    keyInfo.EncryptedValue = Encrypt(envValue);
                    keyInfo.LastUsedAt = DateTime.UtcNow;
                }
            }
        }
    }

    /// <summary>
    /// 取得所有需要從環境變數同步的密鑰
    /// </summary>
    /// <returns>環境變數密鑰列表</returns>
    public List<ApiKeyInfo> GetEnvironmentKeys()
    {
        lock (_lock)
        {
            return _keys.Where(k => !string.IsNullOrEmpty(k.EnvironmentVariable)).ToList();
        }
    }
}
