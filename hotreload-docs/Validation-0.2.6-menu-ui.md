# 0.2.6 局外 UI 恢复修补（2026-11-05）

仍使用 0.2.6。唯一需要更新的运行文件为 BepInEx/plugins/SSMP/Silksong.CoopAssist.dll；项目包内保留此前四格信号、网络诊断、中文名字及原生角色选择修复。各端退出游戏覆盖后启动一次；实际游戏目录和存档未自动安装或修改。

## 实际故障证据

本机 D:/Game/Hollow Knight Silksong/BepInEx/LogOutput.log 在 13:39:47 和 13:39:48 确认运行 0.2.6，分别选择原版及 CuteHornet 后，均记录恢复 sactx-0-512x1024-BC7-Load_Icon_Knight-76ea0715 失败。异常位于 Texture2D.Reinitialize(int,int,GraphicsFormat,bool)，调用链为 SkinHotReload.Restore → Apply；RecoverUi 又执行同一步，再次失败。

旧实现假定 LoadImage(...,false) 能使非 CPU 可读贴图恢复可读。实际运行没有满足该假定。旧模拟把该属性直接改为 true，掩盖了错误；本次先将模拟改为保留不可读状态，确认旧实现再次失败，再修补恢复逻辑。

## 改动

- 移除 Restore 中的 1×1 PNG 引导及 Reinitialize，保留 UI 引用的同一贴图对象、尺寸和已解码格式。
- 格式及 mip 数量相同时，直接从原图 GPU 备份复制。BC7 原图与 RGBA 替换图格式不同时，使用临时 RenderTexture 转换，再生成并复制全部目标 mip。无需读取原贴图 CPU 数据，也没有 CPU 图像回读。
- 仅当贴图仍有可读 CPU 副本时，才先 Apply(false,true) 丢弃该副本。GPU 复制之后不再 Apply，防止把旧皮肤 CPU 内容写回。
- 临时目标、RenderTexture.active 和 GL.sRGBWrite 在 finally 中恢复；处理 Gamma/Linear 色彩空间。现有 11 个皮肤的全部 409 张 Texture2D PNG 均为 RGBA，未改动皮肤资源字节。
- 仍先执行原生角色选择，随后处理个人 UI；存档页缓存与延后显示继续使用原来的贴图对象。未改人物同步或网络消息。

## 验证

154 项换肤事务模拟通过，包含实际日志中的不可读状态、连续皮肤切换/恢复原版、存档页 Image/overrideSprite/RawImage、原对象身份、格式转换、低级 mip 清理、颜色/渲染状态恢复、转换失败后的 UI 恢复及临时资源释放。静态检查要求 Restore 不得调用 Reinitialize 或 LoadImage，并检查转换 API、全部 mip 复制与 finally 清理。

此外运行原有逻辑/动作/名字/网络诊断回归，并校验最终安装包。未启动本次修订后的游戏，未实际执行 GPU 转换或双机/三机测试；模拟不能替代真实画面验收。

局外验收步骤：启动后在“多人游戏 → 设置 → 我的皮肤”依次选择一个皮肤、另一个皮肤、原版，每次等待 [SkinReload] Completed，再打开已有存档页；随后入档确认角色换肤仍可用。若不一致，保留完整 LogOutput.log。

## API 核对

使用本机 UnityEngine.CoreModule.dll 的静态接口/IL 核对，GraphicsFormat 版本 Reinitialize 在调用原生实现前检查 isReadable。REA CLI 的静态身份检查 Evidence 为 ev_bdaebde3e430de75b928601d250281c97c1227ddf87289b3773916441685d8a8；方法体由本地 Mono.Cecil 读取，不把它冒称为 REA 反编译或运行结果。

GPU 格式转换采用与 [Unity Graphics.ConvertTexture 文档](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Graphics.ConvertTexture.html) 所述相同的临时渲染目标、Blit 和 CopyTexture 路径；这里显式生成 mip 并恢复渲染状态。游戏中的 API 由实际 Unity 6000.0.50 程序集作静态核对。
