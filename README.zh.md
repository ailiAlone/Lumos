# Lumos

> 给屏幕披上夜色。v1.0。

**[English](README.md)** | **[中文](README.zh.md)**

Lumos 把屏幕沉进夜色。只有一捧柔和的月晕跟着光标走——像月光落在暗水上。桌面上的任何动态——动画、视频、新窗口——**实时**透过那捧月晕显示,不是冻结的截图。

---

## 快速上手

1. **下载** `Lumos.exe`(单文件,约 72 MB,自包含)。
2. **双击**运行。屏幕沉入夜色;光标就是你的月光。
3. 按 **Esc** 让夜色散去。

就这样。不用装任何东西,没有 DLL,没有配置。

### 快捷键

| 按键       | 作用             |
| ---------- | ---------------- |
| `Esc`      | 让夜色散去       |
| `+` / `=`  | 张开月晕         |
| `-`        | 收拢月晕         |

月晕半径范围 50 ~ 1500 像素。

---

## 分发

`Lumos.exe` 是 .NET 8 自包含单文件发布版,**目标机器不需要装 .NET**。

- 适用 **Windows 10(1809+)和 Windows 11**,x64
- 直接把 .exe 发出去——邮件、聊天、U盘都行,对方双击就跑

---

## 实现原理(给好奇的人看)

一个全屏、无边框、置顶的窗口。三件事让夜色成立:

1. **`WS_EX_LAYERED`** —— 窗口是"分层窗口",系统按**每像素 alpha 通道**把它合成到所有东西之上。
2. **`UpdateLayeredWindow`** 配合 `ULW_ALPHA` + `AC_SRC_ALPHA` —— 窗口的内容是一张 32 位 BGRA 位图,每个像素的 alpha 独立决定该点的透明度。
3. **自顶向下 DIB 节**(`CreateDIBSection`)—— 系统真正读取的、保留 alpha 信息的位图。我们在 C# 里算出 alpha 蒙版,直接 `Marshal.Copy` 到 DIB 的像素指针,然后推给窗口。

每像素 alpha 公式:

| 距光标的距离 | Alpha | 结果 |
| ------------ | ----- | ---- |
| `0 ~ r/2`    | `0`   | 完全透明——夜幕退开,真实桌面透过来 |
| `r/2 ~ r`    | `0 → 255`(smoothstep) | 柔和地沉入夜色 |
| `> r`        | `255` | 满色夜色——不透明黑 |

`r` 是月晕半径(默认 280 像素,用 `+` / `-` 可调)。

窗口同时设了 `WS_EX_TRANSPARENT`(鼠标点击穿透到下面)和 `WS_EX_TOOLWINDOW`(不在任务栏和 Alt-Tab 出现)。

---

## 项目结构

```
Lumos/
├── Lumos.csproj          # 构建配置(net8.0-windows, WinForms, 单文件)
├── Program.cs            # 整个应用,约 220 行
├── app.manifest          # 高 DPI 感知声明
├── README.md             # 英文文档
├── README.zh.md          # 你正在看这个
├── Lumos.exe             # 构建后产物
├── bin/                  # 构建输出(可加入 .gitignore)
└── obj/                  # 构建中间产物(可加入 .gitignore)
```

---

## 从源码构建

### 前提

- .NET 8 SDK(`dotnet --version` 应输出 `8.x`)
- Windows 10 或 11(x64)

### 开发构建

```bash
dotnet build -c Release
```

产物在 `bin/Release/net8.0-windows/win-x64/`。

### 发布构建(用于分发)

```bash
dotnet publish -c Release -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true
```

自包含单文件 `Lumos.exe` 出现在 `bin/Release/net8.0-windows/win-x64/publish/`,随便拷哪里。

### 可调构建参数

| 参数 | 效果 |
| ---- | ---- |
| `-p:PublishTrimmed=true` | 剪裁掉没用到的 BCL,体积降到约 35 MB(发布前先测一下,WinForms 剪裁偶尔会误伤) |
| `-p:PublishAot=true` | NativeAOT,约 15 MB,冷启动无延迟。**当前没启用**,因为需要额外配置,也会失去 .NET 反射机制的安全网。 |
| `-p:DebugType=embedded` | 把 PDB 嵌进 .exe,便于用户拿到崩溃 dump。`.csproj` 里默认开启。 |

---

## 已知限制

- **GDI 截图抓不到它。** 标准截屏 API(`BitBlt` / `CopyFromScreen`)读的是 frame buffer,跳过分层合成层。所以 PowerShell / C# `Graphics.CopyFromScreen` 看不到 Lumos。要截图请用 **Win+Shift+S**(系统截图工具,走 DWM 合成)、**OBS**,或者**手机拍屏幕**。
- **多显示器:** 目前夜色只覆盖主显示器。要跨屏需要把 bounds 和 alpha 蒙版扩展到 `Screen.AllScreens`。(列入 v1.1)
- **主显示器 DPI > 100%:** 表单用的是物理像素 `Bounds`,alpha 按原生分辨率计算,理论上能工作,但 PerMonitorV2 设得偏保守。看到缩放瑕疵的话,可以临时把 `Application.SetHighDpiMode(HighDpiMode.SystemAware)` 作为退路。

---

## 许可证

个人 / 教学用途。用、fork、随便发。

---

## 致谢

- 用 .NET 8 WinForms 写。
- "Lumos"——哈利波特里开灯的咒语。(这个工具把夜幕掀开,只在你的目光所及之处让真实桌面照过来。)
