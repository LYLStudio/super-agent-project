using Spectre.Console;
using SuperAgent.Core;
using SuperAgent.Client;
using Microsoft.Extensions.Configuration;

var model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "qwen3-coder:30b";
var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "dummy";
var baseUrl = Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? "http://localhost:8080";
var configPath = Environment.GetEnvironmentVariable("SUPERAGENT_CONFIG") ?? "appsettings.json";

// 解析命令列參數
foreach (var arg in args)
{
    if (arg.StartsWith("--model=", StringComparison.OrdinalIgnoreCase))
    {
        model = arg.Substring(arg.IndexOf('=') + 1);
    }
    else if (arg.StartsWith("--api-key=", StringComparison.OrdinalIgnoreCase))
    {
        apiKey = arg.Substring(arg.IndexOf('=') + 1);
    }
    else if (arg.StartsWith("--config=", StringComparison.OrdinalIgnoreCase))
    {
        configPath = arg.Substring(arg.IndexOf('=') + 1);
    }
    else if (arg.StartsWith("--base-url=", StringComparison.OrdinalIgnoreCase))
    {
        baseUrl = arg.Substring(arg.IndexOf('=') + 1);
    }
    else if (arg.Equals("--help", StringComparison.OrdinalIgnoreCase) || arg.Equals("-h", StringComparison.OrdinalIgnoreCase))
    {
        AnsiConsole.MarkupLine("[bold]SuperAgent AI Agent CLI[/]");
        AnsiConsole.MarkupLine("");
        AnsiConsole.MarkupLine("用法: SuperAgent [選項]");
        AnsiConsole.MarkupLine("");
        AnsiConsole.MarkupLine("[bold]選項:[/]");
        AnsiConsole.MarkupLine("  --model=NAME  使用的模型名稱 (預設: qwen3-coder:30b)");
        AnsiConsole.MarkupLine("  --api-key=KEY  API 密鑰 (預設: dummy)");
        AnsiConsole.MarkupLine("  --config=PATH  設定檔路徑 (預設: appsettings.json)");
        AnsiConsole.MarkupLine("  --base-url=URL  API 端點 URL (預設: http://localhost:8080)");
        AnsiConsole.MarkupLine("  --help, -h  顯示此說明");
        AnsiConsole.MarkupLine("");
        return;
    }
}

// 載入設定檔 (如果有)
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile(configPath, optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .Build();

if (configuration["App:Model"] is string m)
{
    model = m;
}

if (configuration["App:ApiKey"] is string key)
{
    apiKey = key;
}

if (configuration["App:BaseUrl"] is string url)
{
    baseUrl = url;
}

AnsiConsole.MarkupLine("[bold]SuperAgent AI Agent CLI[/]");
AnsiConsole.MarkupLine($"[bold]Model:[/] {model}");
AnsiConsole.MarkupLine($"[bold]Endpoint:[/] {baseUrl}");
AnsiConsole.MarkupLine("");

var client = OpenAICompatClientFactory.Create(baseUrl, apiKey, model);
var registry = new ToolRegistry();

// 註冊預設工具
registry.Register(new CodeWriterTool());
registry.Register(new CodeReaderTool());
registry.Register(new ShellExecTool());

var agent = new AgentLoop(client, registry);

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
            AnsiConsole.MarkupLine($"[red]錯誤:[/] {result.Error}");

        AnsiConsole.MarkupLine("");
        AnsiConsole.MarkupLine("[bold]最終回應:[/]");
        AnsiConsole.MarkupLine(result.FinalContent);
        AnsiConsole.MarkupLine("");
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]發生錯誤:[/] {ex.Message}");
        AnsiConsole.MarkupLine("");
    }
}