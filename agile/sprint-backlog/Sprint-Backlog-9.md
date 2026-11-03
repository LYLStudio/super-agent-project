# Sprint Backlog 9

## Sprint 目標
實作程式碼重構建議工具，並建立 Docker 容器化。

## 待辦事項 (Product Backlog Items)

| ID | 項目 | 優先級 | 估計工時 | 狀態 | 負責人 |
|----|------|--------|----------|------|--------|
| US-021 | 實作程式碼重構建議工具 | P2 | 4h | ✅ 完成 | 開發者1 |
| US-022 | 實作 Docker 容器化 | P2 | 3h | ✅ 完成 | 開發者2 |

## 進行中的事項
- 無

## 已完成事項
- US-021: 實作程式碼重構建議工具
  - RefactoringTool.cs - 程式碼重構建議
- US-022: 實作 Docker 容器化
  - Dockerfile - 多階段建置
  - docker-compose.yml - 服務佈署配置

## 已完成事項
- RefactoringTool: 程式碼命名問題檢測、方法抽取機會、類別拆分建議
- 新增單元測試 (RefactoringToolTests, 8 個測試)
- Dockerfile: 多階段建置 (build + runtime)
- docker-compose.yml: superagent + api-server 服務
- 所有測試通過 (Core: 52, Client: 3)

## 阻礙清單
- 無

