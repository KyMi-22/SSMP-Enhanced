# 0.2.6 存档页 UI 刷新修复

用户截图使用 SSMP 原生“多人游戏 → 设置 → 我的皮肤”。本机 2026-11-04 的 LogOutput.log 确认加载 CoopAssist 0.2.5；多个 [OwnSkin] 选择事件及 CuteHornet 角色图片加载事件之后，没有 [SkinReload] 完整切换完成记录。

已核对 SettingsPanel.StepSkin：原生菜单直接调用 OwnSkin.Choose。0.2.5 只让 CoopAssist 的 F7/ModMenu 调用完整事务，而原生选择只改启用标记和角色图集，T2D UI 图片表未重建，因此一直取启动时 SteelSong 的 UI。

## 修复

- 为原生 OwnSkin.Choose 增加统一入口，将外部选择转入现有完整热切换事务。内部提交和失败回退使用明确的重入保护，仍调用原生 Choose 执行标记、异步角色图集和外观广播。
- 为 Graphic.OnEnable 增加轻量补刷，仅检查 Image 当前 sprite/overrideSprite 或 RawImage 的纹理；只有名称和尺寸匹配当前皮肤图片表时才交给现有 TrySwap。存档面板可在同一 Menu_Title 场景内稍后启用，无须再次换图才能刷新。
- 切换期间激活的 UI 延后由已有补扫处理，不在恢复原图过程中改写贴图。无关文字、队友材质和其他贴图不进入这条补刷。

## 已检查

- 编译成功，保留两处已有 OverlapBoxNonAlloc 过时提示。
- 原有策略、坐标消息、外观同步、运动/调度回归与版本绑定检查通过。
- 103 项热切换模拟通过，新增覆盖原生菜单直调、恰好一次内部提交、延迟激活的存档图像、已缓存页面跨皮肤刷新、overrideSprite、RawImage、切回原版和换图保护。
- Mono.Cecil 核对真实 SSMP Choose 的静态单参数签名，以及 Unity Image → MaskableGraphic → Graphic 的 OnEnable 调用链。
- 运行图片、加载器及 SSMP 沿用已校验 0.2.5 底包，整包重新核对大小和 SHA256。运行文件只更新 CoopAssist DLL。

这些是日志定位、源代码/程序集静态核对与 API 替身模拟，没有运行修复后的实际游戏、验证真实 GPU 渲染或进行双机测试。建议在原生菜单从 SteelSong 切到 CuteHornet，等待完成，再打开、关闭并重开存档选择页，并检查切回原版。
