#nullable enable

using System.Text;

namespace WidgetCanvas.HtmlWidgets
{
    internal static class HtmlWidgetCanvasPrompt
    {
        public static string BuildCreateTemplate()
        {
            var text = new StringBuilder();
            AppendCommonRequirements(text, null, null);
            text.AppendLine();
            text.AppendLine("组件需求：");
            text.AppendLine("（请将具体需求写在这里，或直接接在本提示词后面。）");
            return text.ToString();
        }

        public static string BuildCreate(double width, double height, string? request = null)
        {
            var text = new StringBuilder();
            AppendCommonRequirements(text, width, height);
            text.AppendLine();
            text.AppendLine("请创建的组件：");
            text.AppendLine(string.IsNullOrWhiteSpace(request)
                ? "（请在这里补充具体需求）"
                : request.Trim());
            return text.ToString();
        }

        public static string BuildEdit(HtmlWidgetDefinition widget, string? request = null)
        {
            var text = new StringBuilder();
            text.AppendLine("请修改下面的桌面 HTML 小组件，并输出修改后的完整代码。");
            AppendCommonRequirements(text, widget.Width, widget.Height);
            text.AppendLine();
            text.AppendLine("当前 HTML（以下边界标记不是代码的一部分）：");
            text.AppendLine("===== CURRENT_HTML_BEGIN =====");
            text.AppendLine(widget.Html);
            text.AppendLine("===== CURRENT_HTML_END =====");
            text.AppendLine();
            text.AppendLine("编辑原则：除非修改要求明确要求重命名，否则保留原 <title>；只修改需求涉及部分，保留其他功能以及已有 state 键和数据结构。");
            text.AppendLine();
            text.AppendLine("修改要求：");
            text.AppendLine(string.IsNullOrWhiteSpace(request)
                ? "（请将具体修改要求写在这里，或直接接在本提示词后面。）"
                : request.Trim());
            return text.ToString();
        }

        private static void AppendCommonRequirements(
            StringBuilder text,
            double? width,
            double? height)
        {
            text.AppendLine("请生成一个运行在 Windows 桌面浮层中的 HTML 小组件。用户需求在提示词末尾；下面的宿主约束优先。");
            text.AppendLine();
            text.AppendLine("交付规则：");
            text.AppendLine("1. 只输出一个 ```html 代码块，代码块外不要写任何内容。");
            text.AppendLine("2. 交付可直接运行且 UTF-8 大小不超过 2 MB 的完整单文件：<!doctype html>、html、head、meta charset、meta viewport、非空且简短的中文 title、style、body、script；title 是组件唯一显示名称。不能留 TODO、假数据或无效按钮。");
            text.AppendLine("3. 禁止 CDN、npm、外部字体/脚本/图片和其他文件依赖；图标用内联 SVG 或 CSS。不要硬编码密钥。");
            text.AppendLine("4. html、body 必须 width:100%;height:100%;margin:0;overflow:hidden;background:transparent，所有元素 box-sizing:border-box。布局响应容器变化；需要滚动时仅内容区滚动。");
            text.AppendLine("5. script 放在 body 内容之后；若放在 head，等待 DOMContentLoaded。先取得 DOM 元素再绑定事件和初始化，不能用任意延时等待 DOM。");
            text.AppendLine("6. 不绘制宿主外框、拖动/缩放手柄、关闭按钮或标题栏，不占用 Esc。默认中文和 \"Microsoft YaHei\",sans-serif。");
            text.AppendLine("7. UI 紧凑、舒适、功能优先；控件有 hover/active/focus/disabled，图标按钮有 title 或 aria-label。不要欢迎页、教程、大段说明、套娃卡片或无意义装饰。");
            text.AppendLine("8. 不用 alert/confirm/prompt；加载、空状态、确认和错误在组件内轻量呈现。异步期间防止重复提交。");
            text.AppendLine();
            text.AppendLine("宿主接口：");
            text.AppendLine("- 使用 const host = window.widgetHost。只存在下面列出的方法，均返回 Promise；参数和值必须可 JSON 序列化。不要猜测其他接口，也不要访问 chrome.webview、postMessage 或自行实现通信桥接。");
            text.AppendLine("- 每次宿主调用都用 try/catch。界面可显示短错误，但必须把 String(error?.message || error) 放在错误元素的 title 或可展开详情中。顶层异步初始化也必须捕获，不能白屏或产生未处理的 Promise rejection。");
            text.AppendLine();
            text.AppendLine("状态（每个组件实例隔离，值是原生 JSON，不要 stringify/parse）：");
            text.AppendLine("- host.state.read(key, defaultValue)；host.state.write(key, value)；host.state.remove(key)；host.state.clear()；host.state.flush() 仅在必须立即落盘时调用。");
            text.AppendLine("- 输入、待办、开关和设置每次变化后立即 write；不要在 JS 中延迟或防抖保存，宿主已负责合并磁盘写入。持久化只用 state，不用 localStorage、sessionStorage、IndexedDB 或 Cookie。");
            text.AppendLine();
            text.AppendLine("常用能力（按需使用）：");
            text.AppendLine("- host.clipboard.read() -> string；host.clipboard.write(text)。不要用 navigator.clipboard。");
            text.AppendLine("- host.url.open({url,hideAfterOpen?}) 用系统浏览器打开 http/https；host.path.open({path,hideAfterOpen?}) 用系统关联程序打开已存在的文档或目录；启动可执行程序用 process.start。host.window.hide() 隐藏当前承载窗口。");
            text.AppendLine("- host.process.start({file,args?,workingDirectory?,windowStyle?,hideAfterStart?}) -> {started,processId}。file 是绝对路径或 PATH 中的可执行文件，args 必须是字符串数组；windowStyle 可为 normal、hidden、minimized、maximized。仅在需求明确时启动程序，不拼接 shell 命令；成功后需要收起浮层时传 hideAfterStart:true。");
            text.AppendLine("- 一次性调用本地 CLI 并读取结果时使用 host.process.run({file,args?,workingDirectory?,input?,timeoutMs?,maxOutputBytes?}) -> {exitCode,stdout,stderr,timedOut,truncated}；同样只传 file 与字符串 args[]，不要拼接 shell 命令。input 最多 1 MB；timeoutMs 范围 100 毫秒至 10 分钟，默认 15 秒；stdout 与 stderr 合计默认 1 MB、最多 10 MB。非零退出码正常返回；超时会终止进程树且 exitCode 为 null。先检查 timedOut、truncated 和 exitCode；不要用它启动长期服务。");
            text.AppendLine("- 禁止 window.open、普通链接跳转和修改 location。搜索、书签、打开文件等一次性动作使用 hideAfterOpen:true，让浮层在成功打开后隐藏。");
            text.AppendLine();
            text.AppendLine("网络（需要远程数据时）：");
            text.AppendLine("- 只能用 host.http.get({url,headers?,timeoutMs?,maxBytes?})、host.http.post({url,body?,contentType?,headers?,timeoutMs?,maxBytes?}) 或 host.http.request({url,method?,body?,contentType?,headers?,timeoutMs?,maxBytes?})，不能 fetch/XMLHttpRequest。body 是字符串；JSON 用 JSON.stringify，非 JSON 明确指定 contentType。");
            text.AppendLine("- 返回 {ok,status,statusText,text,contentType,finalUrl,headers,truncated}。先检查 ok；4xx/5xx 不 reject，网络/超时会 reject；truncated 为 true 时不要解析；JSON.parse 用 try/catch。宿主不提供网页登录态，也不能绕过鉴权、限流或反爬。");
            text.AppendLine("- 自动刷新至少间隔 60 秒，并提供手动刷新；失败显示真实原因和重试入口。");
            text.AppendLine();
            text.AppendLine("只读文件（确有文件需求时）：");
            text.AppendLine("- host.fs.getKnownFolders() -> {userProfile,desktop,documents,downloads,pictures,music,videos,appData,localAppData,temp}，需要访问常用用户目录时先读取，不要猜测或硬编码用户名。");
            text.AppendLine("- host.fs.exists({path}) -> {exists,type,path}，type 为 file、folder 或 null；host.fs.getInfo({path}) -> fileInfo。");
            text.AppendLine("- host.fs.readText({path,maxBytes?,encoding?}) -> {path,name,extension,encoding,text,truncated}；host.fs.readBase64({path,maxBytes?}) -> {path,name,extension,mime,base64,truncated}。");
            text.AppendLine("- host.fs.list({path,pattern?,recursive?,includeFiles?,includeFolders?,includeHidden?,maxItems?}) -> {path,count,recursive,pattern,truncated,items}。");
            text.AppendLine("- host.fs.selectFile({title?,filter?,defaultExtension?,defaultFileName?,initialDirectory?})、host.fs.selectFolder({title?,initialDirectory?}) -> fileInfo，取消返回 null；filter 使用 Windows 的 `名称|*.ext;*.ext|所有文件|*.*` 格式。fileInfo 为 {type,path,name,extension,size,createdAt,modifiedAt}。使用 readText、readBase64、list 的结果前检查 truncated；不完整内容不要解析 JSON 或拼 data URL。本地图片可拼成 `data:${result.mime};base64,${result.base64}`；没有写文件接口。");
            text.AppendLine();
            text.AppendLine("运行与自检：");
            text.AppendLine("- 当前承载窗口隐藏后页面不会销毁，计时和状态继续运行；但组件在画布、独立窗口和编辑之间切换时会重建页面，初始化必须完整从 state 恢复。避免高频轮询和无界内存增长。");
            text.AppendLine("- 输出前静默检查：DOM 时机正确、所有按钮可用、没有虚构接口/外部依赖/页面级滚动条，状态能恢复，异步失败有可见反馈。不要输出检查过程。");
            text.AppendLine();
            if (width.HasValue && height.HasValue)
            {
                text.AppendLine($"组件可用区域：宽 {width:0} DIP，高 {height:0} DIP。请按这个尺寸设计信息密度和布局。");
            }
            else
            {
                text.AppendLine("组件区域由用户稍后在浮岛中拖动决定。请使用响应式布局，让内容在常见的小组件尺寸下都能自然排列。");
            }
        }
    }
}
