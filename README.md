# Lumos

> A night-mode filter for your real desktop. v1.0.

**[English](README.md)** | [中文](README.zh.md)

Lumos veils your screen in night. A soft halo of clarity follows your cursor — like moonlight on dark water. Whatever is happening on your desktop — animations, video, new windows — is visible only inside that halo, **in real time, not a screenshot**.

---

## Quick start

1. **Download** `Lumos.exe` (single ~72 MB self-contained binary).
2. **Double-click** to run. The screen goes dark; your cursor parts the night.
3. Press **Esc** to dismiss the dark.

That's it. Nothing to install, no DLLs, no config.

### Keys

| Key       | Action          |
| --------- | --------------- |
| `Esc`     | Dismiss the dark |
| `+` / `=` | Widen the halo  |
| `-`       | Narrow the halo |

The halo's radius ranges from 50 px to 1500 px.

---

## Distribution

`Lumos.exe` is a self-contained single-file publish. It bundles the .NET 8 runtime, so the target machine does **not** need .NET installed.

- Works on **Windows 10 (1809+)** and **Windows 11**, x64.
- Just send the .exe. By mail, by chat, by USB. The user double-clicks and it runs.

---

## How it works (for the curious)

A full-screen, borderless, always-on-top window. Three things make the trick work:

1. **`WS_EX_LAYERED`** — the window is a "layered window", which the OS composites on top of everything else using a per-pixel alpha channel.
2. **`UpdateLayeredWindow`** with `ULW_ALPHA` + `AC_SRC_ALPHA` — the window's content is a 32-bit BGRA bitmap. Each pixel's alpha controls its opacity independently of the mouse.
3. **A top-down DIB section** (`CreateDIBSection`) — the alpha-aware bitmap the OS actually reads. We compute the alpha mask in plain C# (`Marshal.Copy` into the DIB's pixel pointer), then push it to the window.

The alpha mask per pixel is:

| Distance from cursor | Alpha | Result |
| -------------------- | ----- | ------ |
| `0` to `r/2`         | `0`   | Fully transparent — the night parts, the desktop shows |
| `r/2` to `r`         | `0` → `255` (smoothstep) | Soft falloff into the dark |
| `> r`                | `255` | The dark — opaque black |

`r` is the halo's radius (default 280 px, adjustable with `+` / `-`).

The form also sets `WS_EX_TRANSPARENT` (mouse clicks pass through to whatever is below) and `WS_EX_TOOLWINDOW` (hidden from the taskbar and Alt-Tab).

---

## Project layout

```
Lumos/
├── Lumos.csproj          # Build config (net8.0-windows, WinForms, single-file)
├── Program.cs            # The whole app — ~220 lines
├── app.manifest          # High-DPI awareness declaration
├── README.md             # You are here (English)
├── README.zh.md          # 中文版
├── Lumos.exe             # Built distributable (after publish)
├── bin/                  # Build output (gitignored)
└── obj/                  # Build intermediates (gitignored)
```

---

## Building from source

### Prerequisites

- .NET 8 SDK (`dotnet --version` should print `8.x`)
- Windows 10 or 11 (x64)

### Build (development)

```bash
dotnet build -c Release
```

Output goes to `bin/Release/net8.0-windows/win-x64/`.

### Publish (distribution)

```bash
dotnet publish -c Release -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true
```

The single self-contained `Lumos.exe` ends up in `bin/Release/net8.0-windows/win-x64/publish/`. Copy it anywhere.

### Build options you might want to tweak

| Flag | Effect |
| ---- | ------ |
| `-p:PublishTrimmed=true` | Strip unused BCL, drops size to ~35 MB (test before shipping — WinForms trimming has occasional false positives) |
| `-p:PublishAot=true` | Native AOT, ~15 MB, instant cold start. Currently **not enabled** in `Lumos.csproj` because it requires extra config and removes `dotnet`'s reflection-based safety nets. |
| `-p:DebugType=embedded` | Embeds PDB inside the .exe so users get better crash dumps. Default-on in the .csproj. |

---

## Known limitations

- **GDI screenshots miss it.** Standard screen-capture APIs (`BitBlt`, `CopyFromScreen`) read the frame buffer and skip the layered compositing layer. This means PowerShell/C# `Graphics.CopyFromScreen` won't see Lumos. To capture Lumos, use **Win+Shift+S** (Snipping Tool, which uses DWM), **OBS**, or a **phone camera** pointed at the monitor.
- **Multi-monitor:** the dark is sized for the primary screen. If you want it to span all monitors, the bounds calc and the alpha mask need to be extended to cover `Screen.AllScreens`. (Tracked as a v1.1 item.)
- **DPI > 100% on the primary monitor:** the form uses `Bounds` (physical pixels) and renders alpha at native resolution, so it should work, but the per-monitor v2 awareness is set conservatively. If you see scaling artifacts, try setting `Application.SetHighDpiMode(HighDpiMode.SystemAware)` as a fallback.

---

## License

Personal / educational. Use it, fork it, ship it.

---

## Credits

- Built with .NET 8 WinForms.
- "Lumos" — the light-opening charm from the Harry Potter books. (This tool parts the dark, letting the real desktop shine through only where you look.)
