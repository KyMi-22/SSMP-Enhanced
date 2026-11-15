# 合作整合包 0.2.4 测试版

可直接安装到干净游戏的完整 Mod 包，包含 BepInEx、SSMP 0.4.142、更新的 Coop Assist 0.2.4 DLL、ModMenu 及依赖、DebugMod、11 个可选皮肤。不需要先安装旧包，不含游戏本体和个人存档。

## 安装

1. 双方关闭游戏，将压缩包内容解压到 Hollow Knight Silksong.exe 所在目录。
2. 合并 BepInEx、Hollow Knight Silksong_Data 两个文件夹，复制 doorstop_config.ini、winhttp.dll 和 .doorstop_version 等根目录文件。Docs、Sources、Rollback、Install.md、Manifest.json 是说明、源码和回退材料。
3. 双方使用同一份 0.2.4 包。Start Multiplayer → Direct IP：房主创建，队友填写房主的 Radmin 地址及端口。
4. 选项 → 模组 / Mods → 联机体验；F7 也可打开设置。F2 是 DebugMod，F8 是安全脱困。

从 0.2.3 升级也请合并本包的两个运行文件夹：本次同时更新 DLL 并新增 8 个皮肤目录，仅换 DLL 不会增加皮肤。保留个人配置；不要把旧 DLL 改名后留在 plugins 中，也不要混装其他版本的联机框架。

## 皮肤选择

11 个皮肤安装时全部关闭，默认使用原版外观。回到主菜单并退出房间后，在“联机体验”中选择皮肤，列表可滚动；选好后重启游戏以应用完整外观和 UI。选择“原版外观”可以恢复。

每个人选择自己的皮肤和 UI；队友角色按照队友自己的选择显示。双方安装完整资源，才能正确显示对方选用的皮肤。自己的 HUD 不跟随队友选择。

新增：SteelSong、StarsNSparkles（Cohesive Colors）、FieryHornet / Wisp Hornet、Phantomish、ShadowHornetYellowSilk、CuteHornet、FiendlordsPureVessel、MaidHornet。

保留：GhostOfPharloom、VampireHornet、XJY。详见 Docs/Skins-0.2.4.md。

FieryHornet 自带的两段额外过场视频已原样保留；本包现有加载器不能播放这两段替换视频。角色和普通贴图资源已纳入检查，未额外引入过场视频加载器。

## 新版 DLL 与保留的功能

- DLL 版本为 0.2.4，双方联机扩展版本也更新为 0.2.4；皮肤列表增加滚动显示。
- 完整保留 0.2.3 的人物 20 毫秒显示缓冲、最多 50 毫秒预测、短暂中断恢复、60 Hz 采样和约 33 毫秒受击事件补发优化，以及受击动画映射修复。
- 保留减少逐帧临时内存、名字颜色同步、队友方位提示和地图位置、倒地观战、加入房主出生点、个人念珠和碎片吸附等已有功能。
- 碎石平台继续各端独立。左上角模组列表可在 Modlist 设置中将 DisplayMode 设为 Hidden。

本次没有额外修改 Boss、怪物、机关、死亡或存档机制。验证范围见 Docs/Validation-0.2.4.md；没有进行双机游戏实测。

若仍有卡顿，请保存双方同一次联机的 BepInEx/LogOutput.log。[Motion] 记录采样和位置应用间隔，[Playback] 记录本机解码完成到主线程处理的等待，不等于网络延迟或丢包率。

## 回退

双方关闭游戏，将 Rollback/CoopAssist-0.2.3.zip 内的 BepInEx 文件夹覆盖到游戏根目录，即恢复上一版 DLL。不要同时保留两个 DLL。新增皮肤可留作资源；此回退不删除皮肤、不改变个人存档或配置。双方一起回退。

Docs 中更早版本的文档仅作历史参考；本文件和 Validation-0.2.4.md 对应本次发布。
