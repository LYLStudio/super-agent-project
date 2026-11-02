# SuperAgent 架構設計文件

## 1. 概述

SuperAgent 是一個原生 .NET 10 AI Agent CLI，採用 OpenAI 相容 API 規格。系統具備強悍的 Agent Loop 能力，可以有序地思考、規劃、執行與觀察工具，最終達成自主的任務執行。

## 2. 架構圖

```
使用者
   │
   ▼
[CLI 入口] SuperAgent.Console
   │ (Spectre.Console)
   ▼
[Agent Loop] SuperAgent.Core.AgentLoop
   │
   ├─► [思考] 呼叫 IAgentClient.GetResponseAsync
   │
   ├─► [工具] 透過 IToolRegistry 執行工具
   │       ├─ CodeWriterTool (撰寫程式碼)
   │       ├─ CodeReaderTool (讀取程式碼)
   │       └─ ShellExecTool (執行 shell)
   │
   └─► [觀察] 處理工具結果，回到思考階段

   │
   ▼
[OpenAI 相容客戶端] SuperAgent.Client.OpenAICompatClient
   │ (HTTP + JSON)
   ▼
[OpenAI 相容 API] (支援 qwen3-coder:30b)
```

## 3. 模組設計

### 3.1 SuperAgent.Core
包含核心邏輯與型別定義：
- `AgentLoop` - 主體迴圈，協調思考、規劃、執行、觀察
- `AgentLoopState` - 執行狀態列舉
- `AgentMessage` - 對話消息型別
- `ToolCall` - 工具呼叫型別
- `AgentLoopResult` - 執行結果型別
- `AgentLoopConfig` - 迴圈設定
- `IAgentClient` - AI 客戶端介面
- `IToolRegistry` - 工具註冊介面
- `ITool` - 工具介面
- `ToolRegistry` - 工具註冊實作
- 預設工具: `CodeWriterTool`, `CodeReaderTool`, `ShellExecTool`

### 3.2 SuperAgent.Client
包含 API 客戶端實作：
- `OpenAICompatClient` - 實作 IAgentClient，支援 OpenAI 相容 API
- `OpenAICompatClientFactory` - 客戶端工廠

### 3.3 SuperAgent.Console
包含 CLI 入口：
- `Program.cs` - 主程式，支援互動式對話

## 4. 資料流程

```
1. 使用者輸入任務指令
2. AgentLoop 初始化系統提示詞與使用者訊息
3. 循環開始 (最大 10 次迭代):
   a. 思考: 呼叫 IAgentClient 取得 AI 回應 (可能包含 tool_calls)
   b. 如果有 tool_calls:
      - 規劃: 決定執行工具
      - 執行: 透過 IToolRegistry 執行工具
      - 觀察: 將工具結果加入對話歷史
   c. 如果無 tool_calls:
      - 完成: 回應使用者
4. 回傳最終結果
```

## 5. 設計原則

- **解耦**: Agent Loop 與 API 客戶端透過介面分離
- **可擴展**: 工具系統支援動態註冊新工具
- **型別安全**: 使用 .NET 10 的 nullable 類型與記錄
- **非同步**: 全程使用 async/await 模式
- **錯誤處理**: 工具執行錯誤會被捕捉並回傳錯誤訊息

## 6. 安全性考慮

- ShellExecTool 執行任意 shell 命令存在安全風險
- 建議在生產環境中限制 ShellExecTool 使用權
- API 密鑰應透過環境變數設定，不應硬編碼
- System.Text.Json 已升級至 9.0.0 修復安全性漏洞
