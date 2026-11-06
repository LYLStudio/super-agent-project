using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SuperAgent.Core;

/// <summary>
/// ToolRegistry - 工具註冊與執行管理
/// </summary>
public class ToolRegistry : IToolRegistry
{
    private const int MaximumCachedResults = 256;
    private readonly Dictionary<string, ITool> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _results = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _resultOrder = new();
    private readonly Dictionary<string, LinkedListNode<string>> _resultNodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _resultsLock = new();
    private List<ToolDefinition> _toolDefinitions = new();

    /// <summary>
    /// 註冊工具
    /// </summary>
    public void Register(ITool tool)
    {
        _tools[tool.Name] = tool;
        UpdateToolDefinitions();
    }

    public bool Unregister(string name)
    {
        var removed = _tools.Remove(name);
        if (removed)
        {
            UpdateToolDefinitions();
        }

        return removed;
    }

    private void UpdateToolDefinitions()
    {
        _toolDefinitions = _tools.Values
            .Select(t => new ToolDefinition
            {
                Name = t.Name,
                Description = t.Description,
                Parameters = t.Parameters.Clone()
            })
            .ToList();
    }

    /// <summary>
    /// 取得所有工具定義
    /// </summary>
    public List<ToolDefinition> GetAllTools()
    {
        return _toolDefinitions
            .Select(t => new ToolDefinition
            {
                Name = t.Name,
                Description = t.Description,
                Parameters = t.Parameters
            })
            .ToList();
    }

    /// <summary>
    /// 非同步執行工具
    /// </summary>
    public async Task<string> ExecuteAsync(string toolName, JsonElement arguments, CancellationToken cancellationToken)
    {
        if (!_tools.TryGetValue(toolName, out var tool))
        {
            return $"Error: 工具 '{toolName}' 未找到";
        }

        return await tool.ExecuteAsync(arguments, cancellationToken);
    }

    /// <summary>
    /// 取得最後一個工具呼叫的結果
    /// </summary>
    public Task<string?> GetLastResultAsync(string toolCallId)
    {
        lock (_resultsLock)
        {
            _results.TryGetValue(toolCallId, out var result);
            _results.Remove(toolCallId);
            if (_resultNodes.Remove(toolCallId, out var node))
            {
                _resultOrder.Remove(node);
            }
            return Task.FromResult(result);
        }
    }

    /// <summary>
    /// 設定工具呼叫結果
    /// </summary>
    public void SetResult(string toolCallId, string result)
    {
        lock (_resultsLock)
        {
            if (!_results.ContainsKey(toolCallId))
            {
                var node = _resultOrder.AddLast(toolCallId);
                _resultNodes.Add(toolCallId, node);
            }
            _results[toolCallId] = result;

            while (_results.Count > MaximumCachedResults)
            {
                var oldest = _resultOrder.First!;
                _resultOrder.RemoveFirst();
                _resultNodes.Remove(oldest.Value);
                _results.Remove(oldest.Value);
            }
        }
    }
}

/// <summary>
/// CodeWriter 工具 - 撰寫程式碼文件
/// </summary>
public class CodeWriterTool : ITool
{
    private readonly WorkspaceAccessPolicy _accessPolicy;

    public CodeWriterTool(WorkspaceAccessPolicy? accessPolicy = null)
    {
        _accessPolicy = accessPolicy ?? WorkspaceAccessPolicy.CreateUnrestrictedDefault();
    }

    public string Name => "code_writer";
    public string Description => _accessPolicy.AllowOutsideWorkspace
        ? $"撰寫或覆寫程式碼文件。工作區根目錄：{_accessPolicy.WorkspaceRoot}。"
        : $"在工作區內撰寫或覆寫程式碼文件。工作區根目錄：{_accessPolicy.WorkspaceRoot}；相對路徑以此為準。";

    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            file_path = new { type = "string", description = "要撰寫的文件路徑" },
            content = new { type = "string", description = "程式碼內容" }
        },
        required = new[] { "file_path", "content" }
    });

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        string requestedPath = arguments.GetProperty("file_path").GetString() ?? string.Empty;
        string content = arguments.GetProperty("content").GetString() ?? string.Empty;

        try
        {
            string filePath = _accessPolicy.ResolvePath(requestedPath);
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(filePath, content, cancellationToken);
            return $"Successfully wrote {filePath} ({content.Length} bytes)";
        }
        catch (Exception ex)
        {
            return $"Error writing file: {ex.Message}";
        }
    }
}

/// <summary>
/// CodeReader 工具 - 讀取程式碼文件
/// </summary>
public class CodeReaderTool : ITool
{
    private readonly WorkspaceAccessPolicy _accessPolicy;

    public CodeReaderTool(WorkspaceAccessPolicy? accessPolicy = null)
    {
        _accessPolicy = accessPolicy ?? WorkspaceAccessPolicy.CreateUnrestrictedDefault();
    }

    public string Name => "code_reader";
    public string Description => _accessPolicy.AllowOutsideWorkspace
        ? $"讀取程式碼文件內容。工作區根目錄：{_accessPolicy.WorkspaceRoot}。"
        : $"讀取工作區內的程式碼文件。工作區根目錄：{_accessPolicy.WorkspaceRoot}；相對路徑以此為準。";

    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            file_path = new { type = "string", description = "要讀取的文件路徑" }
        },
        required = new[] { "file_path" }
    });

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        string requestedPath = arguments.GetProperty("file_path").GetString() ?? string.Empty;

        try
        {
            string filePath = _accessPolicy.ResolvePath(requestedPath);
            if (!File.Exists(filePath))
            {
                return $"Error: File not found: {filePath}";
            }

            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            return $"File content ({content.Length} bytes):\n{content}";
        }
        catch (Exception ex)
        {
            return $"Error reading file: {ex.Message}";
        }
    }
}

/// <summary>
/// ShellExec 工具 - 執行 shell 命令
/// </summary>
public class ShellExecTool : ITool
{
    private readonly WorkspaceAccessPolicy _accessPolicy;
    private readonly OutsideWorkspaceCommandAccess _outsideWorkspaceAccess;
    private readonly Func<string, CancellationToken, Task<bool>>? _confirmOutsideWorkspace;

    public ShellExecTool(
        string? workingDirectory = null,
        OutsideWorkspaceCommandAccess outsideWorkspaceAccess = OutsideWorkspaceCommandAccess.Deny,
        Func<string, CancellationToken, Task<bool>>? confirmOutsideWorkspace = null,
        WorkspaceAccessPolicy? accessPolicy = null)
    {
        _accessPolicy = accessPolicy ?? WorkspaceAccessPolicy.CreateUnrestrictedDefault();
        _outsideWorkspaceAccess = outsideWorkspaceAccess;
        _confirmOutsideWorkspace = confirmOutsideWorkspace;
        _defaultWorkingDirectory = workingDirectory ?? _accessPolicy.WorkspaceRoot;
    }

    private readonly string _defaultWorkingDirectory;

    public string Name => "shell_exec";
    public string Description =>
        $"在 {_accessPolicy.WorkspaceRoot} 工作區執行 shell 命令。可選 working_directory 指定工作區子目錄；工作區外目錄政策為 {_outsideWorkspaceAccess}。Shell 命令本身仍可指定其他檔案路徑。";

    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            command = new { type = "string", description = "要執行的 shell 命令" },
            timeout_seconds = new { type = "number", description = "超時秒數，預設 30" },
            working_directory = new { type = "string", description = "命令工作目錄，預設為工作區根目錄；相對路徑以工作區為基準" }
        },
        required = new[] { "command" }
    });

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        string command = arguments.GetProperty("command").GetString() ?? string.Empty;
        int timeout = arguments.TryGetProperty("timeout_seconds", out var timeoutProp)
            ? (int)timeoutProp.GetDouble()
            : 30;

        try
        {
            var requestedWorkingDirectory = arguments.TryGetProperty("working_directory", out var workingDirectoryProperty)
                ? workingDirectoryProperty.GetString()
                : _defaultWorkingDirectory;
            var workingDirectory = await _accessPolicy.ResolveCommandWorkingDirectoryAsync(
                requestedWorkingDirectory,
                _outsideWorkspaceAccess,
                _confirmOutsideWorkspace,
                cancellationToken);

            if (timeout <= 0)
            {
                return "Error executing command: timeout_seconds must be greater than zero";
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "bash",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory
            };
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(command);

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                return "Error: Unable to start process";
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCts.Token);

            try
            {
                await process.WaitForExitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
                var timedOutOutput = await outputTask;
                var timedOutError = await errorTask;
                var reason = cancellationToken.IsCancellationRequested
                    ? "cancelled"
                    : $"timed out after {timeout} seconds";
                return FormatProcessResult(
                    timedOutOutput,
                    timedOutError,
                    process.ExitCode,
                    $"Command {reason}");
            }

            var output = await outputTask;
            var error = await errorTask;
            return FormatProcessResult(output, error, process.ExitCode);
        }
        catch (Exception ex)
        {
            return $"Error executing command: {ex.Message}";
        }
    }

    private static string FormatProcessResult(
        string output,
        string error,
        int exitCode,
        string? status = null)
    {
        var result = new StringBuilder();
        if (!string.IsNullOrEmpty(status))
        {
            result.AppendLine($"{status}.");
        }
        if (!string.IsNullOrEmpty(output))
        {
            result.AppendLine($"STDOUT:{output}");
        }
        if (!string.IsNullOrEmpty(error))
        {
            result.AppendLine($"STDERR:{error}");
        }
        result.AppendLine($"Exit code: {exitCode}");
        return result.ToString();
    }
}

/// <summary>
/// FileSearchTool 工具 - 搜尋檔案內容
/// </summary>
public class FileSearchTool : ITool
{
    private const int DefaultMaxResults = 20;
    private const int MaximumResults = 100;
    private const int MaximumScannedFiles = 1000;
    private const long MaximumFileBytes = 1_048_576;
    private readonly WorkspaceAccessPolicy _accessPolicy;

    public FileSearchTool(WorkspaceAccessPolicy? accessPolicy = null)
    {
        _accessPolicy = accessPolicy ?? WorkspaceAccessPolicy.CreateUnrestrictedDefault();
    }

    public string Name => "file_search";
    public string Description => _accessPolicy.AllowOutsideWorkspace
        ? $"搜尋檔案內容，支援關鍵字與路徑篩選。工作區根目錄：{_accessPolicy.WorkspaceRoot}。"
        : $"在工作區內搜尋檔案內容，支援關鍵字與路徑篩選。工作區根目錄：{_accessPolicy.WorkspaceRoot}；相對路徑以此為準。";

    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            directory = new { type = "string", description = "要搜尋的目錄路徑" },
            pattern = new { type = "string", description = "搜尋關鍵字或檔案模式" },
            file_extension = new { type = "string", description = "檔案副檔名（例如 .cs, .txt）" },
            max_results = new { type = "integer", description = "最大回傳結果數，範圍 1 到 100 (預設 20)" }
        },
        required = new[] { "directory", "pattern" }
    });

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        string requestedDirectory = arguments.GetProperty("directory").GetString() ?? _accessPolicy.WorkspaceRoot;
        string pattern = arguments.GetProperty("pattern").GetString() ?? string.Empty;
        string fileExtension = arguments.TryGetProperty("file_extension", out var extProp)
            ? extProp.GetString() ?? string.Empty
            : string.Empty;
        int maxResults = DefaultMaxResults;
        if (arguments.TryGetProperty("max_results", out var maxProp) &&
            (!maxProp.TryGetInt32(out maxResults) || maxResults is < 1 or > MaximumResults))
        {
            return $"Error: max_results 必須介於 1 到 {MaximumResults}";
        }

        if (string.IsNullOrWhiteSpace(pattern))
        {
            return "Error: 搜尋關鍵字不能為空";
        }

        try
        {
            string directory = _accessPolicy.ResolvePath(requestedDirectory);
            if (!Directory.Exists(directory))
            {
                return $"Error: Directory not found: {directory}";
            }

            var results = new List<string>();
            var scannedFiles = 0;
            var skippedFiles = 0;
            var scanLimitReached = false;
            var resultLimitReached = false;

            foreach (var file in Directory.EnumerateFiles(directory, "*" + fileExtension, SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (scannedFiles == MaximumScannedFiles)
                {
                    scanLimitReached = true;
                    break;
                }

                scannedFiles++;
                try
                {
                    _accessPolicy.ResolvePath(file);
                    if (new FileInfo(file).Length > MaximumFileBytes)
                    {
                        skippedFiles++;
                        continue;
                    }

                    var content = await File.ReadAllTextAsync(file, cancellationToken);
                    if (content.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(file);
                        if (results.Count >= maxResults)
                        {
                            resultLimitReached = true;
                            break;
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (IOException)
                {
                    skippedFiles++;
                }
                catch (UnauthorizedAccessException)
                {
                    skippedFiles++;
                }
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"搜尋結果: {pattern}");
            sb.AppendLine($"找到 {results.Count} 個文件:");
            sb.AppendLine($"已掃描 {scannedFiles} 個文件，略過 {skippedFiles} 個無法讀取或超過 1 MB 的文件。");
            if (scanLimitReached)
            {
                sb.AppendLine($"已達到掃描上限 ({MaximumScannedFiles} 個文件)，結果可能不完整。");
            }
            if (resultLimitReached)
            {
                sb.AppendLine($"已達到結果上限 ({maxResults} 個文件)，可能還有符合結果未列出。");
            }
            sb.AppendLine();

            foreach (var file in results)
            {
                sb.AppendLine($"  - {file}");
            }

            return sb.ToString();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return $"Error searching files: {ex.Message}";
        }
    }
}

/// <summary>
/// GitTool 工具 - Git 版本控制工具
/// </summary>
public class GitTool : ITool
{
    private readonly WorkspaceAccessPolicy _accessPolicy;
    private readonly OutsideWorkspaceCommandAccess _outsideWorkspaceAccess;
    private readonly Func<string, CancellationToken, Task<bool>>? _confirmOutsideWorkspace;

    public GitTool(
        string? workingDirectory = null,
        OutsideWorkspaceCommandAccess outsideWorkspaceAccess = OutsideWorkspaceCommandAccess.Deny,
        Func<string, CancellationToken, Task<bool>>? confirmOutsideWorkspace = null,
        WorkspaceAccessPolicy? accessPolicy = null)
    {
        _accessPolicy = accessPolicy ?? WorkspaceAccessPolicy.CreateUnrestrictedDefault();
        _outsideWorkspaceAccess = outsideWorkspaceAccess;
        _confirmOutsideWorkspace = confirmOutsideWorkspace;
        _defaultWorkingDirectory = workingDirectory ?? _accessPolicy.WorkspaceRoot;
    }

    private readonly string _defaultWorkingDirectory;

    public string Name => "git_tool";
    public string Description =>
        $"在 {_accessPolicy.WorkspaceRoot} 工作區執行 Git 命令。可選 working_directory 指定工作區子目錄；工作區外目錄政策為 {_outsideWorkspaceAccess}。";

    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            command = new { type = "string", description = "Git 命令 (例如 status, log, diff)" },
            args = new { type = "string", description = "額外參數" },
            working_directory = new { type = "string", description = "Git 工作目錄，預設為工作區根目錄；相對路徑以工作區為基準" }
        },
        required = new[] { "command" }
    });

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        string command = arguments.GetProperty("command").GetString() ?? string.Empty;
        string args = arguments.TryGetProperty("args", out var argsProp)
            ? argsProp.GetString() ?? string.Empty
            : string.Empty;

        if (string.IsNullOrWhiteSpace(command))
            return "Error: Git 命令不能為空";

        try
        {
            var fullCommand = $"git {command} {args}".Trim();
            if (Regex.IsMatch(
                    fullCommand,
                    @"(?:^|\s)(?:-C\S*|--git-dir(?:=|\s|$)|--work-tree(?:=|\s|$))",
                    RegexOptions.CultureInvariant))
            {
                return "Error: Git path override options (-C, --git-dir, --work-tree) are not supported; use working_directory instead.";
            }

            var requestedWorkingDirectory = arguments.TryGetProperty("working_directory", out var workingDirectoryProperty)
                ? workingDirectoryProperty.GetString()
                : _defaultWorkingDirectory;
            var workingDirectory = await _accessPolicy.ResolveCommandWorkingDirectoryAsync(
                requestedWorkingDirectory,
                _outsideWorkspaceAccess,
                _confirmOutsideWorkspace,
                cancellationToken);

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = fullCommand,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory
            };

            System.Diagnostics.Process? process = System.Diagnostics.Process.Start(startInfo);
            if (process is null)
            {
                return "Error: Unable to start git process";
            }

            await process.WaitForExitAsync(cancellationToken);

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);

            var result = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(output))
            {
                result.AppendLine($"STDOUT:\n{output}");
            }
            if (!string.IsNullOrEmpty(error))
            {
                result.AppendLine($"STDERR:\n{error}");
            }
            result.AppendLine($"Exit code: {process.ExitCode}");

            return result.ToString();
        }
        catch (Exception ex)
        {
            return $"Error executing git command: {ex.Message}";
        }
    }
}

/// <summary>
/// WebSearchTool - 網頁搜尋工具，支援搜尋引擎與頁面內容擷取
/// </summary>
public class WebSearchTool : ITool
{
    private const string BingSearchEndpoint = "https://www.bing.com/search";
    private const string DuckDuckGoSearchEndpoint = "https://html.duckduckgo.com/html/";
    private const int MaximumResults = 10;
    private static readonly HttpClient SharedHttpClient = CreateHttpClient();
    private static readonly Regex BingResultRegex = new(
        @"<li\b(?=[^>]*\bclass\s*=\s*[""'][^""']*\bb_algo\b)[^>]*>(?<content>.*?)</li\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex BingTitleRegex = new(
        @"<h2\b[^>]*>\s*<a\b(?<attributes>[^>]*)>(?<content>.*?)</a\s*>\s*</h2\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex ParagraphRegex = new(
        @"<p\b[^>]*>(?<content>.*?)</p\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex AnchorRegex = new(
        @"<a\b(?<attributes>[^>]*)>(?<content>.*?)</a\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex AttributeRegex = new(
        @"(?<name>[\w:-]+)\s*=\s*(?:""(?<double>[^""]*)""|'(?<single>[^']*)'|(?<unquoted>[^\s>]+))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TagRegex = new(
        @"<[^>]+>",
        RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(
        @"\s+",
        RegexOptions.Compiled);
    private readonly HttpClient _httpClient;

    public WebSearchTool() : this(SharedHttpClient)
    {
    }

    public WebSearchTool(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public string Name => "web_search";

    public string Description => "透過 Bing 搜尋真實網頁，並在無法使用時改用 DuckDuckGo，回傳標題、網址與搜尋摘要。適用於查詢資訊、查找文件與取得網頁內容。";

    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            query = new { type = "string", description = "搜尋查詢字串" },
            max_results = new { type = "integer", description = "最大結果數量，範圍 1 到 10 (預設 5)" },
            extract_content = new { type = "boolean", description = "是否顯示搜尋引擎提供的頁面摘要 (預設 true)" }
        },
        required = new[] { "query" }
    });

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        if (!arguments.TryGetProperty("query", out var queryProperty) ||
            queryProperty.ValueKind != JsonValueKind.String)
        {
            return "Error: 搜尋查詢必須是文字";
        }

        var query = queryProperty.GetString() ?? string.Empty;
        var maxResults = 5;
        if (arguments.TryGetProperty("max_results", out var maxProp) &&
            (!maxProp.TryGetInt32(out maxResults) || maxResults is < 1 or > MaximumResults))
        {
            return $"Error: max_results 必須介於 1 到 {MaximumResults}";
        }

        var extractContent = true;
        if (arguments.TryGetProperty("extract_content", out var extractProp))
        {
            if (extractProp.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return "Error: extract_content 必須是布林值";
            }

            extractContent = extractProp.GetBoolean();
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return "Error: 搜尋查詢不能為空";
        }

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            using var searchCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCts.Token);
            var searchAttempts = new List<string>();
            foreach (var provider in new[]
            {
            (Name: "Bing", Endpoint: BingSearchEndpoint, Parse: (Func<string, int, List<SearchResult>>)ParseBingResults),
            (Name: "DuckDuckGo", Endpoint: DuckDuckGoSearchEndpoint, Parse: (Func<string, int, List<SearchResult>>)ParseResults)
            })
            {
            try
            {
                var requestUri = $"{provider.Endpoint}?q={Uri.EscapeDataString(query)}";
                using var response = await _httpClient.GetAsync(
                    requestUri,
                    HttpCompletionOption.ResponseHeadersRead,
                    searchCts.Token);
                response.EnsureSuccessStatusCode();

                var html = await response.Content.ReadAsStringAsync(searchCts.Token);
                if (IsVerificationChallenge(provider.Name, html))
                {
                    searchAttempts.Add($"{provider.Name} returned a verification challenge");
                    continue;
                }

                var results = provider.Parse(html, maxResults);
                if (results.Count == 0)
                {
                    searchAttempts.Add($"{provider.Name} returned no parseable results");
                    continue;
                }

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"搜尋查詢: {query} (來源: {provider.Name})");
                sb.AppendLine($"找到 {results.Count} 個結果:");
                sb.AppendLine();

                for (var i = 0; i < results.Count; i++)
                {
                    var result = results[i];
                    sb.AppendLine($"[{i + 1}] {result.Title}");
                    sb.AppendLine($"    網址: {result.Url}");
                    if (extractContent && !string.IsNullOrWhiteSpace(result.Snippet))
                    {
                        sb.AppendLine($"    摘要: {result.Snippet}");
                    }
                    sb.AppendLine();
                }

                return sb.ToString();
            }
            catch (HttpRequestException ex)
            {
                searchAttempts.Add($"{provider.Name}: {ex.Message}");
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                searchAttempts.Add($"{provider.Name} timed out: {ex.Message}");
            }
            }

            return searchAttempts.Count > 0
            ? $"Error executing web search: all search providers failed ({string.Join("; ", searchAttempts)})."
            : $"搜尋查詢: {query}\n未找到搜尋結果。";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (compatible; SuperAgent/1.0; +https://github.com/LYLStudio/super-agent-project)");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html");
        return client;
    }

    private static List<SearchResult> ParseResults(string html, int maxResults)
    {
        var results = new List<SearchResult>();
        var snippets = new List<string>();
        foreach (Match match in AnchorRegex.Matches(html))
        {
            var attributes = ParseAttributes(match.Groups["attributes"].Value);
            if (!attributes.TryGetValue("class", out var classNames))
            {
                continue;
            }

            var content = NormalizeHtmlText(match.Groups["content"].Value);
            if (classNames.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("result__snippet"))
            {
                snippets.Add(content);
                continue;
            }

            if (!classNames.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("result__a") ||
                !attributes.TryGetValue("href", out var href))
            {
                continue;
            }

            var url = ResolveResultUrl(href);
            if (url is null || string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            results.Add(new SearchResult(content, url, string.Empty));
        }

        for (var i = 0; i < results.Count && i < snippets.Count; i++)
        {
            results[i] = results[i] with { Snippet = snippets[i] };
        }

        return results.Take(maxResults).ToList();
    }

    private static List<SearchResult> ParseBingResults(string html, int maxResults)
    {
        var results = new List<SearchResult>();
        foreach (Match resultMatch in BingResultRegex.Matches(html))
        {
            var resultHtml = resultMatch.Groups["content"].Value;
            var titleMatch = BingTitleRegex.Match(resultHtml);
            if (!titleMatch.Success)
            {
                continue;
            }

            var attributes = ParseAttributes(titleMatch.Groups["attributes"].Value);
            if (!attributes.TryGetValue("href", out var href))
            {
                continue;
            }

            var url = ResolveBingResultUrl(href);
            var title = NormalizeHtmlText(titleMatch.Groups["content"].Value);
            if (url is null || string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var snippetMatch = ParagraphRegex.Match(resultHtml);
            var snippet = snippetMatch.Success
                ? NormalizeHtmlText(snippetMatch.Groups["content"].Value)
                : string.Empty;
            results.Add(new SearchResult(title, url, snippet));
            if (results.Count == maxResults)
            {
                break;
            }
        }

        return results;
    }

    private static string? ResolveBingResultUrl(string href)
    {
        if (!Uri.TryCreate(new Uri("https://www.bing.com"), href, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        if (!uri.Host.Equals("www.bing.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.Equals("/ck/a", StringComparison.OrdinalIgnoreCase))
        {
            return uri.AbsoluteUri;
        }

        var encodedDestination = uri.Query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(parameter => parameter.Split('=', 2))
            .FirstOrDefault(parts => parts.Length == 2 && parts[0] == "u")?
            .ElementAtOrDefault(1);
        if (encodedDestination is null || !encodedDestination.StartsWith("a1", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            var base64 = encodedDestination[2..]
                .Replace('-', '+')
                .Replace('_', '/');
            base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
            var destination = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            return Uri.TryCreate(destination, UriKind.Absolute, out var resultUri) &&
                   resultUri.Scheme is "http" or "https"
                ? resultUri.AbsoluteUri
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static bool IsVerificationChallenge(string provider, string html)
    {
        return provider == "DuckDuckGo"
            ? html.Contains("anomaly-modal", StringComparison.OrdinalIgnoreCase)
            : html.Contains("b_captcha", StringComparison.OrdinalIgnoreCase) ||
              html.Contains("captcha", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> ParseAttributes(string attributes)
    {
        var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in AttributeRegex.Matches(attributes))
        {
            var value = match.Groups["double"].Success
                ? match.Groups["double"].Value
                : match.Groups["single"].Success
                    ? match.Groups["single"].Value
                    : match.Groups["unquoted"].Value;
            parsed[match.Groups["name"].Value] = System.Net.WebUtility.HtmlDecode(value);
        }

        return parsed;
    }

    private static string? ResolveResultUrl(string href)
    {
        if (!Uri.TryCreate(new Uri("https://duckduckgo.com"), href, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        if (uri.Host.EndsWith("duckduckgo.com", StringComparison.OrdinalIgnoreCase) &&
            uri.AbsolutePath.Equals("/l/", StringComparison.OrdinalIgnoreCase))
        {
            var destination = uri.Query
                .TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(parameter => parameter.Split('=', 2))
                .FirstOrDefault(parts => parts.Length == 2 && parts[0] == "uddg");
            if (destination is null ||
                !Uri.TryCreate(Uri.UnescapeDataString(destination[1]), UriKind.Absolute, out uri))
            {
                return null;
            }
        }

        return uri.Scheme is "http" or "https" ? uri.AbsoluteUri : null;
    }

    private static string NormalizeHtmlText(string html)
    {
        var withoutTags = TagRegex.Replace(html, " ");
        var decoded = System.Net.WebUtility.HtmlDecode(withoutTags);
        return WhitespaceRegex.Replace(decoded, " ").Trim();
    }

    private sealed record SearchResult(string Title, string Url, string Snippet);
}
