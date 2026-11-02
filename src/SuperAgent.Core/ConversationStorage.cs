using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SuperAgent.Core;

/// <summary>
/// 對話會話持續儲存
/// </summary>
public class ConversationStorage
{
    private readonly string _storagePath;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// 建立會話儲存
    /// </summary>
    public ConversationStorage(string storagePath)
    {
        _storagePath = storagePath;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new DateTimeConverter() }
        };

        // 確保儲存目錄存在
        var directory = Path.GetDirectoryName(_storagePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>
    /// 儲存對話會話
    /// </summary>
    public async Task SaveConversationAsync(
        string conversationId,
        List<AgentMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var conversation = new ConversationRecord
        {
            Id = conversationId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Messages = messages
        };

        var json = JsonSerializer.Serialize(conversation, _jsonOptions);
        await File.WriteAllTextAsync(_storagePath, json, cancellationToken);
    }

    /// <summary>
    /// 載入對話會話
    /// </summary>
    public async Task<List<AgentMessage>?> LoadConversationAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_storagePath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_storagePath, cancellationToken);
            var conversation = JsonSerializer.Deserialize<ConversationRecord>(json, _jsonOptions);
            return conversation?.Messages;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[警告] 載入會話失敗: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 列出所有會話檔案
    /// </summary>
    public List<string> ListConversations()
    {
        if (!File.Exists(_storagePath))
        {
            return new List<string>();
        }

        // 這裡可以擴展為支援多個會話檔案
        return new List<string> { _storagePath };
    }

    /// <summary>
    /// 刪除會話
    /// </summary>
    public void DeleteConversation(string conversationId)
    {
        if (File.Exists(_storagePath))
        {
            File.Delete(_storagePath);
        }
    }
}

/// <summary>
/// 會話記錄
/// </summary>
public class ConversationRecord
{
    public string Id { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<AgentMessage> Messages { get; set; } = new();
}

/// <summary>
/// 日期時間轉換器
/// </summary>
public class DateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString() ?? string.Empty;
        return DateTime.Parse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None);
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString("o", System.Globalization.CultureInfo.InvariantCulture));
    }
}