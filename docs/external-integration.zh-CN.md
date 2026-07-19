# 外部集成

WidgetCanvas 为 Quicker 动作、脚本和其他 Windows 自动化工具提供了一套不依赖 HTML 组件接口的本地协议。

## 命令参数

```powershell
# 正常启动并显示画布
WidgetCanvas.exe

# 将当前组件索引输出为 UTF-8 JSON
WidgetCanvas.exe --list-widgets

# 写入指定文件，适合 Quicker 等 WinExe 调用方
WidgetCanvas.exe --list-widgets --output "%TEMP%\WidgetCanvas-widgets.json"

# 按 title 显示或聚焦独立组件窗口，匹配不区分大小写
WidgetCanvas.exe --widget "便签"

# 读取 HTML 文件并显示为独立组件窗口，不加入画布或组件库
WidgetCanvas.exe --file "D:\Widgets\clock.html"

# 打开管理中心
WidgetCanvas.exe --settings

# 只驻留托盘
WidgetCanvas.exe --background

# 真正退出应用（等同浮岛右上角 ×）
WidgetCanvas.exe --exit
```

`--list-widgets` 直接读取已经落盘的组件目录，无论主程序是否正在运行都能使用。成功退出码为 `0`，失败为 `2`。组件标题具有唯一性；找不到标题或存在歧义时不会误开其他组件。

`--file` 也接受别名 `--widget-file` 和 `-f`。文件必须是 UTF-8，或带 BOM 的 UTF-16/UTF-32 编码，且是 UTF-8 大小不超过 2 MB 的完整单文件 HTML。它不会写入组件目录；同一路径再次调用会重新读取文件并聚焦已有窗口。窗口布局、置顶、贴边隐藏和 `host.state` 按规范化绝对路径保存在 `%LocalAppData%\浮岛\State\FileWidgets`。关闭窗口不会删除源文件。

## 索引 JSON

```json
{
  "schemaVersion": 1,
  "revision": 12,
  "updatedAtUtc": "2026-07-18T08:00:00+00:00",
  "titles": ["便签", "天气"],
  "components": [
    { "id": "...", "title": "便签", "home": "canvas" },
    { "id": "...", "title": "天气", "home": "library" }
  ]
}
```

一次性查询结果的 `revision` 为 `0`；需要判断变动时，应读取下面的实时索引快照。

## 组件变动通知

组件新增、删除、改名，或者在画布与组件库之间移动后，WidgetCanvas 会先原子更新：

```text
%LocalAppData%\浮岛\Integration\widgets.json
```

写入完成后，再触发当前 Windows 会话内的自动重置命名事件：

```text
Local\WidgetCanvas.ComponentsChanged
```

事件只负责唤醒，JSON 文件才是完整载荷和事实来源。画布位置调整、独立窗口移动或缩放、组件状态写入和网络请求都不会增加索引版本号。

## 推荐的 Quicker 动作流程

普通右键组件菜单不需要让动作长期运行：

1. 执行 `WidgetCanvas.exe --list-widgets --output <临时 JSON 路径>`。
2. 读取 `titles` 数组并生成右键菜单。
3. 用户选择标题后，执行 `WidgetCanvas.exe --widget "<标题>"`。
4. 增加一个固定菜单项，执行 `WidgetCanvas.exe --settings`。

如果动作需要实时刷新，可以在后台等待 `Local\WidgetCanvas.ComponentsChanged`，收到信号后重新读取 `%LocalAppData%\浮岛\Integration\widgets.json`。

## 独立窗口布局隔离

独立组件窗口拥有自己的屏幕坐标和内容区大小。拖动或缩放独立窗口不会覆盖组件原有的画布 X/Y/宽高；收回画布时仍使用原来的画布布局，不会因为外部窗口移动而压到其他组件上。
