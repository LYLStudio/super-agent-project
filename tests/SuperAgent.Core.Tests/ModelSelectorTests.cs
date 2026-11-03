using Xunit;
using SuperAgent.Core;

namespace SuperAgent.Core.Tests;

public class ModelSelectorTests
{
    [Fact]
    public void ModelSelector_GetDefaultModel_ReturnsModel()
    {
        var selector = new ModelSelector();

        var model = selector.GetDefaultModel();
        Assert.Equal("qwen3-coder:30b", model);
    }

    [Fact]
    public void ModelSelector_SelectModelForTask_CodingTask_ReturnsQwen()
    {
        var selector = new ModelSelector();

        var model = selector.SelectModelForTask("請幫我撰寫一個 C# 程式來處理資料");
        Assert.Equal("qwen3-coder:30b", model);
    }

    [Fact]
    public void ModelSelector_SelectModelForTask_ConversationTask_ReturnsGpt()
    {
        var selector = new ModelSelector();

        var model = selector.SelectModelForTask("你好，請與我對話");
        Assert.Equal("gpt-4o", model);
    }

    [Fact]
    public void ModelSelector_SelectModelForTask_EmptyTask_ReturnsDefault()
    {
        var selector = new ModelSelector();

        var model = selector.SelectModelForTask("");
        Assert.Equal("qwen3-coder:30b", model);
    }

    [Fact]
    public void ModelSelector_AddAndGetModel_Success()
    {
        var selector = new ModelSelector();

        selector.AddModel(new ModelInfo
        {
            Name = "test-model",
            Description = "測試模型",
            Provider = "Test",
            MaxTokens = 2048,
            Priority = 1
        });

        var model = selector.GetModel("test-model");
        Assert.NotNull(model);
        Assert.Equal("test-model", model?.Name);
        Assert.Equal("測試模型", model?.Description);
    }

    [Fact]
    public void ModelSelector_SetModelParameters_Success()
    {
        var selector = new ModelSelector();

        var result = selector.SetModelParameters("qwen3-coder:30b", temperature: 0.5, maxTokens: 8192, topP: 0.9);

        Assert.True(result);

        var paramsInfo = selector.GetModelParameters("qwen3-coder:30b");
        Assert.NotNull(paramsInfo);
        Assert.Equal(0.5, paramsInfo.Temperature);
        Assert.Equal(8192, paramsInfo.MaxTokens);
        Assert.Equal(0.9, paramsInfo.TopP);
    }

    [Fact]
    public void ModelSelector_SetModelParameters_InvalidModel_ReturnsFalse()
    {
        var selector = new ModelSelector();

        var result = selector.SetModelParameters("non-existent-model", temperature: 0.5);

        Assert.False(result);
    }

    [Fact]
    public void ModelSelector_GetModelParameters_NullModel_ReturnsNull()
    {
        var selector = new ModelSelector();

        var paramsInfo = selector.GetModelParameters("non-existent-model");
        Assert.Null(paramsInfo);
    }

    [Fact]
    public void ModelSelector_RemoveModel_Success()
    {
        var selector = new ModelSelector();

        // 添加測試模型
        selector.AddModel(new ModelInfo
        {
            Name = "temp-model",
            Description = "臨時模型",
            Provider = "Test",
            Priority = 1
        });

        var result = selector.RemoveModel("temp-model");
        Assert.True(result);

        var model = selector.GetModel("temp-model");
        Assert.Null(model);
    }

    [Fact]
    public void ModelSelector_RemoveModel_NotFound_ReturnsFalse()
    {
        var selector = new ModelSelector();

        var result = selector.RemoveModel("non-existent-model");
        Assert.False(result);
    }

    [Fact]
    public void ModelSelector_GetAllModels_ReturnsActiveModels()
    {
        var selector = new ModelSelector();

        var models = selector.GetAllModels();
        Assert.NotEmpty(models);
        foreach (var m in models)
        {
            Assert.True(m.IsActive);
        }
    }
}
