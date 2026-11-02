# Sprint 1: 專案基礎架構與核心實作

## Sprint 目標
建立 SuperAgent AI Agent CLI 的基礎架構，包含：
- 專案解決方案與所有專案設定
- Agent Loop 核心系統（思考、規劃、執行、觀察迴圈）
- OpenAI 相容 API 客戶端
- 預設工具系統（代碼撰寫、讀取、Shell 執行）
- CLI 入口與基礎測試

## Sprint 週期
- 開始日期: 2026-11-02
- 結束日期: 2026-11-03
- 持續時間: 1 天

## User Stories

### US-001: 建立專案結構與設定
**優先級**: P0 (必須)
**描述**: 建立 .NET 10 專案解決方案，包含 Core、Client、Console 與測試專案。
**驗收標準**:
- [ ] SuperAgent.sln 解決方案檔正確
- [ ] SuperAgent.Core 專案包含核心類別
- [ ] SuperAgent.Client 專案包含 OpenAI 相容客戶端
- [ ] SuperAgent.Console 專案包含 CLI 入口
- [ ] 測試專案設定正確

### US-002: 實作 Agent Loop 核心系統
**優先級**: P0 (必須)
**描述**: 實作強悍的 Agent Loop，包含思考、規劃、執行與觀察迴圈。
**驗收標準**:
- [ ] AgentLoopState 列舉定義所有狀態
- [ ] AgentLoop 類別實作 RunAsync 方法
- [ ] 循環包含思考 → 規劃 → 執行 → 觀察步驟
- [ ] 支援最大迭代次數限制
- [ ] 生成系統提示詞

### US-003: 實作 OpenAI 相容 API 客戶端
**優先級**: P0 (必須)
**描述**: 實作相容 OpenAI 規格的 API 客戶端，支援 qwen3-coder:30b 模型。
**驗收標準**:
- [ ] OpenAICompatClient 實作 IAgentClient 介面
- [ ] 支援 POST /chat/completions 端點
- [ ] 處理 tool_calls 回應
- [ ] 支援自訂 base URL、API 密鑰與模型名稱

### US-004: 實作工具註冊系統與預設工具
**優先級**: P0 (必須)
**描述**: 實作工具註冊系統，包含代碼撰寫、讀取與 Shell 執行工具。
**驗收標準**:
- [ ] ToolRegistry 實作 IToolRegistry 介面
- [ ] CodeWriterTool 支援撰寫程式碼文件
- [ ] CodeReaderTool 支援讀取程式碼文件
- [ ] ShellExecTool 支援執行 shell 命令
- [ ] 工具具備 JSON Schema 參數定義

### US-005: 實作 CLI 入口
**優先級**: P1 (重要)
**描述**: 實作命令列介面，支援對話式互動與 Agent Loop 執行。
**驗收標準**:
- [ ] 使用 Spectre.Console 實作互動式 CLI
- [ ] 支援 exit/quit/q 命令結束
- [ ] 支援 help 命令顯示說明
- [ ] 顯示 Agent Loop 執行狀態與迭代次數

### US-006: 建立基礎測試
**優先級**: P1 (重要)
**描述**: 建立 Core 與 Client 專案的單元測試。
**驗收標準**:
- [ ] AgentLoopTests 包含狀態、訊息、工具呼叫、設定測試
- [ ] ToolRegistryTests 包含註冊與執行測試
- [ ] AgentClientTests 包含客戶端建立測試
- [ ] 所有測試通過

## 技術任務
- [ ] 建立 .NET 10 專案結構 (Core, Client, Console, Tests)
- [ ] 設定 NuGet 套件 (System.Text.Json, Spectre.Console, xunit)
- [ ] 實作 AgentLoop.cs 與相關類別
- [ ] 實作 OpenAICompatClient.cs
- [ ] 實作 ToolRegistry.cs 與預設工具
- [ ] 實作 Program.cs CLI 入口
- [ ] 建立單元測試
- [ ] 驗證所有專案編譯與測試通過

## 風險與依賴
- 依賴 NuGet 套件版本相容性
- OpenAI 相容 API 端點可用性（測試使用 Mock）
- Shell 執行工具的安全性考慮

## 驗收標準 (Definition of Done)
- [ ] 所有專案編譯成功 (0 錯誤)
- [ ] 所有單元測試通過
- [ ] 程式碼符合 .NET 10 標準與命名慣例
- [ ] 文件完整且有適當的 XML 註解
