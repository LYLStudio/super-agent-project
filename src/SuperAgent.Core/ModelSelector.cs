using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace SuperAgent.Core;

/// <summary>
/// 模型資訊
/// </summary>
public class ModelInfo
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public int MaxTokens { get; set; } = 4096;
    public double DefaultTemperature { get; set; } = 0.7;
    public double DefaultTopP { get; set; } = 1.0;
    public bool IsActive { get; set; } = true;
    public double Priority { get; set; } = 0;
    public List<string> SuitableTasks { get; set; } = new();

    public ModelInfo Clone()
    {
        return new ModelInfo
        {
            Name = Name,
            Description = Description,
            Provider = Provider,
            MaxTokens = MaxTokens,
            DefaultTemperature = DefaultTemperature,
            DefaultTopP = DefaultTopP,
            IsActive = IsActive,
            Priority = Priority,
            SuitableTasks = new List<string>(SuitableTasks)
        };
    }
}

/// <summary>
/// ModelSelector - 多模型自動選擇系統
/// 支援根據任務類型自動選擇最適合的模型
/// </summary>
public class ModelSelector
{
    private readonly List<ModelInfo> _models = new();
    private readonly Dictionary<string, List<string>> _taskModelMappings = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 建立 ModelSelector
    /// </summary>
    public ModelSelector()
    {
        InitializeDefaultModels();
        InitializeTaskMappings();
    }

    /// <summary>
    /// 新增模型
    /// </summary>
    /// <param name="model">模型資訊</param>
    public void AddModel(ModelInfo model)
    {
        if (string.IsNullOrWhiteSpace(model.Name))
        {
            throw new ArgumentException("模型名稱不能為空", nameof(model.Name));
        }

        // 移除舊同名模型
        _models.RemoveAll(m => m.Name.Equals(model.Name, StringComparison.OrdinalIgnoreCase));
        _models.Add(model);
    }

    /// <summary>
    /// 移除模型
    /// </summary>
    /// <param name="name">模型名稱</param>
    /// <returns>是否成功移除</returns>
    public bool RemoveModel(string name)
    {
        var model = _models.FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (model is null)
        {
            return false;
        }

        return _models.Remove(model);
    }

    /// <summary>
    /// 取得所有活用模型
    /// </summary>
    /// <returns>模型列表</returns>
    public List<ModelInfo> GetAllModels()
    {
        return _models.Where(m => m.IsActive).ToList();
    }

    /// <summary>
    /// 取得模型資訊
    /// </summary>
    /// <param name="name">模型名稱</param>
    /// <returns>模型資訊，或 null 若未找到</returns>
    public ModelInfo? GetModel(string name)
    {
        return _models.FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 根據任務選擇最適合的模型
    /// </summary>
    /// <param name="task">任務描述</param>
    /// <returns>推薦的模型名稱</returns>
    public string SelectModelForTask(string task)
    {
        if (string.IsNullOrWhiteSpace(task))
        {
            return GetDefaultModel() ?? string.Empty;
        }

        var lowerTask = task.ToLowerInvariant();

        // 檢查任務類型對應的模型
        var matchedModels = new List<(ModelInfo Model, int Score)>();

        foreach (var kvp in _taskModelMappings)
        {
            var taskType = kvp.Key;
            var modelNames = kvp.Value;

            // 檢查任務是否匹配此類型
            if (IsTaskMatchingType(task, taskType))
            {
                foreach (var modelName in modelNames)
                {
                    var model = GetModel(modelName);
                    if (model is { IsActive: true })
                    {
                        var score = CalculateTaskMatchScore(task, model, taskType);
                        matchedModels.Add((model, score));
                    }
                }
            }
        }

        // 如果沒有匹配，使用優先級最高的模型
        if (matchedModels.Count == 0)
        {
            var bestModel = _models
                .Where(m => m.IsActive)
                .OrderByDescending(m => m.Priority)
                .FirstOrDefault();

            return bestModel?.Name ?? string.Empty;
        }

        // 返回分數最高的模型
        return matchedModels
            .OrderByDescending(m => m.Score)
            .ThenByDescending(m => m.Model.Priority)
            .First()
            .Model.Name;
    }

    /// <summary>
    /// 取得預設模型
    /// </summary>
    /// <returns>預設模型名稱</returns>
    public string GetDefaultModel()
    {
        var defaultModel = _models
            .Where(m => m.IsActive)
            .OrderByDescending(m => m.Priority)
            .FirstOrDefault();

        return defaultModel?.Name ?? string.Empty;
    }

    /// <summary>
    /// 設定模型參數
    /// </summary>
    /// <param name="name">模型名稱</param>
    /// <param name="temperature">溫度參數</param>
    /// <param name="maxTokens">最大記號數</param>
    /// <param name="topP">Top P 參數</param>
    /// <returns>是否成功設定</returns>
    public bool SetModelParameters(string name, double? temperature = null, int? maxTokens = null, double? topP = null)
    {
        var model = GetModel(name);
        if (model is null)
        {
            return false;
        }

        if (temperature.HasValue)
        {
            model.DefaultTemperature = Math.Clamp(temperature.Value, 0.0, 2.0);
        }

        if (maxTokens.HasValue)
        {
            model.MaxTokens = Math.Max(1, maxTokens.Value);
        }

        if (topP.HasValue)
        {
            model.DefaultTopP = Math.Clamp(topP.Value, 0.0, 1.0);
        }

        return true;
    }

    /// <summary>
    /// 取得模型參數
    /// </summary>
    /// <param name="name">模型名稱</param>
    /// <returns>模型參數，或 null 若未找到</returns>
    public ModelParameterInfo? GetModelParameters(string name)
    {
        var model = GetModel(name);
        if (model is null)
        {
            return null;
        }

        return new ModelParameterInfo
        {
            Name = model.Name,
            Temperature = model.DefaultTemperature,
            MaxTokens = model.MaxTokens,
            TopP = model.DefaultTopP
        };
    }

    /// <summary>
    /// 新增任務類型對應關係
    /// </summary>
    /// <param name="taskType">任務類型</param>
    /// <param name="modelNames">適合的模型名稱</param>
    public void AddTaskModelMapping(string taskType, params string[] modelNames)
    {
        if (!_taskModelMappings.ContainsKey(taskType))
        {
            _taskModelMappings[taskType] = new();
        }

        _taskModelMappings[taskType].AddRange(modelNames);
    }

    /// <summary>
    /// 初始化預設模型
    /// </summary>
    private void InitializeDefaultModels()
    {
        AddModel(new ModelInfo
        {
            Name = "qwen3-coder:30b",
            Description = "強悍的編程與推理模型",
            Provider = "Qwen",
            MaxTokens = 32768,
            DefaultTemperature = 0.7,
            DefaultTopP = 1.0,
            Priority = 10,
            SuitableTasks = new List<string> { "編程", "推理", "複雜任務", "代碼生成", "分析" }
        });

        AddModel(new ModelInfo
        {
            Name = "qwen3-coder:7b",
            Description = "輕量編程模型",
            Provider = "Qwen",
            MaxTokens = 8192,
            DefaultTemperature = 0.7,
            DefaultTopP = 1.0,
            Priority = 5,
            SuitableTasks = new List<string> { "簡單編程", "代碼補全", "快速回應" }
        });

        AddModel(new ModelInfo
        {
            Name = "gpt-4o",
            Description = "通用對話模型",
            Provider = "OpenAI",
            MaxTokens = 128000,
            DefaultTemperature = 0.7,
            DefaultTopP = 1.0,
            Priority = 8,
            SuitableTasks = new List<string> { "對話", "一般任務", "創意寫作" }
        });

        AddModel(new ModelInfo
        {
            Name = "gpt-4",
            Description = "標準對話模型",
            Provider = "OpenAI",
            MaxTokens = 8192,
            DefaultTemperature = 0.7,
            DefaultTopP = 1.0,
            Priority = 3,
            SuitableTasks = new List<string> { "對話", "一般任務" }
        });
    }

    /// <summary>
    /// 初始化任務類型對應關係
    /// </summary>
    private void InitializeTaskMappings()
    {
        // 編程類任務 -> qwen3-coder:30b
        AddTaskModelMapping("編程", "qwen3-coder:30b", "qwen3-coder:7b");
        AddTaskModelMapping("code", "qwen3-coder:30b", "qwen3-coder:7b");
        AddTaskModelMapping("programming", "qwen3-coder:30b", "qwen3-coder:7b");

        // 推理類任務 -> qwen3-coder:30b
        AddTaskModelMapping("推理", "qwen3-coder:30b");
        AddTaskModelMapping("reasoning", "qwen3-coder:30b");
        AddTaskModelMapping("分析", "qwen3-coder:30b");
        AddTaskModelMapping("analysis", "qwen3-coder:30b");

        // 對話類任務 -> gpt-4o
        AddTaskModelMapping("對話", "gpt-4o", "gpt-4");
        AddTaskModelMapping("conversation", "gpt-4o", "gpt-4");
        AddTaskModelMapping("一般", "gpt-4o", "qwen3-coder:7b");
        AddTaskModelMapping("general", "gpt-4o", "qwen3-coder:7b");

        // 創意寫作 -> gpt-4o
        AddTaskModelMapping("創意", "gpt-4o");
        AddTaskModelMapping("creative", "gpt-4o");
        AddTaskModelMapping("writing", "gpt-4o");
    }

    /// <summary>
    /// 檢查任務是否匹配任務類型
    /// </summary>
    /// <param name="task">任務描述</param>
    /// <param name="taskType">任務類型</param>
    /// <returns>是否匹配</returns>
    private bool IsTaskMatchingType(string task, string taskType)
    {
        var lowerTask = task.ToLowerInvariant();
        var lowerType = taskType.ToLowerInvariant();

        // 直接匹配
        if (lowerTask.Contains(lowerType))
        {
            return true;
        }

        // 檢查適合任務的關鍵字
        var model = GetModel(_taskModelMappings[taskType].FirstOrDefault() ?? string.Empty);
        if (model is not null)
        {
            return model.SuitableTasks.Any(st => lowerTask.Contains(st.ToLowerInvariant()));
        }

        return false;
    }

    /// <summary>
    /// 計算任務匹配分數
    /// </summary>
    /// <param name="task">任務描述</param>
    /// <param name="model">模型資訊</param>
    /// <param name="taskType">任務類型</param>
    /// <returns>匹配分數</returns>
    private int CalculateTaskMatchScore(string task, ModelInfo model, string taskType)
    {
        var score = 0;
        var lowerTask = task.ToLowerInvariant();

        // 任務類型匹配加分
        if (lowerTask.Contains(taskType.ToLowerInvariant()))
        {
            score += 20;
        }

        // 適合任務關鍵字匹配加分
        foreach (var suitableTask in model.SuitableTasks)
        {
            if (lowerTask.Contains(suitableTask.ToLowerInvariant()))
            {
                score += 10;
            }
        }

        // 優先級加分
        score += (int)(model.Priority * 2);

        return score;
    }
}

/// <summary>
/// 模型參數資訊
/// </summary>
public class ModelParameterInfo
{
    public string Name { get; set; } = string.Empty;
    public double Temperature { get; set; }
    public int MaxTokens { get; set; }
    public double TopP { get; set; }
}
