<div align="center">
  <img src="assets/widgetcanvas.png" width="112" alt="WidgetCanvas 图标">
  <h1>WidgetCanvas · 浮岛</h1>
  <p>把任意 AI 生成的 HTML 变成真正可用的 Windows 桌面小组件。</p>
  <p><a href="README.en.md">English</a> · <a href="docs/host-api.md">宿主接口</a> · <a href="docs/webdav-sync.zh-CN.md">WebDAV 同步</a> · <a href="docs/external-integration.zh-CN.md">外部集成</a> · <a href="CONTRIBUTING.md">参与贡献</a></p>
</div>

![WidgetCanvas 浮岛](docs/images/hero.png)

WidgetCanvas 是一个面向 AI 创作的 Windows 桌面小组件画布。你只需描述需求，让任意 AI 返回一份单文件 HTML，然后粘贴并放到屏幕上。组件可以保存状态、请求网络、读取本地文件、操作剪贴板，并通过受约束的接口启动本地工具。

## 为什么是 WidgetCanvas

- **AI 优先：**复制内置提示词、补充需求、粘贴 HTML，即可完成组件。
- **不发明组件语法：**组件就是普通 HTML、CSS 和 JavaScript，源码始终可查看。
- **真正的桌面能力：**支持状态、剪贴板、HTTP、只读文件、用户目录和参数化进程调用。
- **放置自由：**创建多个命名画布，拖动、缩放、锁定、搜索、收进组件库，也能弹成独立窗口。
- **适合长期运行：**隐藏浮岛不会销毁仍在使用的组件。
- **本地优先：**默认完全保存在本机，也可选择通过自己的 WebDAV 多设备同步。

## 快速开始

1. 从 [Gitee Releases](https://gitee.com/shuimowang/WidgetCanvas/releases) 或 [GitHub Releases](https://github.com/shuimowang/WidgetCanvas/releases) 下载 `WidgetCanvas-win-x64.zip`。
2. 解压并运行 `WidgetCanvas.exe`。
3. 点击底部“复制 AI 提示词”，发送给任意 AI，并追加组件需求。
4. 复制 AI 返回的完整 HTML。
5. 在浮岛空白处拖出区域，编辑器会自动读取剪贴板并填入 HTML。

如果剪贴板没有完整 HTML，编辑器会填入一个可直接使用的护眼便签。

支持 Windows 10/11 x64。发布包自带 .NET 运行时，无需另装 .NET；程序使用当前 Windows 通常已经包含的 [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)。

## 日常使用

- 点击组件外空白处或按 `Esc`：隐藏浮岛，组件继续运行。
- 点击右上角 `×`：真正退出并释放 WebView2。
- 双击托盘图标或再次运行 EXE：重新显示浮岛。
- 右键托盘图标：进入画布或组件库，也可直接把任意组件显示为独立窗口。
- 从托盘打开“管理中心”：配置自动更新、WebDAV 同步、登录后驻留托盘和显示画布的全局快捷键；快捷键可选择始终打开第一个主画布。
- 组件右键：编辑、重新加载、锁定、弹出、收进组件库、复制或永久删除。
- 按住 `Ctrl` 拖动组件手柄：弹出为独立组件窗口。
- 点击底部当前画布名称：切换、新建、重命名或删除画布；组件库在所有画布之间共享。
- `WidgetCanvas.exe --canvas "工作"`：按名称直接切换并显示画布。
- `WidgetCanvas.exe --widget "组件标题"`：按 HTML `<title>` 直接打开某个组件。
- `WidgetCanvas.exe --file "D:\Widgets\clock.html"`：把 HTML 文件临时加载为独立组件窗口，不加入组件库。
- `WidgetCanvas.exe --settings`：直接打开管理中心；`--background`：只在托盘驻留。
- `WidgetCanvas.exe --exit`：真正退出应用并释放全部组件窗口与 WebView2。

## 外部自动化

Quicker 动作或脚本无需启动界面即可读取当前组件标题：

```powershell
WidgetCanvas.exe --list-widgets --output "%TEMP%\WidgetCanvas-widgets.json"
WidgetCanvas.exe --widget "组件标题"
WidgetCanvas.exe --canvas "工作"
WidgetCanvas.exe --file "D:\Widgets\clock.html"
WidgetCanvas.exe --settings
WidgetCanvas.exe --exit
```

组件目录变动后，应用会原子更新 `%LocalAppData%\浮岛\Integration\widgets.json`，再触发 `Local\WidgetCanvas.ComponentsChanged`。JSON 结构和推荐的 Quicker 右键菜单流程见[外部集成文档](docs/external-integration.zh-CN.md)。

## WebDAV 多设备同步

管理中心可以连接用户自己的 WebDAV 目录，同步组件 HTML 以及便签、待办等 `host.state` 数据。同步不会上传画布布局、独立窗口位置、WebView2 缓存或应用设置；从其他设备收到的新组件先进入组件库。应用采用三方合并和 ETag 条件写入，同时修改时会保留冲突副本，不会静默覆盖。参见 [WebDAV 同步文档](docs/webdav-sync.zh-CN.md)。

## 数据目录

用户创建的组件源码放在便于查看和备份的位置：

```text
Documents\浮岛\组件\widgets.json
```

布局、组件状态、缓存和日志放在本机运行数据目录，不参与 Documents 文档同步。启用 WebDAV 时只从运行状态中提取组件自己的 `host.state` 数据：

```text
%LocalAppData%\浮岛\State\canvas.json
%LocalAppData%\浮岛\State\FileWidgets\*.json
%LocalAppData%\浮岛\Integration\widgets.json
%LocalAppData%\浮岛\Sync\webdav-base.json
%LocalAppData%\浮岛\WebView2\
%LocalAppData%\浮岛\Logs\
```

宿主统一对状态写入做防抖，并采用临时文件、落盘刷新和原子替换；上一份可读数据保留为 `.bak`。

应用设置保存在 `%LocalAppData%\浮岛\Settings\settings.json`。自动更新默认优先使用 Gitee Releases，连接失败会自动回退 GitHub；也可在管理中心更改首选渠道。两个渠道使用同一份构建产物，并在安装前核对发布时生成的 SHA-256 校验值。

## 组件接口

组件通过 `const host = window.widgetHost` 使用 23 个 Promise 方法，覆盖状态、剪贴板、网址、本地路径、窗口、HTTP、只读文件和进程。完整参数与返回结构见[宿主接口文档](docs/host-api.md)。

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

`process.start` 与 `process.run` 能力较强。运行第三方组件前应检查其可见 HTML 源码。WidgetCanvas 不提供任意路径写入、删除、注册表或整段 Shell 字符串接口，但进程方法可以启动本机任意可用程序，包括命令解释器。

## 从源码构建

需要 Windows、.NET 10 SDK 和 WebView2 Runtime。

```powershell
dotnet restore WidgetCanvas.slnx
dotnet test tests\WidgetCanvas.Tests\WidgetCanvas.Tests.csproj -c Release
dotnet publish src\WidgetCanvas\WidgetCanvas.csproj -c Release -r win-x64 --self-contained true
```

发布结果位于 `src\WidgetCanvas\bin\Release\net10.0-windows\win-x64\publish`。

## 当前阶段

WidgetCanvas 目前处于早期预览阶段。`v1.0` 前数据格式仍可能调整，但提供给组件使用的宿主接口会保持克制、准确并持续记录。

## 许可证

[MIT](LICENSE)
