# Sprint Backlog 6

## Sprint 目標
優化工具路由機制與錯誤分類，包含工具路由系統、錯誤分類與重試策略。

## 待辦事項 (Product Backlog Items)

| ID | 項目 | 優先級 | 估計工時 | 狀態 | 負責人 |
|----|------|--------|----------|------|--------|
| US-021 | 工具路由系統 | P1 | 4h | ✅ 完成 | 開發者3 |
| US-022 | 錯誤分類系統 | P1 | 4h | ✅ 完成 | 開發者1 |
| US-023 | 錯誤重試策略 | P1 | 3h | ✅ 完成 | 開發者2 |
| US-024 | Agent Loop 錯誤恢復 | P1 | 3h | ✅ 完成 | 開發者3 |

## 進行中的事項
- 無

## 已完成事項
- US-021: 工具路由系統完成 (ToolRouter.cs，支援關鍵字匹配與工具建議)
- US-022: 錯誤分類系統完成 (ErrorClassification.cs，定義 ErrorClassification 列舉與 ErrorClassifier)
- US-023: 錯誤重試策略完成 (IRetryStrategy 介面與四種策略實作)
- US-024: Agent Loop 錯誤恢復完成 (IntegrationTests 新增錯誤恢復測試)

## 待處理事項
- 無

## 阻礙清單
- Microsoft.Data.Sqlite 8.0.0 有安全性漏洞 (NU1903)，需後續處理
