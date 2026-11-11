# SuperAgent 工具插件系統開發指南

## 系統概述

SuperAgent 工具插件系統支援動態載入外部工具模組，讓開發人員可以無需重新編譯主程式即可新增功能。插件透過實作 `IToolPlugin` 介面來擴充系統能力。

## 插件架構

```
SuperAgent.Core/
├── IToolPlugin.cs          # 插件介面定義
├── PluginContext.cs        # 插件上下文環境
├── PluginLoader.cs         # 插件載入與管理
└── ToolRegistry.cs         # 工具註冊表 (已新增插件支援)

SuperAgent.Console/
└── Program.cs              # 應用程式入口 (初始化插件系統)
```

## 核心介面：IToolPlugin

`IToolPlugin` 繼承自 `ITool`，新增以下成員：

```csharp
public interface IToolPlugin : ITool
{
    new string Name { get; }           // 插件名稱
    new string Description { get; }    // 插件描述
    string Version { get; }            // 插件版本
    string Author { get; }             // 插件作者
    
    Task<bool> InitializeAsync(PluginContext context, CancellationToken cancellationToken);
    Task<bool> UnloadAsync(CancellationToken cancellationToken);
}
```

## PluginContext

提供插件運行所需的環境與服務：

```csharp
public class PluginContext
{
    public IToolRegistry ToolRegistry { get; }     // 工具註冊表
    public IAgentClient AgentClient { get; }       // 代理客戶端
    public string PluginDirectory { get; }         // 插件目錄路徑
    public string BasePath { get; }                # 應用程式基礎路徑
}
```

## 建立第一個插件的步驟

### 步驟 1：建立類別庫專案

```bash
dotnet new classlib -n MyToolPlugin -o ./plugins/MyToolPlugin
```

### 步驟 2：新增 SuperAgent.Core 參考

編輯 `MyToolPlugin.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  
  <ItemGroup>
    <Reference Include="../src/SuperAgent.Core/SuperAgent.Core.csproj" />
  </ItemGroup>
</Project>
```

### 步驟 3：實作插件類別

建立 `MyCustomTool.cs`：

```csharp
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SuperAgent.Core;

namespace MyToolPlugin;

/// <summary>
/// 我的自訂工具插件
/// </summary>
public class MyCustomTool : IToolPlugin
{
    public string Name => "my_custom_tool";
    public string Description => "我的自訂工具，支援特定業務邏輯";
    public string Version => "1.0.0";
    public string Author => "開發者姓名";

    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            input = new { type = "string", description = "輸入內容" },
            operation = new { type = "string", description = "操作類型" }
        },
        required = new[] { "input" }
    });

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var input = arguments.GetProperty("input").GetString() ?? string.Empty;
        var operation = arguments.GetProperty("operation").GetString() ?? "default";

        // 執行插件邏輯
        var result = ProcessInput(input, operation);

        return JsonSerializer.Serialize(new
        {
            success = true,
            result = result,
            timestamp = DateTime.UtcNow.ToString("o")
        });
    }

    public async Task<bool> InitializeAsync(PluginContext context, CancellationToken cancellationToken)
    {
        // 插件初始化：註冊工具、設定等
        context.ToolRegistry.Register(this);
        
        Console.WriteLine($"插件 {Name} v{Version} 已初始化");
        return true;
    }

    public async Task<bool> UnloadAsync(CancellationToken cancellationToken)
    {
        // 插件卸載：清理資源
        Console.WriteLine($"插件 {Name} 已卸載");
        return true;
    }

    private string ProcessInput(string input, string operation)
    {
        // 實作您的業務邏輯
        return $"處理結果: {input} (操作: {operation})";
    }
}
```

### 步驟 4：編譯插件

```bash
dotnet build ./plugins/MyToolPlugin/MyToolPlugin.csproj
```

編譯後會產生 `MyToolPlugin.dll` 檔案。

### 步驟 5：複製到插件目錄

將編譯後的 DLL 複製到主程式的 `plugins` 目錄：

```bash
# 假設主程式輸出目錄為 src/SuperAgent.Console/bin/Debug/net10.0/
cp ./plugins/MyToolPlugin/bin/Debug/net10.0/MyToolPlugin.dll \
   ./src/SuperAgent.Console/bin/Debug/net10.0/plugins/
```

## 插件目錄結構

```
src/SuperAgent.Console/bin/Debug/net10.0/
├── SuperAgent.dll
├── SuperAgent.Core.dll
├── SuperAgent.Client.dll
├── plugins/                    # 插件目錄
│   ├── MyToolPlugin.dll
│   ├── AnotherPlugin.dll
│   └── ...
└── appsettings.json
```

## 插件載入流程

主程式 (`Program.cs`) 在啟動時會自動執行：

1. 建立 `PluginLoader` 實例，指向 `plugins` 目錄
2. 建立 `PluginContext`，傳遞工具註冊表與代理客戶端
3. 呼叫 `LoadAllPluginsAsync` 掃描目錄中的所有 `.dll` 檔案
4. 對每個檔案：
   - 載入組件 (`Assembly.LoadFrom`)
   - 查找實作 `IToolPlugin` 的類別
   - 建立實例並呼叫 `InitializeAsync`
   - 初始化成功後註冊到 `ToolRegistry`
5. 顯示載入的插件清單

## 插件開發最佳實務

### 1. 錯誤處理

```csharp
public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
{
    try
    {
        // 您的邏輯
        return await DoWorkAsync(arguments, cancellationToken);
    }
    catch (Exception ex)
    {
        // 回傳錯誤訊息，不拋出異常
        return JsonSerializer.Serialize(new
        {
            success = false,
            error = ex.Message,
            stackTrace = ex.StackTrace
        });
    }
}
```

### 2. 線程安全

```csharp
private readonly object _lock = new();

public async Task<bool> InitializeAsync(PluginContext context, CancellationToken cancellationToken)
{
    lock (_lock)
    {
        // 安全地存取共享資源
    }
    return true;
}
```

### 3. 資源清理

```csharp
public async Task<bool> UnloadAsync(CancellationToken cancellationToken)
{
    // 清理併用資源、斷開連線等
    await CleanupResourcesAsync();
    return true;
}
```

### 4. 版本控管

```csharp
public string Version => "2.1.0";

// 插件載入後可檢查版本相容性
if (plugin.Version.StartsWith("1."))
{
    // 處理舊版本插件
}
```

## 插件配置與管理

### 啟用/停用插件

在 `Program.cs` 的 `ApplyToolConfiguration` 方法中，可透過設定檔控制插件是否啟用：

```csharp
// appsettings.json
{
  "Agent": {
    "Tools": {
      "my_custom_tool": true,      // 啟用插件
      "another_plugin": false      // 停用插件
    }
  }
}
```

### 插件生命週期

```
1. 載入 (Load)        -> Assembly.LoadFrom
2. 初始化 (Initialize) -> 註冊到 ToolRegistry
3. 執行 (Execute)      -> Agent 呼叫工具
4. 卸載 (Unload)       -> 清理資源
```

## 範例：實作完整插件

以下是一個支援檔案搜尋的插件範例：

```csharp
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SuperAgent.Core;

namespace FileSearchPlugin;

public class FileSearchTool : IToolPlugin
{
    public string Name => "file_searcher";
    public string Description => "在指定路徑搜尋檔案";
    public string Version => "1.0.0";
    public string Author => "SuperAgent Team";

    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            pattern = new { type = "string", description = "搜尋模式 (檔案名稱)" },
            path = new { type = "string", description = "搜尋路徑 (預設為工作區)" },
            recursive = new { type = "boolean", description = "是否遞迴搜尋" }
        },
        required = new[] { "pattern" }
    });

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var pattern = arguments.GetProperty("pattern").GetString() ?? "*";
        var path = arguments.GetProperty("path").GetString() ?? Directory.GetCurrentDirectory();
        var recursive = arguments.GetProperty("recursive").GetBoolean();

        try
        {
            var files = Directory.GetFiles(
                path,
                pattern,
                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly
            );

            var result = new
            {
                pattern,
                path,
                count = files.Length,
                files = files.Select(f => new { name = Path.GetFileName(f), fullPath = f })
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = ex.Message
            });
        }
    }

    public async Task<bool> InitializeAsync(PluginContext context, CancellationToken cancellationToken)
    {
        context.ToolRegistry.Register(this);
        return true;
    }

    public async Task<bool> UnloadAsync(CancellationToken cancellationToken)
    {
        return true;
    }
}
```

## 除錯與監控

### 查看載入的插件

主程式啟動時會顯示：

```
已載入 2 個工具插件
  - my_custom_tool v1.0.0 (作者: 開發者姓名)
  - file_searcher v1.0.0 (作者: SuperAgent Team)
```

### 插件錯誤處理

插件載入失敗不會阻止主程式啟動，錯誤會寫入控制台：

```
插件載入失敗: ./plugins/BrokenPlugin.dll - System.TypeLoadException: ...
```

### 程式碼分析工具檢查插件

使用 `code_analyzer` 工具檢查插件程式碼品質：

```json
{
  "code": "...您的插件程式碼...",
  "language": "csharp",
  "analysisType": "all"
}
```

## 部署建議

1. **插件目錄隔離**：將插件放在獨立目錄，避免與主程式檔案混雜
2. **版本管理**：插件 DLL 應包含版本資訊，便於追蹤
3. **安全檢查**：載入插件前建議檢查程式碼，避免惡意程式碼
4. **日誌記錄**：插件初始化與卸載時應記錄日誌

## 常見問題

### Q: 插件無法載入？

A: 檢查以下項目：
- DLL 是否編譯為 net10.0
- 是否正確實作 `IToolPlugin` 介面
- 是否有 `SuperAgent.Core` 參考
- 是否複製到正確的 `plugins` 目錄

### Q: 插件執行後沒有反應？

A: 檢查：
- 工具名稱是否正確註冊
- 參數是否正確傳遞
- 插件是否在 `Agent:Tools` 設定中啟用

### Q: 插件與主程式衝突？

A: 避免：
- 載入與主程式相同名稱的組件
- 修改靜態全域變數
- 使用反射載入主程式類別

## 貢獻指引

若要為 SuperAgent 新增內建插件：

1. 在 `src/SuperAgent.Core/` 新增插件類別檔案
2. 在 `Program.cs` 的 `ApplyToolConfiguration` 中註冊插件
3. 新增單元測試至 `tests/SuperAgent.Core.Tests/`
4. 更新 `docs/developer/Plugin-System-Guide.md`

---

此指南將持續更新，歡迎開發人員提出改善建議。