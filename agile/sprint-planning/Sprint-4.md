# Sprint 4: 工具擴展與監控 (Tools Extension & Monitoring)

## Sprint 目標
擴充工具系統與監控能力，包含：
- 新增資料庫查詢工具 (DatabaseTool)
- 新增網頁搜尋工具 (WebSearchTool)
- 新增日誌記錄機制
- 改善錯誤處理與監控
- 更新架構文件記錄新工具

## Sprint 週期
- 開始日期: 2026-11-07
- 結束日期: 2026-11-08
- 持續時間: 1 天

## User Stories

### US-014: 新增資料庫查詢工具
**優先級**: P1 (重要)
**描述**: 新增支援 SQLite、MySQL、PostgreSQL 的資料庫查詢工具。
**驗收標準**:
- [ ] 錯誤處理與連接字串安全性
- [ ] 執行 SQL 查詢並回傳結果
- [ ] 支援參數化查詢防止 SQL 注入

### US-015: 新增網頁搜尋工具
**優先級**: P1 (重要)
**描述**: 新增支援搜尋引擎的網頁搜尋工具。
**驗收標準**:
- [ ] 執行搜尋查詢並回傳結果
- [ ] 支援擷取網頁內容
- [ ] 錯誤處理與超時管理

### US-016: 日誌記錄機制
**優先級**: P1 (重要)
**描述**: 新增結構化日誌記錄，支援 Agent Loop 執行追蹤。
**驗收標準**:
- [ ] 記錄 Agent Loop 執行過程
- [ ] 記錄工具執行結果
- [ ] 支援日誌層級 (Debug, Info, Warn, Error)

### 技術債: 架構文件更新
**優先級**: P0 (高)
**描述**: 更新 docs/architecture.md 記錄新工具與監控能力。
**驗收標準**:
- [ ] 更新工具列表記錄 DatabaseTool 與 WebSearchTool
- [ ] 新增監控與日誌章節
- [ ] 更新資料流程圖

## 技術任務
- [ ] 建立 DatabaseTool (SQLite 實作)
- [ ] 建立 WebSearchTool (模擬搜尋引擎)
- [ ] 建立 Logger 服務與介面
- [ ] 更新 ToolRegistry 新工具註冊
- [ ] 更新 AgentLoop 支援日誌記錄
- [ ] 新增工具測試 (DatabaseToolTests, WebSearchToolTests)
- [ ] 更新 docs/architecture.md

## 風險與依賴
- 資料庫工具需要安全的連接字串處理
- 網頁搜尋工具需要 HTTP 請求與 HTML 解析
- 日誌記錄需要效能考量

## 驗收標準 (Definition of Done)
- [ ] 所有專案編譯成功 (0 錯誤)
- [ ] 所有單元測試通過
- [ ] 新工具正常執行
- [ ] 日誌記錄正常運作
- [ ] 程式碼符合 .NET 10 標準與命名慣例
