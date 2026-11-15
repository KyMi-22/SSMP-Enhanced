# 0.2.7 名字颜色及 Aa轩辕体（2026-11-07）

## 名字颜色

2026-11-06 本机 0.2.6 日志已有 [NameColour] published 和 received，包含玩家 0/1/2 的有效 RGBA 颜色。原显示列表却使用 TMPro.TextMeshPro；对应 SSMP PlayerManager、CreateUsername、ChangeNameColor 实际使用 TMProOld.TextMeshPro，所以头顶名字没有进入着色列表。

显示列表及重绘挂钩统一使用真实的旧版组件。保留缓存扫描，在名字创建及原生队伍色重绘后应用已收到的个人颜色，避免重绘覆盖。发色、收色、批量回放及晚加入沿用现有可靠消息。发送所选身份颜色与本地 ColourOwnName 开关解耦；该开关仍控制本机自己头顶的显示。

## 字体及字号

来源为用户提供的 C:/Users/27508/Downloads/AaXuanYuanTi/AaXuanYuanTi-2.ttf，未修改字节。SHA256：FA0F0367B66A111EEB402E537770EC5DFB69FDDFCEF2E3CE4771D76FBD78AD58。Windows 字体解析确认字族 AaXuanYuanTi / Aa轩辕体、Regular / 常规，共 10935 个字形，检查用的中文、ASCII 和数字字形齐全。

文件随包放置于 BepInEx/plugins/SSMP/Fonts/AaXuanYuanTi-2.ttf，不依赖下载目录或系统字体安装。继续使用动态 SDF 转旧版 TMP 字形的已有适配，中文及中英混合名字强制选择 Aa 字体，不再因为游戏内置字体已经能显示中文而跳过。头顶名字和设置预览 fontSize=27，enableAutoSizing=false；不修改正常英文名字及其余界面字体。

## 验证与边界

- 原有 309 项颜色消息封装/批量/晚加入/重连检查和颜色编码回归通过。
- 静态断言核对真实旧版名字列表、字体/颜色挂钩参数，检查 Aa 文件路径、27 号常量、禁用内置字库短路，以及本地显示开关不影响发送。
- 实际 TTF 校验原文件哈希、字体家族及代表字符覆盖，4,005 项字形坐标/Unicode 回归通过。
- 原有 154 项 UI 换肤模拟、运动/调度、网络诊断及字体接口回归通过，静态接口审计无缺失。
- DLL 插件元数据及客户端、服务端扩展均为 0.2.7。整包及更新包包含同一 DLL 和原字体，按运行清单核对大小及 SHA256。

未安装到实际游戏或修改存档，未启动本次修订游戏、执行 GPU 字体栅格化或双机画面验收。各端更新后可在同场景选择不同名字颜色，测试换图重入、中文名字、27 号预览及晚加入。