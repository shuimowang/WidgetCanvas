# Widget host API

[中文说明](../README.md) · [English README](../README.en.md)

Every widget receives one global object:

```js
const host = window.widgetHost;
```

All 23 methods return a `Promise`. Arguments and values must be JSON-serializable. Host calls should be wrapped in `try/catch`; network and system failures reject with an `Error`.

Do not access `chrome.webview`, post raw messages, use `window.open`, or invent methods not listed here.

## State

State is isolated per widget instance and stored as native JSON values.

| Method | Result | Notes |
|---|---|---|
| `host.state.read(key, defaultValue)` | saved value or `defaultValue` | Returns a clone of the stored JSON value. |
| `host.state.write(key, value)` | `true` | Updates memory immediately; disk writes are debounced by the host. |
| `host.state.remove(key)` | `boolean` | Reports whether the key existed. |
| `host.state.clear()` | `number` | Returns the number of removed keys. |
| `host.state.flush()` | `true` | Forces pending state to disk. Use only when immediate persistence matters. |

Use state for user input, settings, switches, lists, and cached results. Do not use `localStorage`, IndexedDB, session storage, or cookies for persistent widget data.

## Clipboard

| Method | Result |
|---|---|
| `host.clipboard.read()` | clipboard text, or an empty string |
| `host.clipboard.write(text)` | `true` |

Only plain text is supported.

## Open resources and control the host window

```js
await host.url.open({
  url: "https://example.com",
  hideAfterOpen: true
});

await host.path.open({
  path: "C:\\Users\\me\\Documents\\notes.md",
  hideAfterOpen: true
});

await host.window.hide();
```

| Method | Notes |
|---|---|
| `host.url.open({url, hideAfterOpen?})` | Opens an `http` or `https` URL in the default browser. |
| `host.path.open({path, hideAfterOpen?})` | Opens an existing file or directory with its Windows association. |
| `host.window.hide()` | Hides the current canvas or detached widget window without destroying its page. |

Normal links, `window.open`, and page navigation are blocked. Use these explicit methods instead.

## HTTP

```js
const response = await host.http.request({
  url: "https://example.com/api",
  method: "POST",
  headers: { "X-Client": "widget" },
  body: JSON.stringify({ value: 1 }),
  contentType: "application/json",
  timeoutMs: 15000,
  maxBytes: 1048576
});
```

Available methods:

- `host.http.get({url, headers?, timeoutMs?, maxBytes?})`
- `host.http.post({url, body?, contentType?, headers?, timeoutMs?, maxBytes?})`
- `host.http.request({url, method?, body?, contentType?, headers?, timeoutMs?, maxBytes?})`

All return:

```js
{
  ok: true,
  status: 200,
  statusText: "OK",
  text: "...",
  contentType: "application/json",
  finalUrl: "https://example.com/api",
  headers: { "content-type": "application/json" },
  truncated: false
}
```

HTTP 4xx and 5xx responses resolve with `ok: false`; connection, timeout, and transport failures reject. Never parse a response that reports `truncated: true`. The host does not share browser cookies or authenticated website sessions.

## Known folders and read-only files

### `host.fs.getKnownFolders()`

Returns redirected Windows known-folder paths:

```js
{
  userProfile,
  desktop,
  documents,
  downloads,
  pictures,
  music,
  videos,
  appData,
  localAppData,
  temp
}
```

Use this method instead of hard-coding a user name or assuming `C:\Users`.

### Inspection and reading

| Method | Result |
|---|---|
| `host.fs.exists({path})` | `{exists, type, path}` where `type` is `file`, `folder`, or `null` |
| `host.fs.getInfo({path})` | `fileInfo` |
| `host.fs.readText({path, maxBytes?, encoding?})` | `{path,name,extension,encoding,text,truncated}` |
| `host.fs.readBase64({path, maxBytes?})` | `{path,name,extension,mime,base64,truncated}` |
| `host.fs.list(options)` | directory listing result |

`host.fs.list` options:

```js
{
  path,
  pattern: "*.json",     // optional
  recursive: false,      // optional
  includeFiles: true,    // optional
  includeFolders: true,  // optional
  includeHidden: false,  // optional
  maxItems: 500          // optional
}
```

It returns `{path,count,recursive,pattern,truncated,items}`. Every item uses:

```js
{
  type: "file",          // or "folder"
  path: "...",
  name: "...",
  extension: ".json",
  size: 1024,
  createdAt: "...",
  modifiedAt: "..."
}
```

Check `truncated` before parsing text, constructing a Base64 data URL, or assuming a directory listing is complete.

### User selection

- `host.fs.selectFile({title?, filter?, defaultExtension?, defaultFileName?, initialDirectory?})`
- `host.fs.selectFolder({title?, initialDirectory?})`

Both return `fileInfo`, or `null` when cancelled. File filters use Windows syntax, for example `JSON|*.json|All files|*.*`.

WidgetCanvas deliberately exposes no arbitrary file-write or delete method.

## Processes

### Start a GUI or long-running program

```js
const result = await host.process.start({
  file: "notepad.exe",
  args: ["C:\\Users\\me\\Documents\\notes.txt"],
  workingDirectory: "C:\\Users\\me\\Documents",
  windowStyle: "normal",
  hideAfterStart: true
});
```

Returns `{started, processId}`. `windowStyle` accepts `normal`, `hidden`, `minimized`, or `maximized`.

### Run a CLI and collect its output

```js
const result = await host.process.run({
  file: "git",
  args: ["status", "--short"],
  workingDirectory: "C:\\Projects\\demo",
  input: "",
  timeoutMs: 15000,
  maxOutputBytes: 1048576
});
```

Returns:

```js
{
  exitCode: 0,
  stdout: "...",
  stderr: "",
  timedOut: false,
  truncated: false
}
```

Rules and limits:

- `file` is an executable path or a program discoverable through `PATH`.
- `args` must be an array of strings. WidgetCanvas never accepts a concatenated shell command.
- Standard input is optional and limited to 1 MiB.
- Timeout defaults to 15 seconds and accepts 100 ms through 10 minutes.
- stdout and stderr share a default 1 MiB, maximum 10 MiB budget.
- A non-zero exit code resolves normally. A timeout terminates the process tree and returns `exitCode: null`.
- Process creation failures reject.
- Use `process.start` for long-running services; use `process.run` only for bounded commands.

## Runtime behavior

- Hiding a canvas or detached widget does not destroy its current page.
- Moving a widget between canvas, editor, library, and detached window may rebuild the page. Initialization must restore state completely.
- Library preview is read-only: state mutations and system actions are simulated or ignored.
- Avoid high-frequency polling and unbounded in-memory history.
