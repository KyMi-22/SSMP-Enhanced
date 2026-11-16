# Silksong Coop｜丝之歌合作联机增强

[English](README.en.md) · 简体中文

Silksong Coop 是为《空洞骑士：丝之歌》小队游玩维护的整合项目。SSMP 提供联机，Coop Assist 在原生联机流程上补充进度协作、队友互助、成茧观战、队友传送和外观设置。适配基线为游戏 **1.0.30000** 和 **SSMP 0.4.142**。

当前交付为 **`0.3.1-standard`**，修订 **`20261116-review-fixes`**，联机标识 **`0.3.1-std3`**。联机前请核对各端版本及修订。发布包在 [Releases](../../releases) 下载：`SSMP-Enhanced-0.3.1-standard.zip`，解压后把 `BepInEx`、`.doorstop_version`、`doorstop_config.ini`、`winhttp.dll` 放到游戏根目录（与游戏 exe 同级）即可，包内已含 BepInEx 5.4.23.4、SSMP 0.4.142、ModMenu 与中文化、DebugMod、I18N、Modlist、UnityHelper、WavLib、皮肤加载器与 Aa轩辕体。11 套皮肤体积约 2.44 GiB，单独发布。安装与回滚细节见 [安装说明](hotreload-docs/Install.md)。

## 当前玩法

- **借款购买。** 念珠不足时点购买会弹出居中的借款窗口：先问要不要借，选借款后用 **↑／↓** 挑队友、**Enter** 确认、**Esc** 回商店，窗口里写明商品价格、现有念珠和借款数额。队友端弹同款窗口；同意后他的念珠减少、你的念珠增加，模组随即自动买下那件商品并正常扣款，同时记进记账本。对方拒绝或余额不够时回到商品列表并提示。房主可在 **F7** 关闭借款（默认开启），关闭后仍可请队友攒钱补差。付费长椅只允许攒钱，不记欠款。
- **念珠记账本。** 存活时默认按 **F1** 开合，可在联机体验设置中改键。每条欠款都有还款按钮。借款按现实时间计 **8 分 48 秒**，逾期加收 **3 念珠**；优先用已有和之后获得的散装念珠还款，念珠串由玩家自行决定何时拆开。
- **共享进度与个人资源。** 入房自动同步剧情、祈愿、地图和装备解锁进度；在线继续共享祈愿、地图和装备进展。客机要主动按房主对齐普通资源时，坐长椅按 **F9**。个人念珠、念珠串、账本和当前纹章配装各自保留。
- **成茧与剧情。** 成茧后用游戏本身的快捷地图绑定，按住查看，松开关闭。用 **↑／↓** 在自己和**同场景**存活队友之间循环；**倒地等待期间整个房间会自动提亮**（看自己的茧也一样），救援或离开后恢复原暗度，队友换图后不再跨图传输画面。剧情等待放宽触发边缘，沿用原生过场流程。
- **队伍便利功能。** **F5** 成茧返回长椅，**F6** 选择队友传送，**F8** 安全脱困，**F10／F11／F12** 设置、前往和删除个人传送点。**F7** 打开联机体验面板，可调整快捷键、皮肤、HUD 与名字样式；快捷键可用任意键盘按键，只要互不冲突。队友传送、设置与删除传送点的过程结果都走游戏自带的发言记录。

## 皮肤

发布包**不含皮肤**（11 套皮肤的贴图约 2.44 GiB，超过 GitHub 单文件上限，也没有打包进来）。想换外观时，用 **Customizer 类皮肤 Mod** 效果最好：把皮肤包放进 `Hollow Knight Silksong_Data/Mods/Customizer/<皮肤名>`，皮肤加载器（`BepInEx/plugins/SkinLoader`）已经随包提供，主菜单和 F7 面板都能选择。

## 从源码构建

前置条件：

- 游戏 **1.0.30000**，目录默认为 `D:\Game\Hollow Knight Silksong`（其它位置用 `tools/Build.ps1 -GameRoot` 指定）
- 游戏目录里已安装 **SSMP 0.4.142** 与 **ModMenu 0.8.0**（构建直接引用它们，不随仓库分发）
- .NET SDK 9.0.315（脚本使用 Roslyn 的 `csc`）
- Aa轩辕体：放到 `assets/Fonts/AaXuanYuanTi-2.ttf`，或让游戏目录里装着合作版（自带该字体）。详见 [assets/Fonts/README.md](assets/Fonts/README.md)

流程：`tools/Build.ps1` → `tools/Test.ps1` → `tools/Audit.ps1` → `tools/Export-Update.ps1`。完整包需要一份解压好的游戏模板，通过 `tools/Export-Package.ps1 -TemplateRoot <目录>` 指定。

## 仓库里有什么

**受版本管理**：`src/`（模组源码）、`tests/`、`tools/`（构建、回归、审计、打包脚本）、`assets/ProgressSyncCatalog.json`（随 DLL 内嵌的进度目录，构建会校验它的哈希）、`hotreload-docs/`、`skins-docs/`、根目录的 README 与迭代简报。

**本地保留、不进仓库**：`build/`、`dist/`、`downloads/`，以及构建用的第三方快照 `upstream-*`、`modmenu-*`、`debug-*`、`i18n-source/`、`modlist-source/`。这些已经写进 `.gitignore`；构建脚本在缺少它们时会改用游戏目录里的同名文件。

**发布物**：`tools/Export-Release.ps1` 生成 `dist/SSMP-Enhanced-<版本>.zip`（只含 `BepInEx` 与 doorstop 四个运行时文件，已换成本次构建的 DLL 与字体），上传到 Releases。`dist/` 里的更新包与完整包留在本地：完整包的主体是 11 套皮肤贴图（`Hollow Knight Silksong_Data/Mods/Customizer`，约 2.44 GiB），皮肤不随本项目发布，用户可自行安装 Customizer 类皮肤包。

## 开发原则

**高质量**：购买、救援、剧情、场景切换和存档尽量复用游戏原生流程。优先验证扣款、退款、断线和重连；修改持久状态时，核对物品数量与获取点，并保留可回滚的交付物。

**高效率**：可复用的操作走同一入口；同步优先发送变化，按批次处理；跨场景画面仅在有人观战时发送。构建、回归、原生接口核对和打包都通过现有脚本执行。

**高联机体验感**：让队友容易协作，也能保留自己的钱包、配装和操作习惯。用明确的同意、确认和游戏内提示，让玩家知道下一步该做什么。

## 迭代与验证

[当前迭代简报](Iteration-Brief-0.3.0-standard.md) 汇总修改历史、已知边界与实机验收清单。[验证记录](hotreload-docs/Validation-0.3.0-standard.md) 区分自动化检查与已有的联机日志。当前修订已通过构建和回归；借款到账、成茧提亮等改动仍待逐项联机验收。

开发入口：[构建](tools/Build.ps1) · [全量回归](tools/Test.ps1) · [原生接口检查](tools/Audit.ps1) · [发布包](tools/Export-Release.ps1) · [更新包](tools/Export-Update.ps1) · [完整包](tools/Export-Package.ps1)。

## 授权

本仓库的源码、测试、工具与文档使用 [MIT 许可](LICENSE)。发布包里附带的第三方组件（SSMP、BepInEx、ModMenu、DebugMod、字体、皮肤等）各自保留原授权，版本与来源见 [THIRD-PARTY.md](THIRD-PARTY.md)。
