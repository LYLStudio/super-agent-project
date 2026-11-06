using Spectre.Console;
using SuperAgent.Core;
using SuperAgent.Client;
using BoxOfYellow.ConsoleMarkdownRenderer.Spectre;
using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.Text.Json;

var configPath = Environment.GetEnvironmentVariable("SUPERAGENT_CONFIG")
    ?? Path.Combine(AppContext.BaseDirectory, "appsettings.json");
string? modelOverride = null;
string? baseUrlOverride = null;
string? apiKeyOverride = null;

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
}

if (args.Any(arg => arg.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("-h", StringComparison.OrdinalIgnoreCase)))
{
    AnsiConsole.MarkupLine("[bold]SuperAgent AI Agent CLI[/]");
    AnsiConsole.MarkupLine("");
    AnsiConsole.MarkupLine("用法: SuperAgent [選項]");
    AnsiConsole.MarkupLine("");
    AnsiConsole.MarkupLine("[bold]選項:[/]");
    AnsiConsole.MarkupLine("  --model=NAME  使用的模型名稱 (預設: gpt-oss:20b)");
    AnsiConsole.MarkupLine("  --api-key=KEY  API 密鑰 (預設: dummy)");
    AnsiConsole.MarkupLine("  --config=PATH  設定檔路徑 (預設: appsettings.json)");
    AnsiConsole.MarkupLine("  --base-url=URL  API 端點 URL (預設: http://192.168.1.102:11434/v1)");
    AnsiConsole.MarkupLine("  --help, -h  顯示此說明");
    AnsiConsole.MarkupLine("");
    return;
}

var fullConfigPath = Path.GetFullPath(configPath);
var configuration = new ConfigurationBuilder()
    .SetBasePath(Path.GetDirectoryName(fullConfigPath)!)
    .AddJsonFile(Path.GetFileName(fullConfigPath), optional: false, reloadOnChange: true)
    .AddEnvironmentVariables()
    .Build();

var model = modelOverride
    ?? Environment.GetEnvironmentVariable("OPENAI_MODEL")
    ?? configuration["OpenAI:Model"]
    ?? "gpt-oss:20b";
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

var modelCatalogClient = new OpenAICompatClient(baseUrl, () => apiKey, () => model);
try
{
    var availableModels = await modelCatalogClient.GetAvailableModelsAsync();
    if (availableModels.Count == 0)
    {
        AnsiConsole.MarkupLine("[yellow]模型 API 回傳空清單，將使用設定模型。[/]");
    }
    else if (!Console.IsInputRedirected)
    {
        var choices = new Dictionary<string, string>(StringComparer.Ordinal);
        choices.Add($"保持目前設定 ({model})", model);
        foreach (var availableModel in availableModels)
        {
            if (!choices.ContainsValue(availableModel))
            {
                choices.Add(availableModel, availableModel);
            }
        }

        var selectedLabel = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold]選擇推論模型[/]")
                .PageSize(15)
                .AddChoices(choices.Keys));
        model = choices[selectedLabel];
    }
    else
    {
        AnsiConsole.MarkupLine(
            $"[grey]已從推論 API 取得 {availableModels.Count} 個模型；非互動輸入，沿用設定模型。[/]");
    }
}
catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException)
{
    AnsiConsole.MarkupLine(
        $"[yellow]無法從推論 API 取得模型清單，沿用設定模型 '{Markup.Escape(model)}'。原因: {Markup.Escape(ex.Message)}[/]");
}

var agentConfig = new AgentLoopConfig
{
    MaxIterations = ReadIntSetting("Agent:MaxIterations", 10),
    Temperature = ReadDoubleSetting("Agent:Temperature", 0.7),
    MaxTokens = ReadIntSetting("Agent:MaxTokens", 4000),
    ShowThinkingProcess = ReadBoolSetting("Agent:ShowThinkingProcess", false)
};

if (agentConfig.MaxIterations <= 0)
{
    throw new InvalidOperationException("設定 Agent:MaxIterations 必須大於 0。");
}

if (agentConfig.MaxTokens <= 0)
{
    throw new InvalidOperationException("設定 Agent:MaxTokens 必須大於 0。");
}

var apiKeyManager = new ApiKeyManager(Environment.GetEnvironmentVariable("SUPERAGENT_ENCRYPTION_KEY") ?? "default-encryption-key-0000000000000000");
apiKeyManager.AddKey("configured-key", apiKey);
var modelSelector = new ModelSelector();

// 顯示 API 密鑰狀態
AnsiConsole.MarkupLine("[bold]SuperAgent AI Agent CLI[/]");
AnsiConsole.MarkupLine($"[bold]Model:[/] {model}");
AnsiConsole.MarkupLine($"[bold]Endpoint:[/] {baseUrl}");
AnsiConsole.MarkupLine($"[bold]Max iterations:[/] {agentConfig.MaxIterations}");
AnsiConsole.MarkupLine($"[bold]Show thinking process:[/] {agentConfig.ShowThinkingProcess}");
AnsiConsole.MarkupLine($"[bold]API 密鑰數量:[/] {apiKeyManager.KeyCount} (活用: {apiKeyManager.ActiveKeyCount})");
AnsiConsole.MarkupLine("");

// 建立支援密鑰輪替的客戶端
var client = OpenAICompatClientFactory.CreateWithKeyRotation(
    baseUrl,
    apiKeyManager,
    modelSelector,
    model);

var registry = new ToolRegistry();
var markdownRenderer = new MarkdownRenderer();

// 註冊預設工具
registry.Register(new CodeWriterTool());
registry.Register(new CodeReaderTool());
registry.Register(new ShellExecTool());
registry.Register(new DatabaseTool());
registry.Register(new WebSearchTool());

// Sprint 8: 新增程式碼分析與除錯工具
registry.Register(new CodeAnalysisTool());
registry.Register(new TestGeneratorTool());
registry.Register(new DebuggerTool());

// Sprint 9: 新增程式碼重構建議工具
registry.Register(new RefactoringTool());

// Sprint 10: 效能監控與日誌工具
registry.Register(new PerformanceMonitor());
registry.Register(new LoggingTool());

// 工具註冊系統中定義但未註冊的工具
registry.Register(new FileSearchTool());
registry.Register(new GitTool());

var agent = new AgentLoop(client, registry, agentConfig);
if (agentConfig.ShowThinkingProcess)
{
    var streamedIterations = new HashSet<int>();
    agent.ResponseChunkReceived += (iteration, chunk) =>
    {
        if (streamedIterations.Add(iteration))
        {
            AnsiConsole.MarkupLine($"[bold cyan]模型處理過程 (串流，第 {iteration} 輪)[/]");
        }

        AnsiConsole.Markup(Markup.Escape(chunk));
    };
    agent.ToolExecutionStarted += toolCall =>
    {
        AnsiConsole.MarkupLine($"[bold yellow]▶ 執行工具: {Markup.Escape(toolCall.Name)}[/]");
        AnsiConsole.MarkupLine("[yellow]實際參數:[/]");
        AnsiConsole.WriteLine(JsonSerializer.Serialize(toolCall.Arguments, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    };
    agent.ToolExecutionCompleted += (toolCall, result) =>
    {
        AnsiConsole.MarkupLine($"[bold magenta]工具結果: {Markup.Escape(toolCall.Name)}[/]");
        AnsiConsole.WriteLine(result);
    };
}

while (true)
{
    AnsiConsole.Markup("[bold green]SuperAgent>[/] ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input))
        continue;

    if (input.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase) ||
        input.Trim().Equals("quit", StringComparison.OrdinalIgnoreCase) ||
        input.Trim().Equals("q", StringComparison.OrdinalIgnoreCase))
    {
        AnsiConsole.MarkupLine("[bold]Goodbye![/]");
        break;
    }

    if (input.Trim().Equals("help", StringComparison.OrdinalIgnoreCase))
    {
        AnsiConsole.MarkupLine("[bold]可用命令:[/]");
        AnsiConsole.MarkupLine("  - exit/quit/q: 結束程式");
        AnsiConsole.MarkupLine("  - help: 顯示此訊息");
        AnsiConsole.MarkupLine("");
        AnsiConsole.MarkupLine("[bold]請直接輸入你的任務指令，Agent 將自動思考與執行。[/]");
        AnsiConsole.MarkupLine("");
        continue;
    }

    AnsiConsole.MarkupLine("[bold]正在處理任務...[/]");
    AnsiConsole.MarkupLine("");

    try
    {
        var result = await agent.RunAsync(input);

        AnsiConsole.MarkupLine($"[bold]狀態:[/] {result.FinalState}");
        AnsiConsole.MarkupLine($"[bold]迭代次數:[/] {result.TotalIterations}");
        AnsiConsole.MarkupLine($"[bold]成功:[/] {result.Success}");

        if (!string.IsNullOrEmpty(result.Error))
            AnsiConsole.MarkupLine($"[red]錯誤:[/] {Markup.Escape(result.Error)}");

        AnsiConsole.MarkupLine("");
        AnsiConsole.MarkupLine("[bold green]最終回答:[/]");
        AnsiConsole.Write(markdownRenderer.Render(result.FinalContent).Root ?? Text.Empty);
        AnsiConsole.MarkupLine("");
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]發生錯誤:[/] {Markup.Escape(ex.Message)}");
        AnsiConsole.MarkupLine("");
    }
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
