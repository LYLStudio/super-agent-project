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
| **監控與日誌** | 效能監控 (時間/記憶體)、結構化多級別日誌系統 |
| **部署支援** | Docker 容器化、GitHub Actions CI/CD 流程 |

## 安裝與建置

### 1. 使用 Docker 建置（推薦）

```bash
# 建置 Docker 映像
docker build -t superagent .

# 執行容器
docker run -it \
  -e OPENAI_API_KEY="your-api-key" \
  -e OPENAI_MODEL="qwen3-coder:30b" \
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

## 命令列參數

| 參數 | 說明 | 預設值 |
|------|------|--------|
| `--model=NAME` | 使用的模型名稱 | `qwen3-coder:30b` |
| `--api-key=KEY` | API 密鑰 | `dummy` |
| `--config=PATH` | 設定檔路徑 | `appsettings.json` |
| `--base-url=URL` | API 端點 URL | `http://localhost:8080` |
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
| `performance_monitor` | 效能監控工具 | 記錄執行時間與資源使用量 |
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
監控 API 呼叫的執行時間，並記錄相關日誌
```

**效能監控輸出**:
```
=== 效能監控報告 ===
操作: api_call
執行次數: 1

=== 時間效能指標 ===
總執行時間: 45 ms
平均執行時間: 45.00 ms

=== 效能評估 ===
⚠️ 可接受: 執行時間 10ms - 100ms
```

**日誌記錄輸出**:
```
[Info] 2026-11-03T14:30:00Z [ApiClient] API 呼叫開始
[Info] 2026-11-03T14:30:01Z [ApiClient] API 呼叫完成，耗時 45ms
```

## 配置檔範例 (appsettings.json)

```json
{
  "App": {
    "ApiKey": "your-api-key-here",
    "Model": "qwen3-coder:30b",
    "BaseUrl": "http://localhost:8080"
  },
  "Logging": {
    "Level": "Info",
    "Path": "logs/superagent.log"
  }
}
```

## 環境變數設定

| 變數名稱 | 說明 | 範例 |
|----------|------|------|
| `OPENAI_API_KEY` | OpenAI 相容 API 密鑰 | `sk-your-key-here` |
| `OPENAI_MODEL` | 使用的模型 | `qwen3-coder:30b` |
| `OPENAI_BASE_URL` | API 端點 URL | `http://localhost:8080` |
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
