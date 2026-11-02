using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace SuperAgent.Core;

/// <summary>
/// 工具路由器 - 根據任務類型自動選擇最適合的工具
/// </summary>
public class ToolRouter
{
    private readonly IToolRegistry _toolRegistry;
    private readonly Dictionary<string, List<string>> _keywordMappings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _toolPriorities = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 建立 ToolRouter
    /// </summary>
    public ToolRouter(IToolRegistry toolRegistry)
    {
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        InitializeDefaultMappings();
    }

    /// <summary>
    /// 新增關鍵字對應關係
    /// </summary>
    /// <param name="toolName">工具名稱</param>
    /// <param name="keywords">關鍵字列表</param>
    public void AddKeywordMapping(string toolName, params string[] keywords)
    {
        if (!_keywordMappings.ContainsKey(toolName))
        {
            _keywordMappings[toolName] = new();
        }

        foreach (var keyword in keywords)
        {
            _keywordMappings[toolName].Add(keyword.ToLowerInvariant());
        }
    }

    /// <summary>
    /// 設定工具優先級
    /// </summary>
    /// <param name="toolName">工具名稱</param>
    /// <param name="priority">優先級 (數值越高優先級越高)</param>
    public void SetToolPriority(string toolName, int priority)
    {
        _toolPriorities[toolName] = priority;
    }

    /// <summary>
    /// 根據任務建議工具
    /// </summary>
    /// <param name="task">任務描述</param>
    /// <returns>建議的工具列表 (按優先級排序)</returns>
    public List<ToolRecommendation> RecommendTools(string task)
    {
        if (string.IsNullOrWhiteSpace(task))
        {
            return new();
        }

        var lowerTask = task.ToLowerInvariant();
        var recommendations = new List<ToolRecommendation>();

        foreach (var kvp in _keywordMappings)
        {
            var toolName = kvp.Key;
            var keywords = kvp.Value;

            // 檢查是否有關鍵字匹配
            var matchedKeywords = keywords.Where(k => lowerTask.Contains(k)).ToList();
            if (matchedKeywords.Count == 0)
            {
                continue;
            }

            // 檢查工具是否可用
            var availableTools = _toolRegistry.GetAllTools();
            var toolDef = availableTools.FirstOrDefault(t => t.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase));
            if (toolDef is null)
            {
                continue;
            }

            var priority = _toolPriorities.TryGetValue(toolName, out var p) ? p : 0;
            var score = matchedKeywords.Count * 10 + priority;

            recommendations.Add(new ToolRecommendation
            {
                ToolName = toolName,
                ToolDescription = toolDef.Description,
                MatchedKeywords = matchedKeywords,
                Score = score,
                Priority = priority
            });
        }

        // 按分數排序
        return recommendations.OrderByDescending(r => r.Score).ThenByDescending(r => r.Priority).ToList();
    }

    /// <summary>
    /// 取得最佳建議的工具
    /// </summary>
    /// <param name="task">任務描述</param>
    /// <returns>最佳建議的工具，或 null 若無建議</returns>
    public ToolRecommendation? RecommendBestTool(string task)
    {
        var recommendations = RecommendTools(task);
        return recommendations.FirstOrDefault();
    }

    /// <summary>
    /// 檢查工具是否可用
    /// </summary>
    /// <param name="toolName">工具名稱</param>
    /// <returns>是否可用</returns>
    public bool IsToolAvailable(string toolName)
    {
        var availableTools = _toolRegistry.GetAllTools();
        return availableTools.Any(t => t.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 初始化預設關鍵字對應關係
    /// </summary>
    private void InitializeDefaultMappings()
    {
        // CodeWriterTool - 寫入、撰寫、程式碼、文件
        AddKeywordMapping("code_writer", "寫入", "撰寫", "程式碼", "文件", "create", "write", "code", "file");

        // CodeReaderTool - 讀取、查看、展示、顯示
        AddKeywordMapping("code_reader", "讀取", "查看", "展示", "顯示", "read", "view", "show", "display");

        // ShellExecTool - 執行、命令、shell、腳本
        AddKeywordMapping("shell_exec", "執行", "命令", "shell", "腳本", "run", "command", "execute", "script");

        // DatabaseTool - 查詢、資料庫、SQL、資料
        AddKeywordMapping("database_query", "查詢", "資料庫", "SQL", "資料", "query", "database", "db", "select", "insert", "update");

        // WebSearchTool - 搜尋、網頁、查詢資訊、爬蟲
        AddKeywordMapping("web_search", "搜尋", "網頁", "查詢資訊", "爬蟲", "search", "web", "browse", "find information");
    }
}

/// <summary>
/// ToolRecommendation - 工具建議資訊
/// </summary>
public class ToolRecommendation
{
    public string ToolName { get; set; } = string.Empty;
    public string ToolDescription { get; set; } = string.Empty;
    public List<string> MatchedKeywords { get; set; } = new();
    public int Score { get; set; }
    public int Priority { get; set; }
}
