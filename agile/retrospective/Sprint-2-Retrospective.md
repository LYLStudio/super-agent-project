# Sprint 2 回顧 (Retrospective)

## Sprint 週期
2026-11-04 至 2026-11-05

## 什麼做得好 (What Went Well)
1. **套件升級成功**: System.Text.Json 從 8.0.0 升級至 9.0.0，修復安全性漏洞。
2. **重試機制實作**: RetryPolicy 支援指數退避重試，增強系統健壯性。
3. **會話持續儲存**: ConversationStorage 支援會話歷史儲存與載入。
4. **新增工具**: FileSearchTool 與 GitTool 擴展工具系統。
5. **Agent Loop 更新**: 支援重試與會話儲存的 Agent Loop 實作完成。
6. **所有測試通過**: 8/8 單元測試通過。

## 什麼可以改善 (What Could Be Improved)
1. **命令列參數解析**: US-013 未完成，移至 Sprint 3。
2. **整合測試**: 尚未建立整合測試，建議在 Sprint 3 加入。
3. **錯誤處理測試**: 缺乏針對重試機制的專門測試。

## 行動項目 (Action Items)
| 項目 | 負責人 | 截止日期 |
|------|--------|----------|
| 實作命令列參數解析 (US-013) | 開發者2 | Sprint 3 |
| 建立整合測試 | 開發者3 | Sprint 3 |
| 增加重試機制測試 | 開發者2 | Sprint 3 |

## 下一個 Sprint 目標 (Sprint 3)
- 實作命令列參數解析 (--model, --api-key, --config)
- 建立整合測試
- 增加更多工具 (資料庫查詢、網頁爬蟲)
- 優化錯誤處理與日誌記錄
