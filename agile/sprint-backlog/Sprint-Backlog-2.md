# Sprint Backlog 2

## Sprint 目標
強化系統的健壯性與擴展性，包含錯誤處理、重試機制與新增工具。

## 待辦事項 (Product Backlog Items)

| ID | 項目 | 優先級 | 估計工時 | 狀態 | 負責人 |
|----|------|--------|----------|------|--------|
| US-007 | 實作對話會話持續儲存 | P1 | 4h | ✅ 完成 | 開發者1 |
| US-008 | 實作 Agent Loop 錯誤處理與重試機制 | P1 | 4h | ✅ 完成 | 開發者2 |
| US-009 | 增加檔案搜尋工具 | P1 | 3h | ✅ 完成 | 開發者3 |
| US-010 | 增加 Git 操作工具 | P1 | 3h | ✅ 完成 | 開發者1 |
| US-013 | 實作命令列參數解析 | P1 | 3h | ⬜ 待處理 (下一 Sprint) | 開發者2 |
| 技術債 | 升級 System.Text.Json | P0 | 2h | ✅ 完成 | 開發者3 |
| 技術債 | 確認 Spectre.Console 版本 | P0 | 1h | ✅ 完成 | 開發者3 |

## 進行中的事項
- 無

## 已完成事項
- US-007: 對話會話持續儲存完成 (ConversationStorage.cs)
- US-008: 錯誤處理與重試機制完成 (RetryPolicy.cs + AgentLoop.cs 更新)
- US-009: 檔案搜尋工具完成 (FileSearchTool)
- US-010: Git 操作工具完成 (GitTool)
- 技術債: System.Text.Json 升級至 9.0.0 完成
- 技術債: Spectre.Console 0.57.0 確認完成

## 待處理事項
- US-013: 命令列參數解析 (移至 Sprint 3)

## 阻礙清單
- 無
