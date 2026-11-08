# SuperAgent AI Agent CLI

[![Build Status](https://img.shields.io/github/actions/workflow/status/your-org/super-agent/ci/main?label=CI)](https://github.com/your-org/super-agent/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/your-org/super-agent.svg)](https://github.com/your-org/super-agent/releases)
[![License](https://img.shields.io/github/license/your-org/super-agent)](LICENSE)

## 專案概覽

**SuperAgent AI Agent CLI** 是一個支援多模型 AI 代理人的命令列工具。它整合了程式碼分析、重構建議、效能監控與日誌記錄等功能，讓開發者能透過自然語言指令自動執行複雜的程式開發任務。

## 功能特色

| 類別 | 功能 |
|------|------|
| **核心系統** | Agent Loop 核心迴圈、對話會話儲存、錯誤處理與重試機制 |
| **工具系統** | 14 個內建 ITool 工具 (程式碼、檔案、Shell、資料庫、搜尋等) |
| **API 管理** | AES-256 加密密鑰儲存、多模型自動選擇、密鑰輪替 |
| **開發能力** | 程式碼品質分析、自動測試生成、除錯輔助、重構建議 |
| **監控與日誌** | 效能監控 (時間/記憶體)、預設啟用的結構化日誌檔與自動輪替 |
| **部署支援** | Docker 容器化、GitHub Actions CI/CD 流程 |

## 安裝與建置

### 1. 使用 Docker 建置（推薦）

```bash
# 建置 Docker 映像
docker build -t superagent .

# 執行容器
docker run -it \
  -e OPENAI_API_KEY="your-api-key" \
  -e OPENAI_MODEL="gpt-oss:20b" \
  -e SUPERAGENT_ENCRYPTION_KEY="your-encryption-key" \
  superagent
```

### 2. 本地建置與執行

```bash
# 建置專案
dotnet build src/SuperAgent.Console/SuperAgent.Console.csproj -c Release

# 執行應用程式
dotnet run --project src/SuperAgent.Console -- --help
```

### 3. 原生 C++ 版本（獨立專案）

原生版本由 `cpp/` 下的 CMake 專案獨立管理，不加入 `SuperAgent.sln`；.NET 專案仍由原 solution 管理。需要 CMake 與 C++17 編譯器；HTTPS 需安裝 OpenSSL 開發套件，未安裝時仍可連線 HTTP 端點。CMake 會下載固定版本的 JSON 與 HTTP 相依套件。

```bash
cmake -S cpp -B build/cpp -DCMAKE_BUILD_TYPE=Release
cmake --build build/cpp --config Release
ctest --test-dir build/cpp --output-on-failure
```

執行原生版本：

```bash
OPENAI_API_KEY="your-api-key" build/cpp/superagent-cpp
```

原生版互動流程已對齊 .NET 主控台：全螢幕自適應輸出、多行提示編輯/歷史、模型文字串流、工具呼叫/結果分段、任務狀態摘要，以及 F2 設定、F3 日誌、F5 熱重載。設定可讀取 .NET `appsettings.json` 命名格式，會話以 .NET 相容格式保存；模型選擇可讀取端點模型清單。原生版另包含檔案讀寫/搜尋、程式碼分析、測試樣板、除錯/重構建議、效能量測、唯讀 SQLite/選配 localhost ODBC 查詢、安全 Shell 命令子集、Git 查詢與需確認的本地變更及結構化輪替日誌。網路搜尋、遠端資料庫連線及破壞性/遠端 Git 操作未開放。因安全界線，C++ Shell/Git 執行政策仍比 .NET 版嚴格；詳細限制與 Windows/macOS 建置方式請見 [cpp/README.md](cpp/README.md)。

互動啟動會使用全螢幕自適應介面：狀態列固定在頂端，快捷鍵與多行輸入固定在底部，中央輸出區會依終端視窗大小即時重排，僅在內容超出可視範圍時顯示右側捲軸。使用 `PageUp`／`PageDown` 捲動輸出；多行或自動折行的提示中，上下鍵會依可視行移動，游標到達首／末行時可瀏覽已送出的提示歷史。輸出會以不同顏色與分段標題區分模型串流、工具執行／結果與最終回答，最終回答以純文字呈現，不套用 Markdown 排版。按 `F2` 開啟設定，`F3` 查看並篩選最近 50 筆日誌（結果顯示在可捲動輸出區），`F5` 熱重載設定。設定修改會保存至目前設定檔（可用 `--config=PATH` 指定），返回對話時立即套用；進入設定時會清除主畫面，避免顯示先前終端命令。命令列參數與環境變數仍會覆蓋設定檔值；API 密鑰會以明文寫入設定檔，請妥善保護檔案權限。若模型未回傳文字，介面會顯示提示。

底部提示區支援多行編輯：`Enter` 換行，`F4` 或 `Ctrl+Enter` 送出；上下鍵在多行或自動折行提示中移動可視行，到達首／末行時瀏覽本次執行期間已送出的提示，左右鍵移動游標，Home／End 移至目前行首／行尾。也支援 Backspace、Delete 與 Ctrl+A／Ctrl+E；視窗尺寸變更時畫面會自動重排。

`web_search` 工具會優先透過 Bing 搜尋即時網頁，遇到驗證頁或無法解析結果時會改用 DuckDuckGo，回傳標題、連結與搜尋摘要；不需要額外 API 金鑰，但執行環境必須能連線至網際網路。

## 命令列參數

| 參數 | 說明 | 預設值 |
|------|------|--------|
| `--model=NAME` | 使用的模型名稱 | `gpt-oss:20b` |
| `--api-key=KEY` | API 密鑰 | `dummy` |
| `--config=PATH` | 設定檔路徑 | `appsettings.json` |
| `--base-url=URL` | OpenAI 相容 API 端點 URL | `http://192.168.1.102:11434/v1` |
| `--help, -h` | 顯示說明 | - |

## 可用工具清單

SuperAgent 內建 14 個工具，可透過自然語言指令自動選擇與執行：

| 工具名稱 | 說明 | 主要功能 |
|----------|------|----------|
| `code_writer` | 程式碼撰寫工具 | 撰寫或覆寫程式碼文件 |
| `code_reader` | 程式碼讀取工具 | 讀取程式碼文件內容 |
| `shell_exec` | Shell 執行工具 | 執行系統命令 |
| `database_tool` | 資料庫查詢工具 | 執行 SQL 查詢 |
| `web_search_tool` | 網頁搜尋工具 | 搜尋網頁內容 |
| `code_analyzer` | 程式碼分析工具 | 檢測品質問題與潛在風險 |
| `test_generator` | 測試生成工具 | 自動產生 xunit 單元測試 |
| `debugger` | 除錯輔助工具 | 分析錯誤並提供修復建議 |
| `refactoring` | 重構建議工具 | 提供命名、方法抽取、類別拆分建議 |
| `performance_monitor` | 複雜度分析工具 | 量測分析操作時間與程序記憶體；不會執行輸入程式碼 |
| `logging` | 日誌工具 | 結構化多級別日誌記錄 |

## 使用情境示範

### 情境 1：分析並改善現有程式碼

```bash
# 在 CLI 中輸入
請分析 src/SuperAgent.Core/PerformanceMonitor.cs 的程式碼品質，並提供改善建議
```

**Agent 回應**:
1. 自動使用 `code_reader` 工具讀取文件
2. 使用 `code_analyzer` 工具檢測品質問題
3. 提供具體的改善建議

**預期輸出**:
```
=== 程式碼品質分析結果 ===
程式語言: csharp
程式碼長度: 2200 字元
行數: 215 行

=== 發現的問題 ===
⚠️ 第 45 行: 行長過長 (135 字元 > 120)
⚠️ 第 78 行: 發現長字符串常數，建議提取為變數

=== 改善建議 ===
💡 考慮使用 nullable 類型提升型別安全性
💡 檢查是否有適當的錯誤處理 (try-catch)
💡 考慮將大型方法拆分為較小的單一職責方法
💡 編寫單元測試以確保程式碼正確性
```

### 情境 2：自動產生單元測試

```bash
# 在 CLI 中輸入
為 src/SuperAgent.Core/Calculator.cs 裡的所有方法產生 xunit 單元測試
```

**生成的測試代碼範例**:
```csharp
using System;
using Xunit;
using System.Threading;
using System.Threading.Tasks;

namespace SuperAgent.Core.Tests;

public class CalculatorTests
{
    [Fact]
    public void Add_Test()
    {
        // Arrange
        var calculator = new Calculator();

        // Act
        // TODO: 請根據方法簽名填入正確的參數
        // var result = calculator.Add();

        // Assert
        // TODO: 請根據預期結果填入斷言
        // Assert.True(result);
    }
}
```

### 情境 3：錯誤分析與修復

```bash
# 在 CLI 中輸入
我遇到這個錯誤：System.NullReferenceException: 物件參考未設定為物件實例。
請分析這個錯誤並提供修復建議。
```

**Agent 回應**:
```
=== 錯誤分析結果 ===
錯誤訊息: System.NullReferenceException: 物件參考未設定為物件實例。

錯誤類型: NullReferenceException - 空引用錯誤

=== 修復建議 ===
🔧 檢查變數是否已初始化
🔧 使用 null 檢查 (?:) 或 null 條件運算符
🔧 在使用前檢查物件是否為 null
```

### 情境 4：程式碼重構建議

```bash
# 在 CLI 中輸入
分析 src/SuperAgent.Core/Processor.cs 類別，提供重構建議
```

**Agent 回應**:
```
=== 程式碼重構分析結果 ===
程式語言: csharp
程式碼長度: 5000 字元
行數: 150 行

=== 發現的重構機會 ===
1. 類別包含 25 個方法，超過建議數量 (10)，建議拆分為多個類別
2. 檢測到多個職責 (儲存、載入、驗證)，建議拆分為多個類別

=== 一般重構建議 ===
1. 將大型方法拆分為較小的單一職責方法
2. 改善變數與方法命名，使其更具描述性
3. 移除重複程式碼，提取為共用方法
4. 將過大的類別拆分為多個職責明確的類別
```

### 情境 5：效能監控與日誌記錄

```bash
# 在 CLI 中輸入
分析程式碼片段的複雜度並記錄分析耗時
```

**效能監控輸出**:
```
=== 效能監控報告 ===
操作: code_review
分析次數: 1
複雜度分數: 2
注意: 時間指標量測的是複雜度分析操作，不代表輸入程式碼的實際執行效能。

=== 分析耗時指標 ===
總分析時間: 0.123 ms
平均分析時間: 0.123 ms
```

**日誌記錄輸出**:
```
[Info] 2026-11-03T14:30:00Z [ApiClient] API 呼叫開始
[Info] 2026-11-03T14:30:01Z [ApiClient] API 呼叫完成，耗時 45ms
```

## 配置檔範例 (appsettings.json)

```json
{
  "OpenAI": {
    "BaseUrl": "http://192.168.1.102:11434/v1",
    "ApiKey": "dummy",
    "Model": "gpt-oss:20b",
    "RequestTimeoutSeconds": 120
  },
  "Agent": {
    "MaxIterations": 20,
    "MaxTokens": 8000,
    "MaxContextCharacters": 32000,
    "ShowThinkingProcess": false,
    "DefaultSystemPrompt": "優先提供精簡且可執行的回答。",
    "Region": "台灣",
    "Language": "繁體中文（台灣）",
    "WorkspaceRoot": ".",
    "AllowOutsideWorkspace": false,
    "OutsideWorkspaceCommandAccess": "Deny",
    "Tools": {
      "code_writer": true,
      "code_reader": true,
      "shell_exec": true,
      "database_query": true,
      "web_search": true,
      "code_analyzer": true,
      "test_generator": true,
      "debugger": true,
      "refactoring": true,
      "performance_monitor": true,
      "logging": true,
      "file_search": true,
      "git_tool": true
    }
  },
  "Logging": {
    "Enabled": true,
    "Path": "logs/superagent.log",
    "RollingInterval": "Day",
    "FileSizeLimitMB": 10,
    "Level": "Info"
  }
}
```

`appsettings.json` 會隨建置與發布複製到輸出目錄；程式預設從該設定檔讀取連線資訊。環境變數與命令列參數可覆寫設定值。
`Agent.WorkspaceRoot` 定義 Agent 工作區根目錄，預設為啟動程式時的目前目錄。`Agent.AllowOutsideWorkspace` 預設為 `false`，控制檔案讀取、寫入和搜尋是否能存取工作區外路徑。Shell/Git 工具只由 `Agent.Tools.shell_exec` 和 `Agent.Tools.git_tool` 個別開關控制，預設可在工作區根目錄與其子目錄執行。
`Agent.DefaultSystemPrompt` 可放入額外的系統預設指示；`Agent.Region` 與 `Agent.Language` 分別指定地區脈絡與回覆語系，預設為「台灣」與「繁體中文（台灣）」。上述設定也可於 `F2` 設定選單修改並保存；系統會在每次請求組合這些值，地區及語系變更會套用到後續對話。系統預設提示詞支援多行編輯，輸入 `:save` 儲存，輸入 `:clear` 清除。
`Agent.OutsideWorkspaceCommandAccess` 控制 Shell/Git 的 `working_directory` 指向工作區外目錄時的行為，支援 `Deny`（預設拒絕）、`Confirm`（互動時每次詢問，非互動模式一律拒絕）及 `Allow`。`working_directory` 未指定時使用工作區根目錄；相對路徑以工作區根目錄為基準。可在對話按 `F2` →「工作區外命令存取」修改此設定。這項政策限制工具設定的工作目錄，不是完整命令沙箱：Shell 命令本身仍可切換目錄或存取其他路徑；Git 工具會拒絕 `-C`、`--git-dir` 和 `--work-tree` 路徑覆寫選項。啟用命令工具前請確認 Agent 指令與模型可信。
可在 `Agent.Tools` 以工具名稱設定個別工具是否啟用，未指定時預設啟用。也可在對話按 `F2` →「管理工具啟用狀態」切換；設定會保存並立即更新工具清單。
日誌記錄預設啟用，`Logging.Path` 指定日誌檔案基底路徑；程式啟動、Agent 請求及工具執行等應用事件會自動記錄（不包含使用者提示內容或工具參數）。程式會依 `Logging.RollingInterval`（`Day` 每日或 `Hour` 每小時）與 `Logging.FileSizeLimitMB` 大小上限，自動建立帶 UTC 時間及序號的日誌檔。達到大小上限後，下一筆紀錄會寫入同一時段的下一個分段檔案；舊檔不會自動刪除。可在 `F2` 設定中開關記錄、調整切檔週期及檔案大小上限，並按 `F3` 按時間倒序瀏覽最近 50 筆紀錄或輸入文字篩選。僅設定日誌啟用但未有紀錄，先前是因為只在模型主動呼叫 `logging` 工具時才落檔；現在已加入應用程式事件記錄。
將 `Agent.ShowThinkingProcess` 設為 `true` 可串流顯示模型輸出的處理訊息，並列出工具呼叫的實際參數與執行結果；模型處理訊息以青色、工具呼叫與結果以黃色／紫紅色、最終回答以綠色區分。此處顯示的是模型輸出的訊息，不是隱藏的內部推理內容。預設為 `false`。也可用環境變數 `Agent__ShowThinkingProcess=true` 覆寫。
每個模型請求逾時預設為 120 秒，可透過 `OpenAI__RequestTimeoutSeconds` 設定。非互動模式不會在啟動時查詢模型清單；互動模式的模型選擇查詢最多等待 10 秒，失敗時會沿用設定模型。Agent 每回合最多 20 次迭代，對話上下文軟預算預設為 32000 字元（保留最新完整回合、裁切較長的非使用者訊息，較早回合超出預算時省略）。檔案搜尋最多掃描 1000 個檔案、略過大於 1 MiB 的檔案；資料庫回傳預設限制為 100 列／20000 字元，超過時會提示截斷。

## 環境變數設定

| 變數名稱 | 說明 | 範例 |
|----------|------|------|
| `OPENAI_API_KEY` | OpenAI 相容 API 密鑰 | `sk-your-key-here` |
| `OPENAI_MODEL` | 使用的模型 | `gpt-oss:20b` |
| `OPENAI_BASE_URL` | OpenAI 相容 API 端點 URL | `http://192.168.1.102:11434/v1` |
| `OpenAI__RequestTimeoutSeconds` | API 請求逾時秒數 | `120` |
| `Agent__DefaultSystemPrompt` | 額外系統預設提示詞 | `請以精簡方式回答` |
| `Agent__Region` | 回覆使用的地區脈絡 | `台灣` |
| `Agent__Language` | 回覆使用的語系 | `繁體中文（台灣）` |
| `Agent__MaxContextCharacters` | 對話上下文預算字元數 | `32000` |
| `Agent__WorkspaceRoot` | Agent 工作區根目錄 | `.` |
| `Agent__AllowOutsideWorkspace` | 是否允許檔案工具存取工作區外路徑 | `false` |
| `Agent__OutsideWorkspaceCommandAccess` | Shell/Git 工作目錄在工作區外時的政策：`Deny`、`Confirm` 或 `Allow` | `Deny` |
| `Logging__Enabled` | 是否啟用日誌檔案記錄 | `true` |
| `Logging__RollingInterval` | 日誌時間切檔週期：`Day` 或 `Hour` | `Day` |
| `Logging__FileSizeLimitMB` | 單一日誌分段檔案大小上限 (MiB) | `10` |
| `SUPERAGENT_ENCRYPTION_KEY` | API 密鑰加密密鑰 | `your-encryption-key-32chars` |
| `SUPERAGENT_API_KEY` | SuperAgent API 密鑰 | `superagent-key` |

## CI/CD 流程

### GitHub Actions CI 流程

專案已內建完整的 CI/CD 流程：

1. **CI 流程** (`.github/workflows/ci.yml`):
   - 程式碼建置
   - 單元測試執行
   - 代碼品質檢查
   - 測試結果上傳

2. **Release 流程** (`.github/workflows/release.yml`):
   - 標記觸發時自動建置與發布
   - NuGet 套件發布

### Docker 容器化

```bash
# 執行完整環境 (包含 API 伺服器)
docker-compose up -d

# 查看服務狀態
docker-compose ps

# 查看日誌
docker-compose logs -f superagent
```

## 最佳實務建議

### 程式碼開發流程

```
需求分析
    ↓
透過 SuperAgent 描述任務
    ↓
Agent 自動選擇適當工具執行
    ↓
檢查與修改生成的程式碼
    ↓
使用 code_analyzer 檢查品質
    ↓
使用 test_generator 產生測試
    ↓
執行測試驗證
    ↓
使用 refactoring 優化程式碼
    ↓
提交程式碼
```

### 錯誤處理流程

```
遇到錯誤
    ↓
使用 debugger 分析錯誤類型
    ↓
根據修復建議修改程式碼
    ↓
使用 performance_monitor 驗證效能
    ↓
使用 logging 記錄修復過程
```

### 安全性最佳實務

- 使用 `SUPERAGENT_ENCRYPTION_KEY` 加密 API 密鑰
- 定期輪替 API 密鑰
- 將敏感資訊放入環境變數而非硬編碼
- 使用 `.gitignore` 排除設定檔與日誌

## 故障排除

| 問題 | 可能原因 | 解決方案 |
|------|----------|----------|
| 連線 API 失敗 | 錯誤的 API 密鑰或端點 | 檢查 `OPENAI_API_KEY` 與 `OPENAI_BASE_URL` |
| 工具未執行 | 工具名稱錯誤 | 檢查工具名稱是否正確 (例如 `code_analyzer`) |
| 程式碼品質分析無結果 | 程式碼太簡單 | 確保程式碼有足夠的複雜度 |
| 效能監控時間為 0 | 操作太快 | 增加 `iterations` 參數或複雜度 |
| Docker 容器無法啟動 | 權限問題 | 檢查 Docker 權限與環境變數 |

## 支援與貢獻

- **問題報告**: GitHub Issues
- **功能請求**: GitHub Discussions
- **程式碼貢獻**: 請先 Fork 並提交 Pull Request

## 版本歷史

| 版本 | 日期 | 主要特色 |
|------|------|----------|
| v1.0.0 | 2026-11-03 | Sprint 1-6 完成：核心系統與工具系統 |
| v1.1.0 | 2026-11-03 | Sprint 7 完成：API 密鑰管理與多模型支援 |
| v1.2.0 | 2026-11-03 | Sprint 8 完成：程式碼分析與除錯工具 |
| v1.3.0 | 2026-11-03 | Sprint 9 完成：重構建議與 Docker 容器化 |
| v1.4.0 | 2026-11-03 | Sprint 10 完成：CI/CD 流程與效能監控 |

## 專案結構

```
super-agent/
├── README.md
├── Dockerfile
├── docker-compose.yml
├── .github/
│   └── workflows/
│       ├── ci.yml
│       └── release.yml
├── agile/
│   ├── user-stories/
│   ├── sprint-planning/
│   ├── sprint-backlog/
│   └── retrospective/
├── src/
│   ├── SuperAgent.Core/
│   │   ├── AgentLoop.cs
│   │   ├── ApiKeyManager.cs
│   │   ├── ModelSelector.cs
│   │   ├── ToolRegistry.cs
│   │   ├── CodeAnalysisTool.cs
│   │   ├── TestGeneratorTool.cs
│   │   ├── DebuggerTool.cs
│   │   ├── RefactoringTool.cs
│   │   ├── PerformanceMonitor.cs
│   │   ├── LoggingTool.cs
│   │   └── ...
│   ├── SuperAgent.Client/
│   │   └── OpenAICompatClient.cs
│   └── SuperAgent.Console/
│       └── Program.cs
└── tests/
    ├── SuperAgent.Core.Tests/
    │   ├── ApiKeyManagerTests.cs
    │   ├── ModelSelectorTests.cs
    │   ├── CodeAnalysisToolTests.cs
    │   ├── TestGeneratorToolTests.cs
    │   ├── DebuggerToolTests.cs
    │   ├── RefactoringToolTests.cs
    │   ├── PerformanceMonitorTests.cs
    │   ├── LoggingToolTests.cs
    │   └── ...
    └── SuperAgent.Client.Tests/
        └── AgentClientTests.cs
```

## 授權

本專案採用 MIT 授權。查看 [LICENSE](LICENSE) 文件以取得詳細資訊。

---

**SuperAgent AI Agent CLI** - 讓 AI 代理人協助你的程式開發工作流程，從程式碼分析到部署，一站式完成！
