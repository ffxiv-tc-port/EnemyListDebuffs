# EnemyListDebuffs

[Felys1998/EnemyListDebuffs](https://github.com/Felys1998/EnemyListDebuffs) 的台服移植版。
在敵對列表上以圖示＋倒數計時顯示自身對每個目標施加的持續傷害／弱化狀態，方便多目標戰鬥時追蹤。

## 功能

- 敵對列表每個目標旁顯示對應的弱化狀態圖示與剩餘時間
- 可從右側開始填滿圖示
- 可調整圖示群組的位置、間距、縮放比例，以及圖示大小、倒數文字的位置與字型大小

## 指令

- `/eldebuffs`：開啟設定視窗

## 台服版的修改

- 顯示改由 Dalamud 的視窗事件驅動、不依賴特徵碼，遊戲改版時不會因為特徵碼對不上而停用
- 介面繁體中文化

## 安裝

在 Dalamud 設定的「自訂插件庫」加入
`https://raw.githubusercontent.com/ffxiv-tc-port/DalamudPluginsTC/main/repo.json`
並啟用，再從插件列表安裝。

## 說明

原作者已不再積極遊玩本作，上游處於尋求新維護者狀態；本 fork 由 ffxiv-tc-port 維護，供台服使用。

原作者：Felys、aers
