# 0.2.5 热切换检查

复用原生 OwnSkin.Choose/DressLoaded 异步角色换肤和 Changed → 原生外观消息；成功后调用原生 SendSkinLook，不新增网络消息或改变去重。旧 Customizer.GetTexturePacks 改为交由原生 OwnSkin，保持原图引用和队友克隆材质的独立性。

T2D 每个首次被改写的贴图保存同格式的 GPU 原图；恢复时重建原格式及 mip 链、复制原图，保留纹理对象身份。Unity 6000 对非可读纹理的 Reinitialize 有显式异常检查，因此恢复前必要时用一张有效 1x1 PNG 重新建立可读状态，恢复后再释放 CPU 像素。不会在 GPU 复制之后调用 Apply，以免旧 CPU 像素覆盖恢复结果。不能支持的非完整 mip 链不执行破坏性替换。

切换的文件 IO 与 PNG 尺寸读取在后台任务中进行，Unity 对象操作在主线程分帧执行。序列化选择请求，处理读取失败、解码失败回退和场景/Hero Start 取消原生异步等待的竞态。每次场景加载仅安排一轮贴图补扫，不新增持续全资源轮询。

## 已执行

- .NET 编译成功，仅两处已有 OverlapBoxNonAlloc 过时提示。
- 原有 205 策略、500 原生坐标消息、21,013 增强、309 外观封装、19,266 运动/调度检查及既有静态绑定检查通过。
- 91 项热切换事务检查：多次原版/A/B/原版、不可读贴图重建、原格式及身份恢复、不触碰无关队友纹理、Hero Start 回调、解码异常回退、损坏文件、重复提交、原图缓存释放。该测试使用 Unity API 替身，不是 GPU 实测。
- 读取 11 包全部 409 张 Texture2D 目录图片，验证真实文件可构建热切换计划，无规范化名称冲突。原 912 张 PNG 和两段 WebM 沿用 0.2.4 已校验字节；打包时逐项复核运行文件 SHA256。
- 对发货加载器的私有字段、TrySwap、GetTexturePacks、原生皮肤方法、SSMP 缓存字段和 Unity 6000 可读性检查作静态核对。

## 证据

按 reverse-engineer-anything 工作流使用已安装 REA CLI 对两个已发布加载器作只读托管文件检查；未修改 REA/Codex 配置，也未执行目标 DLL。

- Customizer.dll: REA Evidence ev_360955b78adf14d137f1c7d7ca8ff2148d44cdf6da46a14744b0b229c79c25cf。
- CustomizerT2D.dll: REA Evidence ev_40d5b857751e1d6c5cf4710e80811dc929bed8c24a3092c1c7706acd9c434115。

REA 结果只证明托管文件身份和静态元数据。具体加载流程另通过本地 Mono.Cecil IL 检查与固定版本 SSMP 源码核对：OwnSkin.cs、SkinMods.cs、AtlasSlashes.cs、SkinManager.cs、ClientManager.cs。没有将静态分析当作运行时观测。

## 未验证

未安装到实际游戏、未启动游戏、未开放端口、未进行双机联机或 GPU 渲染实测。保留 0.2.4 单 DLL 回退。换肤可能短暂占用 CPU/显存，首次保存原图和单张大图解码仍可能出现一帧停顿。
