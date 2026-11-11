using Spectre.Console;
using SuperAgent.Core;
using SuperAgent.Client;
using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

var configPath = Environment.GetEnvironmentVariable("SUPERAGENT_CONFIG")
    ?? Path.Combine(AppContext.BaseDirectory, "appsettings.json");
string? modelOverride = null;
string? baseUrlOverride = null;
string? apiKeyOverride = null;
string? providerOverride = null;

foreach (var arg in args)
{
    if (arg.StartsWith("--api-key=", StringComparison.OrdinalIgnoreCase))
    {
        apiKeyOverride = arg.Substring(arg.IndexOf('=') + 1);
    }
    else if (arg.StartsWith("--model=", StringComparison.OrdinalIgnoreCase))
    {
        modelOverride = arg.Substring(arg.IndexOf('=') + 1);
    }
    else if (arg.StartsWith("--config=", StringComparison.OrdinalIgnoreCase))
    {
        configPath = arg.Substring(arg.IndexOf('=') + 1);
    }
    else if (arg.StartsWith("--base-url=", StringComparison.OrdinalIgnoreCase))
    {
        baseUrlOverride = arg.Substring(arg.IndexOf('=') + 1);
    }
    else if (arg.StartsWith("--provider=", StringComparison.OrdinalIgnoreCase))
    {
        providerOverride = arg.Substring(arg.IndexOf('=') + 1);
    }
}

if (args.Any(arg => arg.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("-h", StringComparison.OrdinalIgnoreCase)))
{
    AnsiConsole.MarkupLine("[bold]SuperAgent AI Agent CLI[/]");
    AnsiConsole.MarkupLine("");
    AnsiConsole.WriteLine("用法: SuperAgent [選項]");
    AnsiConsole.MarkupLine("");
    AnsiConsole.MarkupLine("[bold]選項:[/]");
    AnsiConsole.MarkupLine("  --provider=NAME  推論供應者: OpenAICompatible 或 GitHubCopilot");
    AnsiConsole.MarkupLine("  --model=NAME  使用的模型名稱 (GitHubCopilot 預設: auto)");
    AnsiConsole.MarkupLine("  --api-key=KEY  API 密鑰 (預設: dummy)");
    AnsiConsole.MarkupLine("  --config=PATH  設定檔路徑 (預設: appsettings.json)");
    AnsiConsole.MarkupLine("  --base-url=URL  API 端點 URL (預設: http://192.168.1.102:11434/v1)");
    AnsiConsole.MarkupLine("  --help, -h  顯示此說明");
    AnsiConsole.MarkupLine("");
    return;
}

var fullConfigPath = Path.GetFullPath(configPath);
IConfigurationRoot configuration = BuildConfiguration(fullConfigPath);

var modelProvider = providerOverride
    ?? Environment.GetEnvironmentVariable("SUPERAGENT_MODEL_PROVIDER")
    ?? configuration["Agent:ModelProvider"]
    ?? "OpenAICompatible";
var isCopilotProvider = IsCopilotProvider(modelProvider);
if (!isCopilotProvider && !IsOpenAIProvider(modelProvider))
{
    throw new InvalidOperationException("Agent:ModelProvider 必須是 OpenAICompatible 或 GitHubCopilot。");
}
var model = modelOverride
    ?? (isCopilotProvider
        ? Environment.GetEnvironmentVariable("GITHUB_COPILOT_MODEL")
            ?? configuration["GitHubCopilot:Model"]
            ?? "auto"
        : Environment.GetEnvironmentVariable("OPENAI_MODEL")
            ?? configuration["OpenAI:Model"]
            ?? "gpt-oss:20b");
var baseUrl = baseUrlOverride
    ?? Environment.GetEnvironmentVariable("OPENAI_BASE_URL")
    ?? configuration["OpenAI:BaseUrl"]
    ?? "http://192.168.1.102:11434/v1";
var apiKey = new[]
{
    apiKeyOverride,
    Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
    Environment.GetEnvironmentVariable("SUPERAGENT_API_KEY"),
    configuration["OpenAI:ApiKey"]
}.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "dummy";

var requestTimeoutSeconds = ReadIntSetting("OpenAI:RequestTimeoutSeconds", 120);
if (requestTimeoutSeconds <= 0)
{
    throw new InvalidOperationException("設定 OpenAI:RequestTimeoutSeconds 必須大於 0。");
}

var agentConfig = new AgentLoopConfig
{
    MaxIterations = ReadIntSetting("Agent:MaxIterations", 10),
    Temperature = ReadDoubleSetting("Agent:Temperature", 0.7),
    MaxTokens = ReadIntSetting("Agent:MaxTokens", 4000),
    MaxContextCharacters = ReadIntSetting("Agent:MaxContextCharacters", 32000),
    ShowThinkingProcess = ReadBoolSetting("Agent:ShowThinkingProcess", false),
    DefaultSystemPrompt = configuration["Agent:DefaultSystemPrompt"] ?? string.Empty,
    Region = configuration["Agent:Region"] ?? "台灣",
    Language = configuration["Agent:Language"] ?? "繁體中文（台灣）"
};
var workspaceRoot = Path.GetFullPath(
    configuration["Agent:WorkspaceRoot"] ?? Directory.GetCurrentDirectory());
var allowOutsideWorkspace = ReadBoolSetting("Agent:AllowOutsideWorkspace", false);
var workspaceAccessPolicy = new WorkspaceAccessPolicy(workspaceRoot, allowOutsideWorkspace);
var outsideWorkspaceCommandAccess = ReadOutsideWorkspaceCommandAccess();
var loggingEnabled = ReadBoolSetting("Logging:Enabled", true);
var loggingFilePath = configuration["Logging:Path"] ?? Path.Combine("logs", "superagent.log");
var logRollingInterval = ReadLogRollingInterval();
var loggingFileSizeLimitMb = ReadIntSetting("Logging:FileSizeLimitMB", 10);
if (string.IsNullOrWhiteSpace(loggingFilePath) || loggingFileSizeLimitMb <= 0)
{
    throw new InvalidOperationException("設定 Logging:Path 不可為空，Logging:FileSizeLimitMB 必須大於 0。");
}

if (agentConfig.MaxIterations is < 1 or > 100)
{
    throw new InvalidOperationException("設定 Agent:MaxIterations 必須介於 1 到 100。");
}

if (agentConfig.MaxTokens is < 1 or > 64000)
{
    throw new InvalidOperationException("設定 Agent:MaxTokens 必須介於 1 到 64000。");
}

if (agentConfig.MaxContextCharacters <= 0)
{
    throw new InvalidOperationException("設定 Agent:MaxContextCharacters 必須大於 0。");
}

if (string.IsNullOrWhiteSpace(agentConfig.Region) || string.IsNullOrWhiteSpace(agentConfig.Language))
{
    throw new InvalidOperationException("設定 Agent:Region 與 Agent:Language 不可為空。");
}

var registry = new ToolRegistry();
using var client = new ReloadableAgentClient(CreateAgentClient(
    modelProvider,
    model,
    baseUrl,
    apiKey,
    workspaceAccessPolicy.WorkspaceRoot,
    requestTimeoutSeconds));
InteractiveConsoleView? consoleView = InteractiveConsoleView.IsInteractive
    ? new InteractiveConsoleView($"SuperAgent | {model} | {workspaceAccessPolicy.WorkspaceRoot}")
    : null;
var applicationLogger = CreateApplicationLogger();
ApplyToolConfiguration();
LogApplicationEvent("info", "Application started", "SuperAgent");

var agent = new AgentLoop(client, registry, agentConfig);

// 初始化工具插件系統
var pluginDirectory = Path.Combine(AppContext.BaseDirectory, "plugins");
var pluginLoader = new PluginLoader(pluginDirectory);
var pluginContext = new PluginContext(registry, client, pluginDirectory, AppContext.BaseDirectory);
var loadedPlugins = await pluginLoader.LoadAllPluginsAsync(pluginContext, CancellationToken.None);

if (loadedPlugins.Count > 0)
{
    WriteMarkupLine($"[bold]已載入 {loadedPlugins.Count} 個工具插件[/]");
    foreach (var plugin in loadedPlugins)
    {
        registry.RegisterPluginTool(plugin);
        WriteMarkupLine($"  - {plugin.Name} v{plugin.Version} (作者: {plugin.Author})");
    }
    WriteMarkupLine(string.Empty);
}

var promptHistory = new List<string>();
var streamedIterations = new HashSet<int>();
WriteMarkupLine("[bold]SuperAgent AI Agent CLI[/]");
WriteMarkupLine($"[bold]Model provider:[/] {modelProvider}");
WriteMarkupLine($"[bold]Model:[/] {model}");
if (!isCopilotProvider)
{
    WriteMarkupLine($"[bold]Endpoint:[/] {baseUrl}");
}
WriteMarkupLine($"[bold]Max iterations:[/] {agentConfig.MaxIterations}");
WriteMarkupLine($"[bold]Show thinking process:[/] {agentConfig.ShowThinkingProcess}");
WriteMarkupLine($"[bold]Agent 工作區:[/] {Markup.Escape(workspaceAccessPolicy.WorkspaceRoot)}");
WriteMarkupLine($"[bold]允許工作區外存取:[/] {allowOutsideWorkspace}");
WriteMarkupLine($"[bold]工作區外命令存取:[/] {outsideWorkspaceCommandAccess}");
WriteMarkupLine($"[bold]日誌記錄:[/] {(loggingEnabled ? "啟用" : "停用")} ({Markup.Escape(loggingFilePath)}, {logRollingInterval}, {loggingFileSizeLimitMb} MiB)");
WriteMarkupLine(string.Empty);
agent.ResponseChunkReceived += (iteration, chunk) =>
{
    if (!agentConfig.ShowThinkingProcess)
    {
        return;
    }

    if (streamedIterations.Add(iteration))
    {
        WriteOutputSection($"模型串流 / 第 {iteration} 輪", "cyan");
    }

    WriteMarkup(Markup.Escape(chunk));
};
agent.ToolExecutionStarted += toolCall =>
{
    LogApplicationEvent("info", $"Tool started: {toolCall.Name}", "Agent");
    if (agentConfig.ShowThinkingProcess)
    {
        WriteOutputSection($"▶ 工具執行：{toolCall.Name}", "yellow");
        WriteMarkupLine("[yellow]實際參數:[/]");
        WriteTextLine(JsonSerializer.Serialize(toolCall.Arguments, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }
};
agent.ToolExecutionCompleted += (toolCall, result) =>
{
    LogApplicationEvent("info", $"Tool completed: {toolCall.Name}", "Agent");
    if (agentConfig.ShowThinkingProcess)
    {
        WriteOutputSection($"工具結果：{toolCall.Name}", "magenta");
        WriteTextLine(result);
    }
};

while (true)
{
    consoleView?.SetHeader($"SuperAgent | {model} | {workspaceRoot}");
    var promptResult = ReadPromptInput(promptHistory);
    if (promptResult.Shortcut == "settings")
    {
        consoleView?.Suspend();
        await OpenSettingsMenuAsync();
        consoleView?.Resume();
        ReloadConfiguration(announce: true);
        continue;
    }

    if (promptResult.Shortcut == "reload")
    {
        ReloadConfiguration(announce: true);
        continue;
    }

    if (promptResult.Shortcut == "logs")
    {
        consoleView?.Suspend();
        var logLines = ViewLogs();
        consoleView?.Resume();
        WriteOutputSection($"最近日誌 ({logLines.Count} 筆)", "magenta");
        foreach (var line in logLines)
        {
            WriteMarkupLine(Markup.Escape(line));
        }

        consoleView?.ScrollToLastSection();
        continue;
    }

    var input = promptResult.Input;

    if (input is null)
    {
        break;
    }

    if (string.IsNullOrWhiteSpace(input))
        continue;

    if (input.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase) ||
        input.Trim().Equals("quit", StringComparison.OrdinalIgnoreCase) ||
        input.Trim().Equals("q", StringComparison.OrdinalIgnoreCase))
    {
        WriteMarkupLine("[bold]Goodbye![/]");
        break;
    }

    if (input.Trim().Equals("help", StringComparison.OrdinalIgnoreCase))
    {
        WriteMarkupLine("[bold]可用命令:[/]");
        WriteMarkupLine("  - exit/quit/q: 結束程式");
        WriteMarkupLine("  - help: 顯示此訊息");
        WriteMarkupLine("  - ↑/↓: 瀏覽已送出的提示歷史");
        WriteMarkupLine("  - ←/→、Home/End: 移動游標");
        WriteMarkupLine("  - F2: 開啟設定選單");
        WriteMarkupLine("  - F3: 查看日誌");
        WriteMarkupLine("  - F5: 熱重載設定檔");
        WriteMarkupLine(string.Empty);
        WriteMarkupLine("[bold]請直接輸入你的任務指令，Agent 將自動思考與執行。[/]");
        WriteMarkupLine(string.Empty);
        continue;
    }

    promptHistory.Add(input);
    streamedIterations.Clear();
    WriteOutputSection("正在處理任務", "cyan");
    WriteMarkupLine(string.Empty);

    try
    {
        LogApplicationEvent("info", "Agent request started", "Agent");
        var result = await agent.RunAsync(input);
        LogApplicationEvent(
            result.Success ? "info" : "warning",
            $"Agent request completed: success={result.Success}, state={result.FinalState}, iterations={result.TotalIterations}",
            "Agent");

        WriteMarkupLine(string.Empty);
        WriteMarkupLine($"[bold]狀態:[/] {result.FinalState}");
        WriteMarkupLine($"[bold]迭代次數:[/] {result.TotalIterations}");
        WriteMarkupLine($"[bold]成功:[/] {result.Success}");

        if (!string.IsNullOrEmpty(result.Error))
            WriteMarkupLine($"[red]錯誤:[/] {Markup.Escape(result.Error)}");

        WriteOutputSection("最終回答", "green");
        if (string.IsNullOrWhiteSpace(result.FinalContent))
        {
            WriteMarkupLine("[yellow]（模型未返回文字內容）[/]");
        }
        else
        {
            WriteTextLine(result.FinalContent);
        }
        WriteMarkupLine(string.Empty);
    }
    catch (Exception ex)
    {
        LogApplicationEvent("error", $"Agent request failed: {ex.GetType().Name}", "Agent");
        WriteMarkupLine($"[red]發生錯誤:[/] {Markup.Escape(ex.Message)}");
        WriteMarkupLine(string.Empty);
    }
}

consoleView?.Dispose();

void ApplyToolConfiguration()
{
    var tools = new List<ITool>
    {
        new CodeWriterTool(workspaceAccessPolicy),
        new CodeReaderTool(workspaceAccessPolicy),
        new DatabaseTool(),
        new WebSearchTool(),
        new CodeAnalysisTool(),
        new TestGeneratorTool(),
        new DebuggerTool(),
        new RefactoringTool(),
        new PerformanceMonitor(),
        new LoggingTool(
            loggingEnabled,
            loggingFilePath,
            logRollingInterval,
            checked((long)loggingFileSizeLimitMb * 1024 * 1024),
            echoToConsole: consoleView is null),
        new FileSearchTool(workspaceAccessPolicy),
        new ShellExecTool(
            workspaceAccessPolicy.WorkspaceRoot,
            outsideWorkspaceCommandAccess,
            ConfirmOutsideWorkspaceCommandAsync,
            workspaceAccessPolicy),
        new GitTool(
            workspaceAccessPolicy.WorkspaceRoot,
            outsideWorkspaceCommandAccess,
            ConfirmOutsideWorkspaceCommandAsync,
            workspaceAccessPolicy)
    };

    var configuredTools = new List<ITool>();
    foreach (var tool in tools)
    {
        var toolName = tool.Name;
        var value = configuration[$"Agent:Tools:{toolName}"];
        if (value is not null && !bool.TryParse(value, out _))
        {
            throw new InvalidOperationException($"設定 Agent:Tools:{toolName} 必須是布林值。");
        }

        var enabled = value is null || bool.Parse(value);
        if (enabled)
        {
            configuredTools.Add(tool);
        }
    }

    foreach (var tool in tools)
    {
        registry.Unregister(tool.Name);
    }

    foreach (var tool in configuredTools)
    {
        registry.Register(tool);
    }
}

void WriteMarkupLine(string value)
{
    if (consoleView is null)
    {
        AnsiConsole.MarkupLine(value);
        return;
    }

    consoleView.AppendMarkupLine(value);
}

void WriteOutputSection(string title, string color)
{
    if (consoleView is null)
    {
        WriteMarkupLine(string.Empty);
        WriteMarkupLine($"[bold {color}]── {Markup.Escape(title)} ──[/]");
        return;
    }

    consoleView.AppendSectionHeader(title, color);
}

void WriteMarkup(string value)
{
    if (consoleView is null)
    {
        AnsiConsole.Markup(value);
        return;
    }

    consoleView.AppendMarkup(value);
}

void WriteTextLine(string value)
{
    if (consoleView is null)
    {
        AnsiConsole.WriteLine(value);
        return;
    }

    consoleView.AppendText(value + Environment.NewLine);
}

LoggingTool CreateApplicationLogger()
{
    return new LoggingTool(
        loggingEnabled,
        loggingFilePath,
        logRollingInterval,
        checked((long)loggingFileSizeLimitMb * 1024 * 1024),
        echoToConsole: consoleView is null);
}

void LogApplicationEvent(string level, string message, string source)
{
    var result = applicationLogger.WriteApplicationEvent(level, message, source);
    if (result.StartsWith("❌", StringComparison.Ordinal))
    {
        WriteMarkupLine($"[red]應用程式日誌寫入失敗:[/] {Markup.Escape(result)}");
    }
}

void ManageToolSettings()
{
    var tools = GetToolChoices();
    while (true)
    {
        var choices = tools.Select(tool =>
        {
            var enabled = ReadToolEnabled(tool.Name);
            var status = !enabled ? "停用" : "啟用";
            return $"{tool.Label} ({status})";
        }).Append("返回設定").ToList();
        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold]設定工具啟用狀態[/]")
                .PageSize(20)
                .AddChoices(choices));

        if (selected == "返回設定")
        {
            return;
        }

        var selectedTool = tools.First(tool => selected.StartsWith(tool.Label + " (", StringComparison.Ordinal));
        var nextEnabled = !ReadToolEnabled(selectedTool.Name);
        SaveConfigurationValue(
            fullConfigPath,
            "Agent:Tools",
            selectedTool.Name,
            JsonValue.Create(nextEnabled));
        AnsiConsole.MarkupLine(
            $"[green]{Markup.Escape(selectedTool.Label)} 已{(nextEnabled ? "啟用" : "停用")}。[/]");
        var previousConfiguration = configuration;
        configuration = BuildConfiguration(fullConfigPath);
        (previousConfiguration as IDisposable)?.Dispose();
        ApplyToolConfiguration();
    }
}

bool ReadToolEnabled(string toolName)
{
    var value = configuration[$"Agent:Tools:{toolName}"];
    if (value is null)
    {
        return true;
    }

    if (bool.TryParse(value, out var enabled))
    {
        return enabled;
    }

    throw new InvalidOperationException($"設定 Agent:Tools:{toolName} 必須是布林值。");
}

IReadOnlyList<(string Name, string Label)> GetToolChoices()
{
    return
    [
        ("code_writer", "程式碼寫入"),
        ("code_reader", "程式碼讀取"),
        ("shell_exec", "Shell 命令"),
        ("database_query", "資料庫查詢"),
        ("web_search", "網路搜尋"),
        ("code_analyzer", "程式碼分析"),
        ("test_generator", "測試產生"),
        ("debugger", "除錯"),
        ("refactoring", "重構建議"),
        ("performance_monitor", "效能監控"),
        ("logging", "日誌"),
        ("file_search", "檔案搜尋"),
        ("git_tool", "Git")
    ];
}

async Task OpenSettingsMenuAsync()
{
    while (true)
    {
        var setting = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold]設定 (儲存後返回會立即套用)[/]")
                .PageSize(22)
                .AddChoices(
                    "API 端點 URL",
                    "API 密鑰",
                    "模型名稱",
                    "請求逾時秒數",
                    "最大迭代次數",
                    "Temperature",
                    "最大輸出 Token",
                    "上下文字元上限",
                    "顯示處理過程",
                    "系統預設提示詞",
                    "地區",
                    "語系",
                    "Agent 工作區路徑",
                    "允許工作區外存取",
                    "工作區外命令存取",
                    "日誌記錄啟用",
                    "日誌切檔週期",
                    "日誌檔案大小上限 (MiB)",
                    "管理工具啟用狀態",
                    "返回對話"));

        if (setting == "返回對話")
        {
            return;
        }

        if (setting == "模型名稱")
        {
            await SelectModelFromEndpointAsync();
        }
        else if (setting == "管理工具啟用狀態")
        {
            ManageToolSettings();
        }
        else if (setting == "工作區外命令存取")
        {
            EditOutsideWorkspaceCommandAccess();
        }
        else
        {
            EditConfigurationSetting(fullConfigPath, setting);
        }

        var updatedConfiguration = BuildConfiguration(fullConfigPath);
        (configuration as IDisposable)?.Dispose();
        configuration = updatedConfiguration;
    }
}

IReadOnlyList<string> ViewLogs()
{
    var filter = AnsiConsole.Prompt(
        new TextPrompt<string>("日誌篩選文字 (可留空):")
            .AllowEmpty()
            .DefaultValue(string.Empty));

    try
    {
        var logReader = new LoggingTool(
            loggingEnabled,
            loggingFilePath,
            logRollingInterval,
            checked((long)loggingFileSizeLimitMb * 1024 * 1024));
        var entries = logReader.ReadRecentLogs(limit: 50, query: filter);
        if (entries.Count == 0)
        {
            return [$"找不到符合條件的日誌紀錄。日誌路徑：{loggingFilePath}"];
        }

        return entries.Select(entry =>
        {
            var message = entry.Message.Length > 180
                ? entry.Message[..177] + "..."
                : entry.Message;
            return $"{entry.Timestamp} [{entry.Level}] [{entry.Source}] {message}";
        }).ToList();
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
    {
        return [$"讀取日誌失敗：{ex.Message}"];
    }
}

async Task SelectModelFromEndpointAsync()
{
    var settings = BuildConfiguration(fullConfigPath);
    try
    {
        if (IsCopilotProvider(modelProvider))
        {
            var configuredCopilotModel = Environment.GetEnvironmentVariable("GITHUB_COPILOT_MODEL")
                ?? settings["GitHubCopilot:Model"]
                ?? "auto";
            var selectedCopilotModel = AnsiConsole.Ask(
                "GitHub Copilot 模型名稱:",
                configuredCopilotModel);
            if (string.IsNullOrWhiteSpace(selectedCopilotModel))
            {
                AnsiConsole.MarkupLine("[yellow]模型名稱不可為空，設定未變更。[/]");
                return;
            }

            SaveConfigurationValue(fullConfigPath, "GitHubCopilot", "Model", JsonValue.Create(selectedCopilotModel));
            AnsiConsole.MarkupLine($"[green]已保存 GitHub Copilot 模型 {Markup.Escape(selectedCopilotModel)}。[/]");
            return;
        }

        var endpoint = baseUrlOverride
            ?? Environment.GetEnvironmentVariable("OPENAI_BASE_URL")
            ?? settings["OpenAI:BaseUrl"]
            ?? "http://192.168.1.102:11434/v1";
        var key = new[]
        {
            apiKeyOverride,
            Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
            Environment.GetEnvironmentVariable("SUPERAGENT_API_KEY"),
            settings["OpenAI:ApiKey"]
        }.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "dummy";
        var configuredModel = settings["OpenAI:Model"] ?? model;
        var timeoutSeconds = int.TryParse(
            settings["OpenAI:RequestTimeoutSeconds"],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var configuredTimeout) && configuredTimeout > 0
                ? Math.Min(configuredTimeout, 10)
                : 10;

        IReadOnlyList<string> availableModels;
        using (var modelCatalogClient = new OpenAICompatClient(
                   endpoint,
                   () => key,
                   () => configuredModel,
                   requestTimeout: TimeSpan.FromSeconds(timeoutSeconds)))
        {
            availableModels = await modelCatalogClient.GetAvailableModelsAsync();
        }

        if (availableModels.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]端點未回傳可用模型，改用手動輸入。[/]");
            EditConfigurationSetting(fullConfigPath, "模型名稱");
            return;
        }

        var choices = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var availableModel in availableModels)
        {
            var label = string.Equals(availableModel, configuredModel, StringComparison.OrdinalIgnoreCase)
                ? $"{availableModel} (目前)"
                : availableModel;
            choices[label] = availableModel;
        }

        if (!availableModels.Contains(configuredModel, StringComparer.OrdinalIgnoreCase))
        {
            choices[$"{configuredModel} (目前，端點清單外)"] = configuredModel;
        }

        choices["手動輸入模型名稱…"] = string.Empty;
        var selectedLabel = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"[bold]選擇模型[/] [grey]({Markup.Escape(endpoint)})[/]")
                .PageSize(15)
                .AddChoices(choices.Keys));
        var selectedModel = choices[selectedLabel];

        if (string.IsNullOrEmpty(selectedModel))
        {
            EditConfigurationSetting(fullConfigPath, "模型名稱");
            return;
        }

        SaveConfigurationValue(fullConfigPath, "OpenAI", "Model", JsonValue.Create(selectedModel));
        AnsiConsole.MarkupLine($"[green]已保存模型 {Markup.Escape(selectedModel)}。[/]");
    }
    catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException or OperationCanceledException or UriFormatException)
    {
        AnsiConsole.MarkupLine($"[yellow]無法從端點取得模型清單: {Markup.Escape(ex.Message)}[/]");
        AnsiConsole.MarkupLine("[grey]改用手動輸入模型名稱。[/]");
        EditConfigurationSetting(fullConfigPath, "模型名稱");
    }
    finally
    {
        (settings as IDisposable)?.Dispose();
    }
}

void ReloadConfiguration(bool announce)
{
    var previousConfiguration = configuration;
    var nextConfiguration = BuildConfiguration(fullConfigPath);
    configuration = nextConfiguration;

    try
    {
        var nextModelProvider = providerOverride
            ?? Environment.GetEnvironmentVariable("SUPERAGENT_MODEL_PROVIDER")
            ?? configuration["Agent:ModelProvider"]
            ?? "OpenAICompatible";
        var nextIsCopilotProvider = IsCopilotProvider(nextModelProvider);
        if (!nextIsCopilotProvider && !IsOpenAIProvider(nextModelProvider))
        {
            throw new InvalidOperationException("Agent:ModelProvider 必須是 OpenAICompatible 或 GitHubCopilot。");
        }
        var nextModel = modelOverride
            ?? (nextIsCopilotProvider
                ? Environment.GetEnvironmentVariable("GITHUB_COPILOT_MODEL")
                    ?? configuration["GitHubCopilot:Model"]
                    ?? "auto"
                : Environment.GetEnvironmentVariable("OPENAI_MODEL")
                    ?? configuration["OpenAI:Model"]
                    ?? "gpt-oss:20b");
        var nextBaseUrl = baseUrlOverride
            ?? Environment.GetEnvironmentVariable("OPENAI_BASE_URL")
            ?? configuration["OpenAI:BaseUrl"]
            ?? "http://192.168.1.102:11434/v1";
        var nextApiKey = new[]
        {
            apiKeyOverride,
            Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
            Environment.GetEnvironmentVariable("SUPERAGENT_API_KEY"),
            configuration["OpenAI:ApiKey"]
        }.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "dummy";
        var nextTimeout = ReadIntSetting("OpenAI:RequestTimeoutSeconds", 120);
        var nextWorkspaceRoot = Path.GetFullPath(
            configuration["Agent:WorkspaceRoot"] ?? Directory.GetCurrentDirectory());
        var nextAllowOutside = ReadBoolSetting("Agent:AllowOutsideWorkspace", false);
        var nextOutsideCommandAccess = ReadOutsideWorkspaceCommandAccess();
        var nextWorkspacePolicy = new WorkspaceAccessPolicy(nextWorkspaceRoot, nextAllowOutside);
        var nextMaxIterations = ReadIntSetting("Agent:MaxIterations", 10);
        var nextMaxTokens = ReadIntSetting("Agent:MaxTokens", 4000);
        var nextMaxContextCharacters = ReadIntSetting("Agent:MaxContextCharacters", 32000);
        var nextTemperature = ReadDoubleSetting("Agent:Temperature", 0.7);
        var nextShowThinking = ReadBoolSetting("Agent:ShowThinkingProcess", false);
        var nextDefaultSystemPrompt = configuration["Agent:DefaultSystemPrompt"] ?? string.Empty;
        var nextRegion = configuration["Agent:Region"] ?? "台灣";
        var nextLanguage = configuration["Agent:Language"] ?? "繁體中文（台灣）";
        var nextLoggingEnabled = ReadBoolSetting("Logging:Enabled", true);
        var nextLoggingFilePath = configuration["Logging:Path"] ?? Path.Combine("logs", "superagent.log");
        var nextLogRollingInterval = ReadLogRollingInterval();
        var nextLoggingFileSizeLimitMb = ReadIntSetting("Logging:FileSizeLimitMB", 10);

        if (nextTimeout <= 0 ||
            nextMaxIterations is < 1 or > 100 ||
            nextMaxTokens is < 1 or > 64000 ||
            nextMaxContextCharacters <= 0 ||
            string.IsNullOrWhiteSpace(nextRegion) ||
            string.IsNullOrWhiteSpace(nextLanguage) ||
            string.IsNullOrWhiteSpace(nextLoggingFilePath) ||
            nextLoggingFileSizeLimitMb <= 0 ||
            !double.IsFinite(nextTemperature) ||
            nextTemperature is < 0 or > 2)
        {
            throw new InvalidOperationException("設定檔中有無效的逾時、Agent 參數或工作區設定。");
        }

        foreach (var tool in GetToolChoices())
        {
            var enabled = configuration[$"Agent:Tools:{tool.Name}"];
            if (enabled is not null && !bool.TryParse(enabled, out _))
            {
                throw new InvalidOperationException($"設定 Agent:Tools:{tool.Name} 必須是布林值。");
            }
        }

        var nextClient = CreateAgentClient(
            nextModelProvider,
            nextModel,
            nextBaseUrl,
            nextApiKey,
            nextWorkspacePolicy.WorkspaceRoot,
            nextTimeout);

        modelProvider = nextModelProvider;
        model = nextModel;
        baseUrl = nextBaseUrl;
        apiKey = nextApiKey;
        requestTimeoutSeconds = nextTimeout;
        workspaceRoot = nextWorkspacePolicy.WorkspaceRoot;
        allowOutsideWorkspace = nextAllowOutside;
        outsideWorkspaceCommandAccess = nextOutsideCommandAccess;
        workspaceAccessPolicy = nextWorkspacePolicy;
        loggingEnabled = nextLoggingEnabled;
        loggingFilePath = nextLoggingFilePath;
        logRollingInterval = nextLogRollingInterval;
        loggingFileSizeLimitMb = nextLoggingFileSizeLimitMb;
        applicationLogger = CreateApplicationLogger();
        agentConfig.MaxIterations = nextMaxIterations;
        agentConfig.MaxTokens = nextMaxTokens;
        agentConfig.MaxContextCharacters = nextMaxContextCharacters;
        agentConfig.Temperature = nextTemperature;
        agentConfig.ShowThinkingProcess = nextShowThinking;
        agentConfig.DefaultSystemPrompt = nextDefaultSystemPrompt;
        agentConfig.Region = nextRegion;
        agentConfig.Language = nextLanguage;

        ApplyToolConfiguration();
        client.Replace(nextClient);
        LogApplicationEvent("info", "Configuration reloaded", "SuperAgent");
        consoleView?.SetHeader($"SuperAgent | {model} | {workspaceRoot}");

        (previousConfiguration as IDisposable)?.Dispose();
        if (announce)
        {
            WriteMarkupLine("[green]設定已熱重載並套用至後續對話。[/]");
            WriteMarkupLine($"[grey]供應者: {Markup.Escape(modelProvider)} | 模型: {Markup.Escape(model)} | 工作區: {Markup.Escape(workspaceRoot)} | 檔案工作區外存取: {allowOutsideWorkspace} | 工作區外命令存取: {outsideWorkspaceCommandAccess} | 日誌: {(loggingEnabled ? "啟用" : "停用")}[/]");
        }
    }
    catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException or UnauthorizedAccessException or FormatException or ArgumentException)
    {
        configuration = previousConfiguration;
        (nextConfiguration as IDisposable)?.Dispose();
        WriteMarkupLine($"[red]設定重載失敗，保留目前設定: {Markup.Escape(ex.Message)}[/]");
    }
}

IStreamingAgentClient CreateAgentClient(
    string provider,
    string selectedModel,
    string endpoint,
    string selectedApiKey,
    string selectedWorkspace,
    int timeoutSeconds)
{
    if (IsCopilotProvider(provider))
    {
        return new GitHubCopilotAgentClient(
            registry,
            selectedModel,
            selectedWorkspace,
            TimeSpan.FromSeconds(timeoutSeconds));
    }

    return new OpenAICompatClient(
        endpoint,
        () => selectedApiKey,
        () => selectedModel,
        requestTimeout: TimeSpan.FromSeconds(timeoutSeconds));
}

bool IsCopilotProvider(string provider)
{
    return provider.Equals("GitHubCopilot", StringComparison.OrdinalIgnoreCase) ||
        provider.Equals("github-copilot", StringComparison.OrdinalIgnoreCase) ||
        provider.Equals("copilot", StringComparison.OrdinalIgnoreCase);
}

bool IsOpenAIProvider(string provider)
{
    return provider.Equals("OpenAICompatible", StringComparison.OrdinalIgnoreCase) ||
        provider.Equals("openai-compatible", StringComparison.OrdinalIgnoreCase) ||
        provider.Equals("openai", StringComparison.OrdinalIgnoreCase);
}

int ReadIntSetting(string key, int defaultValue)
{
    var value = configuration[key];
    if (value is null)
    {
        return defaultValue;
    }

    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
    {
        return result;
    }

    throw new InvalidOperationException($"設定 {key} 的值 '{value}' 不是有效的整數。");
}

double ReadDoubleSetting(string key, double defaultValue)
{
    var value = configuration[key];
    if (value is null)
    {
        return defaultValue;
    }

    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
    {
        return result;
    }

    throw new InvalidOperationException($"設定 {key} 的值 '{value}' 不是有效的數字。");
}

bool ReadBoolSetting(string key, bool defaultValue)
{
    var value = configuration[key];
    if (value is null)
    {
        return defaultValue;
    }

    if (bool.TryParse(value, out var result))
    {
        return result;
    }

    throw new InvalidOperationException($"設定 {key} 的值 '{value}' 不是有效的布林值。");
}

LogRollingInterval ReadLogRollingInterval()
{
    var value = configuration["Logging:RollingInterval"];
    if (string.IsNullOrWhiteSpace(value))
    {
        return LogRollingInterval.Day;
    }

    return value.Equals("Day", StringComparison.OrdinalIgnoreCase)
        ? LogRollingInterval.Day
        : value.Equals("Hour", StringComparison.OrdinalIgnoreCase)
            ? LogRollingInterval.Hour
            : throw new InvalidOperationException("設定 Logging:RollingInterval 必須是 Day 或 Hour。");
}

OutsideWorkspaceCommandAccess ReadOutsideWorkspaceCommandAccess()
{
    var value = configuration["Agent:OutsideWorkspaceCommandAccess"];
    if (string.IsNullOrWhiteSpace(value))
    {
        return OutsideWorkspaceCommandAccess.Deny;
    }

    if (Enum.TryParse<OutsideWorkspaceCommandAccess>(value, ignoreCase: true, out var access) &&
        Enum.IsDefined(access))
    {
        return access;
    }

    throw new InvalidOperationException(
        "設定 Agent:OutsideWorkspaceCommandAccess 必須是 Deny、Confirm 或 Allow。");
}

void EditOutsideWorkspaceCommandAccess()
{
    var selected = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title($"工作區外命令存取 (目前: {outsideWorkspaceCommandAccess})")
            .AddChoices(
                "拒絕工作區外目錄 (Deny)",
                "每次提示確認 (Confirm)",
                "允許工作區外目錄 (Allow)"));
    var value = selected switch
    {
        "拒絕工作區外目錄 (Deny)" => OutsideWorkspaceCommandAccess.Deny,
        "每次提示確認 (Confirm)" => OutsideWorkspaceCommandAccess.Confirm,
        "允許工作區外目錄 (Allow)" => OutsideWorkspaceCommandAccess.Allow,
        _ => throw new InvalidOperationException("未知的工作區外命令存取模式。")
    };

    SaveConfigurationValue(
        fullConfigPath,
        "Agent",
        "OutsideWorkspaceCommandAccess",
        JsonValue.Create(value.ToString()));
    outsideWorkspaceCommandAccess = value;
    AnsiConsole.MarkupLine($"[green]工作區外命令存取已設為 {value}。[/]");
}

Task<bool> ConfirmOutsideWorkspaceCommandAsync(string path, CancellationToken cancellationToken)
{
    if (Console.IsInputRedirected || Console.IsOutputRedirected || cancellationToken.IsCancellationRequested)
    {
        return Task.FromResult(false);
    }

    var confirmed = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title($"[yellow]命令要求使用工作區外目錄：{Markup.Escape(path)}。是否允許這次操作？[/]")
            .AddChoices("拒絕", "允許這次操作"));
    return Task.FromResult(confirmed == "允許這次操作");
}

(string? Input, string? Shortcut) ReadPromptInput(IReadOnlyList<string> history)
{
    if (consoleView is not null)
    {
        return consoleView.ReadInput(history);
    }

    if (Console.IsInputRedirected || Console.IsOutputRedirected)
    {
        return (Console.ReadLine(), null);
    }

    var buffer = new System.Text.StringBuilder();
    var cursor = 0;
    var historyIndex = history.Count;
    var draft = string.Empty;
    var previousDisplayWidth = 0;
    Console.Write("\u001b[s");

    void Redraw()
    {
        var value = buffer.ToString();
        Console.Write("\u001b[u");
        Console.Write(value);
        var displayWidth = GetTerminalDisplayWidth(value);
        if (previousDisplayWidth > displayWidth)
        {
            Console.Write(new string(' ', previousDisplayWidth - displayWidth));
        }

        previousDisplayWidth = displayWidth;
        Console.Write("\u001b[u");
        Console.Write(value.AsSpan(0, cursor));
    }

    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        var text = buffer.ToString();
        var textElementStarts = StringInfo.ParseCombiningCharacters(text);
        var keyHandled = true;

        switch (key.Key)
        {
            case ConsoleKey.Enter:
                Console.Write("\u001b[u");
                Console.Write(buffer.ToString());
                Console.WriteLine();
                return (buffer.ToString(), null);
            case ConsoleKey.F2:
                Console.WriteLine();
                return (null, "settings");
            case ConsoleKey.F3:
                Console.WriteLine();
                return (null, "logs");
            case ConsoleKey.F5:
                Console.WriteLine();
                return (null, "reload");
            case ConsoleKey.LeftArrow:
                for (var index = textElementStarts.Length - 1; index >= 0; index--)
                {
                    if (textElementStarts[index] < cursor)
                    {
                        cursor = textElementStarts[index];
                        break;
                    }
                }
                break;
            case ConsoleKey.RightArrow:
                cursor = textElementStarts.FirstOrDefault(start => start > cursor, buffer.Length);
                break;
            case ConsoleKey.UpArrow:
                if (history.Count > 0 && historyIndex > 0)
                {
                    if (historyIndex == history.Count)
                    {
                        draft = text;
                    }

                    historyIndex--;
                    buffer.Clear().Append(history[historyIndex]);
                    cursor = buffer.Length;
                }
                break;
            case ConsoleKey.DownArrow:
                if (historyIndex < history.Count)
                {
                    historyIndex++;
                    var replacement = historyIndex == history.Count ? draft : history[historyIndex];
                    buffer.Clear().Append(replacement);
                    cursor = buffer.Length;
                }
                break;
            case ConsoleKey.Home:
                cursor = 0;
                break;
            case ConsoleKey.End:
                cursor = buffer.Length;
                break;
            case ConsoleKey.Backspace:
                for (var index = textElementStarts.Length - 1; index >= 0; index--)
                {
                    if (textElementStarts[index] < cursor)
                    {
                        buffer.Remove(textElementStarts[index], cursor - textElementStarts[index]);
                        cursor = textElementStarts[index];
                        break;
                    }
                }
                break;
            case ConsoleKey.Delete:
                var nextTextElement = textElementStarts.FirstOrDefault(start => start > cursor, buffer.Length);
                if (nextTextElement > cursor)
                {
                    buffer.Remove(cursor, nextTextElement - cursor);
                }
                break;
            default:
                if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == ConsoleKey.A)
                {
                    cursor = 0;
                }
                else if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == ConsoleKey.E)
                {
                    cursor = buffer.Length;
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    buffer.Insert(cursor, key.KeyChar);
                    cursor++;
                }
                else
                {
                    keyHandled = false;
                }
                break;
        }

        if (keyHandled)
        {
            Redraw();
        }
    }
}

int GetTerminalDisplayWidth(string value)
{
    var width = 0;
    var elements = StringInfo.GetTextElementEnumerator(value);
    while (elements.MoveNext())
    {
        var elementWidth = 0;
        foreach (var rune in elements.GetTextElement().EnumerateRunes())
        {
            var category = System.Text.Rune.GetUnicodeCategory(rune);
            if (category is System.Globalization.UnicodeCategory.NonSpacingMark or
                System.Globalization.UnicodeCategory.EnclosingMark or
                System.Globalization.UnicodeCategory.Format)
            {
                continue;
            }

            elementWidth = Math.Max(elementWidth, IsWideTerminalRune(rune.Value) ? 2 : 1);
        }

        width += elementWidth;
    }

    return width;
}

bool IsWideTerminalRune(int value)
{
    return value is >= 0x1100 and <= 0x115F
        or >= 0x2329 and <= 0x232A
        or >= 0x2E80 and <= 0x303E
        or >= 0x3040 and <= 0xA4CF
        or >= 0xAC00 and <= 0xD7A3
        or >= 0xF900 and <= 0xFAFF
        or >= 0xFE10 and <= 0xFE19
        or >= 0xFE30 and <= 0xFE6F
        or >= 0xFF01 and <= 0xFF60
        or >= 0xFFE0 and <= 0xFFE6
        or >= 0x1F300 and <= 0x1FAFF
        or >= 0x20000 and <= 0x3FFFD;
}

IConfigurationRoot BuildConfiguration(string path)
{
    return new ConfigurationBuilder()
        .SetBasePath(Path.GetDirectoryName(path)!)
        .AddJsonFile(Path.GetFileName(path), optional: false, reloadOnChange: true)
        .AddEnvironmentVariables()
        .Build();
}

void EditConfigurationSetting(string path, string setting)
{
    var (sectionName, key, type, defaultValue) = setting switch
    {
        "API 端點 URL" => ("OpenAI", "BaseUrl", "string", "http://192.168.1.102:11434/v1"),
        "API 密鑰" => ("OpenAI", "ApiKey", "secret", "dummy"),
        "模型名稱" => ("OpenAI", "Model", "string", "gpt-oss:20b"),
        "請求逾時秒數" => ("OpenAI", "RequestTimeoutSeconds", "positiveInt", "120"),
        "最大迭代次數" => ("Agent", "MaxIterations", "iterations", "10"),
        "Temperature" => ("Agent", "Temperature", "temperature", "0.7"),
        "最大輸出 Token" => ("Agent", "MaxTokens", "tokens", "4000"),
        "上下文字元上限" => ("Agent", "MaxContextCharacters", "positiveInt", "32000"),
        "顯示處理過程" => ("Agent", "ShowThinkingProcess", "bool", "false"),
        "系統預設提示詞" => ("Agent", "DefaultSystemPrompt", "multiline", ""),
        "地區" => ("Agent", "Region", "string", "台灣"),
        "語系" => ("Agent", "Language", "string", "繁體中文（台灣）"),
        "Agent 工作區路徑" => ("Agent", "WorkspaceRoot", "directory", Directory.GetCurrentDirectory()),
        "允許工作區外存取" => ("Agent", "AllowOutsideWorkspace", "bool", "false"),
        "日誌記錄啟用" => ("Logging", "Enabled", "bool", "true"),
        "日誌切檔週期" => ("Logging", "RollingInterval", "rollingInterval", "Day"),
        "日誌檔案大小上限 (MiB)" => ("Logging", "FileSizeLimitMB", "positiveInt", "10"),
        _ => throw new ArgumentOutOfRangeException(nameof(setting), setting, "未知的設定項目。")
    };

    var configurationFile = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
        ?? throw new InvalidDataException($"設定檔 '{path}' 必須是 JSON 物件。");
    var section = GetOrCreateSection(configurationFile, sectionName);

    var currentValue = section[key]?.ToString() ?? defaultValue;
    JsonNode? updatedValue;

    if (type == "multiline")
    {
        Console.WriteLine($"目前系統預設提示詞：{(string.IsNullOrEmpty(currentValue) ? "（未設定）" : Environment.NewLine + currentValue)}");
        Console.WriteLine("輸入新的多行提示詞；單獨輸入 :save 儲存，單獨輸入 :clear 清除。");
        var lines = new List<string>();
        while (Console.ReadLine() is { } line)
        {
            if (line == ":clear")
            {
                lines.Clear();
                break;
            }

            if (line == ":save")
            {
                break;
            }

            lines.Add(line);
        }

        updatedValue = JsonValue.Create(string.Join(Environment.NewLine, lines));
    }
    else if (type == "bool")
    {
        var current = bool.TryParse(currentValue, out var parsed) && parsed;
        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"{Markup.Escape(setting)} (目前: {(current ? "開啟" : "關閉")})")
                .AddChoices("開啟", "關閉"));
        updatedValue = JsonValue.Create(selected == "開啟");
    }
    else if (type == "secret")
    {
        var value = AnsiConsole.Prompt(
            new TextPrompt<string>("輸入新的 API 密鑰 (留空保持不變):")
                .AllowEmpty()
                .Secret());
        if (string.IsNullOrEmpty(value))
        {
            AnsiConsole.MarkupLine("[grey]密鑰未變更。[/]");
            return;
        }

        updatedValue = JsonValue.Create(value);
    }
    else if (type == "rollingInterval")
    {
        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"{Markup.Escape(setting)} (目前: {Markup.Escape(currentValue)})")
                .AddChoices("每日切檔 (Day)", "每小時切檔 (Hour)"));
        updatedValue = JsonValue.Create(selected == "每日切檔 (Day)" ? "Day" : "Hour");
    }
    else
    {
        while (true)
        {
            var prompt = type switch
            {
                "positiveInt" => $"{setting} (目前: {Markup.Escape(currentValue)}，需大於 0):",
                "iterations" => $"{setting} (目前: {Markup.Escape(currentValue)}，範圍 1-100):",
                "tokens" => $"{setting} (目前: {Markup.Escape(currentValue)}，範圍 1-64000):",
                "temperature" => $"{setting} (目前: {Markup.Escape(currentValue)}，範圍 0-2):",
                "directory" => $"{setting} (目前: {Markup.Escape(currentValue)}，必須是已存在的目錄):",
                _ => $"{setting} (目前: {Markup.Escape(currentValue)}):"
            };
            var value = AnsiConsole.Prompt(new TextPrompt<string>(prompt).DefaultValue(currentValue));

            if (type is "string" or "directory")
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    AnsiConsole.MarkupLine("[red]此設定不可為空。[/]");
                    continue;
                }

                if (type == "directory" && !Directory.Exists(value))
                {
                    AnsiConsole.MarkupLine("[red]工作區目錄不存在，請輸入有效的目錄路徑。[/]");
                    continue;
                }

                updatedValue = JsonValue.Create(type == "directory" ? Path.GetFullPath(value) : value);
                break;
            }

            if (type == "temperature")
            {
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var temperature) &&
                    double.IsFinite(temperature) && temperature is >= 0 and <= 2)
                {
                    updatedValue = JsonValue.Create(temperature);
                    break;
                }
            }
            else if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer) &&
                     (type switch
                     {
                         "iterations" => integer is >= 1 and <= 100,
                         "tokens" => integer is >= 1 and <= 64000,
                         "positiveInt" => integer > 0,
                         _ => false
                     }))
            {
                updatedValue = JsonValue.Create(integer);
                break;
            }

            AnsiConsole.MarkupLine("[red]輸入值無效，請依提示範圍重新輸入。[/]");
        }
    }

    SaveConfigurationValue(path, sectionName, key, updatedValue);
    AnsiConsole.MarkupLine($"[green]已保存 {Markup.Escape(setting)}。[/]");
}

void SaveConfigurationValue(string path, string sectionName, string key, JsonNode? value)
{
    var configurationFile = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
        ?? throw new InvalidDataException($"設定檔 '{path}' 必須是 JSON 物件。");
    var pathSegments = sectionName.Split(':', StringSplitOptions.RemoveEmptyEntries)
        .Append(key)
        .ToArray();
    var current = configurationFile;
    foreach (var segment in pathSegments[..^1])
    {
        current = GetOrCreateSection(current, segment);
    }

    current[pathSegments[^1]] = value;
    File.WriteAllText(
        path,
        configurationFile.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
}

JsonObject GetOrCreateSection(JsonObject configurationFile, string sectionName)
{
    var section = configurationFile[sectionName] switch
    {
        null => new JsonObject(),
        JsonObject existingSection => existingSection,
        _ => throw new InvalidDataException($"設定檔中的 '{sectionName}' 必須是 JSON 物件。")
    };
    configurationFile[sectionName] = section;
    return section;
}
