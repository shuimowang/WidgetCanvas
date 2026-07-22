<div align="center">
  <img src="assets/widgetcanvas.png" width="112" alt="WidgetCanvas logo">
  <h1>WidgetCanvas</h1>
  <p>Turn AI-generated HTML into live Windows desktop widgets.</p>
  <p><a href="README.md">简体中文</a> · <a href="docs/host-api.md">Host API</a> · <a href="docs/webdav-sync.md">WebDAV sync</a> · <a href="docs/external-integration.md">External integration</a> · <a href="CONTRIBUTING.md">Contributing</a></p>
</div>

![WidgetCanvas canvas](docs/images/hero.png)

WidgetCanvas is an AI-first desktop widget canvas for Windows. Describe a widget to any AI, paste the returned single-file HTML, and place it anywhere on a full-screen overlay. Widgets can store state, call HTTP APIs, read local files, use the clipboard, and launch local tools through a small host API.

## Why WidgetCanvas

- **AI-first workflow:** copy the built-in prompt, describe what you need, and paste one HTML file.
- **No custom widget language:** widgets are ordinary HTML, CSS, and JavaScript with visible source.
- **Desktop capabilities:** state, clipboard, HTTP, read-only files, known folders, and parameterized process execution.
- **Flexible placement:** use multiple named canvases, then drag, resize, lock, archive, search, or detach widgets.
- **Designed for long-running widgets:** hiding the canvas keeps active widgets running.
- **Local by default:** data stays on your computer unless you opt into sync through your own WebDAV service.

## Quick start

1. Download `WidgetCanvas-win-x64.zip` from [Gitee Releases](https://gitee.com/shuimowang/WidgetCanvas/releases) or [GitHub Releases](https://github.com/shuimowang/WidgetCanvas/releases).
2. Extract it and run `WidgetCanvas.exe`.
3. Click **复制 AI 提示词**, send it to any AI, and add your widget requirements.
4. Copy the returned complete HTML document.
5. Drag an empty area on the canvas. The editor reads the HTML from your clipboard automatically.

If the clipboard does not contain a complete HTML document, the editor starts with a ready-to-use note widget.

Windows 10/11 x64 is supported. WidgetCanvas is self-contained and does not require a separate .NET installation. It uses the [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/), which is included with current Windows installations.

## Everyday use

- Click outside all widgets or press `Esc` to hide the canvas without stopping widgets.
- Use the top-right `×` to actually exit and release WebView2 resources.
- Double-click the tray icon or run `WidgetCanvas.exe` again to reopen the canvas.
- Right-click the tray icon to open the canvas or component library, or launch any widget directly in its own window.
- Open **Management Center** from the tray to configure automatic updates, WebDAV sync, launch to the tray at sign-in, and a global canvas hotkey that can always open the first main canvas.
- Right-click a widget to edit, reload, lock, detach, archive, duplicate, or delete it.
- Hold `Ctrl` while dragging a widget handle to detach it into a standalone window.
- Click the current canvas name in the bottom dock to switch, create, rename, or delete canvases. The library is shared globally.
- Use `WidgetCanvas.exe --canvas "Work"` to switch to and show a named canvas.
- Use `WidgetCanvas.exe --widget "Widget title"` to open one widget directly by its HTML `<title>`.
- Use `WidgetCanvas.exe --file "D:\Widgets\clock.html"` to load an HTML file as a standalone widget without adding it to the library.
- Use `WidgetCanvas.exe --settings` to open Management Center, or `--background` to start in tray-only mode.
- Use `WidgetCanvas.exe --exit` to quit the application and release all widget windows and WebView2 instances.

## External automation

Quicker actions and scripts can query the current titles without starting the UI:

```powershell
WidgetCanvas.exe --list-widgets --output "%TEMP%\WidgetCanvas-widgets.json"
WidgetCanvas.exe --widget "Widget title"
WidgetCanvas.exe --canvas "Work"
WidgetCanvas.exe --file "D:\Widgets\clock.html"
WidgetCanvas.exe --settings
WidgetCanvas.exe --exit
```

Catalog changes atomically update `%LocalAppData%\浮岛\Integration\widgets.json` and signal `Local\WidgetCanvas.ComponentsChanged`. See [External integration](docs/external-integration.md) for the JSON schema and recommended Quicker menu flow.

## WebDAV sync

Management Center can connect to your own WebDAV directory and synchronize widget HTML plus widget-owned `host.state` data such as notes and tasks. Canvas layout, detached-window position, WebView2 cache, and application settings stay on each device. New widgets received from another device enter the library. Three-way merging and conditional ETag writes preserve concurrent edits as conflict copies instead of silently overwriting them. See [WebDAV sync](docs/webdav-sync.md).

## Data locations

User-authored widget source is easy to back up:

```text
Documents\浮岛\组件\widgets.json
```

Machine-local state is kept out of Documents sync. When WebDAV is enabled, only widget-owned `host.state` values are extracted from runtime state:

```text
%LocalAppData%\浮岛\State\canvas.json
%LocalAppData%\浮岛\State\FileWidgets\*.json
%LocalAppData%\浮岛\Integration\widgets.json
%LocalAppData%\浮岛\Sync\webdav-base.json
%LocalAppData%\浮岛\WebView2\
%LocalAppData%\浮岛\Logs\
```

Writes are debounced and atomic. The previous readable file is kept as a `.bak` backup.

Application settings are stored in `%LocalAppData%\浮岛\Settings\settings.json`. Automatic updates prefer Gitee Releases by default and fall back to GitHub; the preferred channel can be changed in Management Center. Both channels publish the same artifacts, which are verified against the published SHA-256 checksum before installation.

## Widget host API

Widgets use `const host = window.widgetHost`. The API contains 23 Promise-based methods across state, clipboard, URLs, local paths, window control, HTTP, read-only files, and processes. See the complete [Host API reference](docs/host-api.md).

```html
<script>
const host = window.widgetHost;

async function load() {
  const count = await host.state.read("count", 0);
  await host.state.write("count", count + 1);

  const response = await host.http.get({ url: "https://example.com/data.json" });
  if (!response.ok || response.truncated) throw new Error(`HTTP ${response.status}`);
}

load().catch(error => {
  document.body.textContent = String(error?.message || error);
});
</script>
```

`process.start` and `process.run` are intentionally powerful. Review a widget's visible HTML source before running code from someone you do not trust. WidgetCanvas does not provide arbitrary file-write, delete, registry, or shell-string APIs, but the process methods can start any available executable, including command interpreters.

## Build from source

Requirements: Windows, .NET 10 SDK, and the WebView2 Runtime.

```powershell
dotnet restore WidgetCanvas.slnx
dotnet test tests\WidgetCanvas.Tests\WidgetCanvas.Tests.csproj -c Release
dotnet publish src\WidgetCanvas\WidgetCanvas.csproj -c Release -r win-x64 --self-contained true
```

The publish output is under `src\WidgetCanvas\bin\Release\net10.0-windows\win-x64\publish`.

## Status

WidgetCanvas is in early preview. The storage format may change before `v1.0`, but the widget-facing host API is kept deliberately small and documented.

## License

[MIT](LICENSE)
