# 第三方组件与资产

本仓库只包含 Coop Assist 自己的源码、测试、工具和文档。运行时依赖的第三方组件**不随仓库分发**，需要按下面的版本自行安装；发布包（见 Releases）里的二进制由这些项目再分发而来，各自保留原授权。

| 组件 | 版本 | 授权 | 来源 | 仓库内是否有 |
| --- | --- | --- | --- | --- |
| SSMP（Silksong Multiplayer） | 0.4.142（SHA-256 钉死在 `src/CoopAssist.cs` 的 `ExpectedSsmp`） | LGPL-2.1 | [Candy-649/SSMP](https://github.com/Candy-649/SSMP) | 否 |
| ModMenu | 0.8.0 | 见上游仓库 | [silksong-modding/Silksong.ModMenu](https://github.com/silksong-modding/Silksong.ModMenu) | 否 |
| BepInEx | 5.4.23.4 | LGPL-2.1 | BepInEx 官方发布 | 否 |
| Harmony（0Harmony） | 随 BepInEx 5 | MIT | BepInEx / Harmony | 否 |
| BouncyCastle.Cryptography | 随 SSMP 发布 | MIT | BouncyCastle | 否 |
| UnityDoorstop | 4.4.1 | LGPL-2.1 | [NeighTools/UnityDoorstop](https://github.com/NeighTools/UnityDoorstop) | 否 |
| DebugMod | 1.1.2 | 见上游仓库 | HK Speedrunning | 否 |
| I18N / Modlist | 1.1.1 / 0.3.1 | 见上游仓库 | silksong-modding | 否 |
| Aa轩辕体（AaXuanYuanTi-2.ttf） | 2 | 需单独确认再分发授权 | Aa 字库 | 源码仓库不含，随 Releases 的发布包分发 |
| 皮肤与 FieryHornet 过场资源 | 11 套 | 各自作者授权 | 随完整包分发，不在仓库 | 否 |

## 发布时要注意

- **完整包不再上传 GitHub。** 它含 11 套皮肤（`Hollow Knight Silksong_Data/Mods/Customizer`，约 2.44 GiB），超过 GitHub Release 单文件 2 GiB 的上限；需要的话拆成多个分卷或另找托管。主发布包 `SSMP-Enhanced-<版本>.zip` 约 13 MiB。
- 发布包内已含 SSMP/ModMenu/DebugMod 等二进制，按各自授权随包分发；仓库里保留了授权说明与上游来源，LGPL 组件的源码获取方式见上表链接。
- 皮肤、WebM 等素材需要在发布说明里标明来源与授权，或只随本地整合包分发。

## 本项目的游戏基线

- 游戏：**1.0.30000**
- SSMP：**0.4.142**（`ExpectedSsmp` 校验的是二进制哈希，换版本必须先改这个常量并重跑 `tools/Audit.ps1`）
- 联机标识：`0.3.0-std3`
