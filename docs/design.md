# 設計文件

## 1. Agent Loop 設計

Agent Loop 是系統的核心，實作以下迴圈：

```
思考 (Thinking) → 規劃 (Planning) → 執行 (Acting) → 觀察 (Observing) → 回到思考
```

### 思考階段
- 呼叫 AI API 取得回應
- 回應可能包含文字內容與/或 tool_calls

### 規劃階段
- 決定哪些工具需要執行
- 設定執行順序

### 執行階段
- 透過 IToolRegistry 執行選定的工具
- 每個工具執行結果被儲存

### 觀察階段
- 將工具結果加入對話歷史
- 回到思考階段繼續處理

## 2. 工具系統設計

### 工具介面 (ITool)
```csharp
public interface ITool
{
    string Name { get; }
    string Description { get; }
    JsonElement Parameters { get; }  // JSON Schema
    Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken);
}
```

### 預設工具

| 工具名稱 | 描述 | 參數 |
|----------|------|------|
| code_writer | 撰寫程式碼文件 | file_path, content |
| code_reader | 讀取程式碼文件 | file_path |
| shell_exec | 執行 shell 命令 | command, timeout_seconds |

## 3. API 客戶端設計

OpenAICompatClient 支援以下端點:
- POST /chat/completions

請求格式相容 OpenAI API:
```json
{
  "model": "qwen3-coder:30b",
  "messages": [...],
  "tools": [...],
  "temperature": 0.7,
  "max_tokens": 4000
}
```

## 4. 設定管理

支援透過環境變數與 appsettings.json 設定:
- OPENAI_BASE_URL: API 端點
- OPENAI_API_KEY: API 密鑰
- OPENAI_MODEL: 模型名稱 (預設: qwen3-coder:30b)

## 5. 錯誤處理

- API 請求失敗：拋出例外
- 工具執行失敗：回傳錯誤訊息字串
- Agent Loop 超過最大迭代次數：標記為 Failed 狀態
