# External integration

WidgetCanvas exposes a small, tool-neutral protocol for Quicker actions, scripts, launchers, and other Windows automation tools. It does not require a browser bridge or access to widget HTML.

## Commands

Assume `WidgetCanvas.exe` is the installed executable.

```powershell
# Start normally and show the canvas
WidgetCanvas.exe

# Return the current component index as UTF-8 JSON
WidgetCanvas.exe --list-widgets

# Write the same JSON to a file (useful for WinExe callers such as Quicker)
WidgetCanvas.exe --list-widgets --output "%TEMP%\WidgetCanvas-widgets.json"

# Show or focus one standalone widget; title matching is case-insensitive
WidgetCanvas.exe --widget "便签"

# Open Management Center
WidgetCanvas.exe --settings

# Start in tray-only mode
WidgetCanvas.exe --background
```

`--list-widgets` reads the persisted catalog directly and exits, so it works whether the main application is running or not. It returns exit code `0` on success and `2` on failure. Component titles are unique; a missing or ambiguous `--widget` title is reported instead of opening the wrong component.

## Component index JSON

The command and live snapshot use this schema:

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

The one-shot `--list-widgets` result uses revision `0`; consumers that need change tracking should use the live snapshot.

## Change notification

After a component is added, deleted, renamed, or moved between Canvas and Library, WidgetCanvas first atomically updates:

```text
%LocalAppData%\浮岛\Integration\widgets.json
```

It then signals this named auto-reset event in the current Windows session:

```text
Local\WidgetCanvas.ComponentsChanged
```

Layout moves, standalone-window moves, resize operations, widget state writes, and HTTP activity do not increment the component-index revision. A listener should wait for the event and then read the snapshot; the event is only a wake-up signal, while the JSON file is the payload and source of truth.

## Suggested Quicker action flow

For a right-click component menu, no long-running listener is required:

1. Run `WidgetCanvas.exe --list-widgets --output <temporary-json-path>`.
2. Read the `titles` array and build the menu.
3. When a title is selected, run `WidgetCanvas.exe --widget "<title>"`.
4. Add a fixed menu item that runs `WidgetCanvas.exe --settings`.

If an always-running Quicker action needs immediate refresh, wait on `Local\WidgetCanvas.ComponentsChanged` in its background code, then reread `%LocalAppData%\浮岛\Integration\widgets.json`. Do not infer a change from the source catalog while WidgetCanvas is writing it.

## Standalone layout isolation

A standalone widget stores its own screen rectangle and content size. Moving or resizing it does not overwrite the component's Canvas X/Y/width/height. Returning the widget to Canvas therefore restores its previous canvas layout instead of moving it over another component.
