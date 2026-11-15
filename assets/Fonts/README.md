# 字体说明

`AaXuanYuanTi-2.ttf`（Aa轩辕体）用于中文玩家名、F7 面板与记账本，是第三方字体，**不放进本仓库**。

## 怎么让构建找到它

`tools/Build.ps1` 会按下面顺序查找，有一份即可：

1. `assets/Fonts/AaXuanYuanTi-2.ttf`（想本地放一份就放这里，已被 `.gitignore` 忽略）
2. 游戏目录 `BepInEx/plugins/SSMP/Fonts/AaXuanYuanTi-2.ttf`（安装合作版后自带）
3. 工作区外两级 `BepInEx/plugins/SSMP/Fonts/AaXuanYuanTi-2.ttf`

构建会把找到的字体复制到 `build/Fonts/`，打包时随更新包一起发出。

## 为什么不在仓库里

该字体的授权需要单独确认（个人使用和再分发不是一回事）。若要把它提交进仓库，请先确认授权覆盖公开再分发，再删除 `.gitignore` 里对应的两行。
