# Quicker 动作版

动作版使用 .NET 10 Windows x64 共享运行时，保留 WebView2 托管依赖并把原生加载器嵌入单个 EXE。接收机器需要 .NET 10 Desktop Runtime 和 Microsoft Edge WebView2 Runtime。

从仓库根目录发布到新的版本目录：

```powershell
dotnet test tests\WidgetCanvas.Tests\WidgetCanvas.Tests.csproj -c Release
dotnet publish src\WidgetCanvas\WidgetCanvas.csproj -p:PublishProfile=Quicker --self-contained false -o artifacts\v0.5.4\quicker
```

动作下载 `WidgetCanvas.exe`，并将其保存到 `_context.GetActionDataDirectory()` 返回的动作数据目录。应用仍使用既有的“文档\浮岛”和 `%LocalAppData%\浮岛` 保存组件、设置及状态，移动 EXE 不需要迁移用户数据。

动作版由 Quicker 动作更新程序文件，跳过 Gitee/GitHub 自动更新；管理中心会显示动作更新方式，已有独立版的更新偏好不会被改写。独立发布仍沿用原有自包含构建与更新渠道。

`WidgetCanvas.exe --exit` 会等待主实例释放单实例互斥量，最多等待 15 秒；退出指令转发失败或等待超时返回非零退出码。没有运行实例时直接退出，不打开界面。动作替换文件前应等待退出命令完成并检查结果，下载失败时保留当前可用程序。

验证界面时可为进程设置 `WIDGETCANVAS_PROFILE_ROOT` 为绝对路径。组件写入该目录的 `Documents`，设置、状态与 WebView2 数据写入 `Local`，用于隔离测试，避免触发真实配置中的自动同步。未设置时继续使用原用户目录；同一 Windows 会话内仍只运行一个实例。
