# Contributing

Thanks for helping improve WidgetCanvas.

## Development setup

- Windows 10 or 11 x64
- .NET 10 SDK
- Microsoft Edge WebView2 Runtime

```powershell
git clone https://github.com/shuimowang/WidgetCanvas.git
cd WidgetCanvas
dotnet restore WidgetCanvas.slnx
dotnet test tests\WidgetCanvas.Tests\WidgetCanvas.Tests.csproj -c Debug
dotnet run --project src\WidgetCanvas\WidgetCanvas.csproj
```

## Pull requests

- Keep changes focused and explain user-visible behavior.
- Add or update tests when storage, prompt text, or host API behavior changes.
- Keep the AI prompt and `docs/host-api.md` aligned with the implementation.
- Do not add host methods without a concrete widget use case and a clear security boundary.
- Avoid native HWND parent/style/Z-order changes around WebView2 initialization.
- Do not commit generated `bin`, `obj`, publish, cache, or user data directories.

## Widget examples

Example widgets should be complete single-file HTML documents without CDN, npm, external font, script, or image dependencies. Use inline SVG or CSS for icons. A useful example must handle loading, empty, and error states and must not contain API keys or private data.

By contributing, you agree that your contribution is licensed under the MIT License.
