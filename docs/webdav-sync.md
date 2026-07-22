# WebDAV sync

WidgetCanvas can synchronize widgets through a user-provided WebDAV directory. The feature is disabled by default, requires no WidgetCanvas account, and does not send data through a WidgetCanvas-operated server.

## Setup

1. Open **Management Center** from the tray icon.
2. Enter a WebDAV service URL or parent directory plus the credentials. HTTPS and an app-specific password are recommended.
3. Select **Test connection**. WidgetCanvas creates and uses a dedicated `WidgetCanvas` directory below the entered URL. If the URL already ends in `WidgetCanvas`, that directory is used directly.
4. Select **Sync now**. Enable automatic sync and save if continuous synchronization is desired.

The URL must point to an existing HTTP or HTTPS WebDAV directory. WidgetCanvas maintains one file inside its managed directory:

```text
widgetcanvas-sync-v1.json
```

The server must support WebDAV `PROPFIND`, `MKCOL`, plus `GET` and `PUT` for the sync file. Username/password authentication currently uses HTTP Basic Authentication, so HTTPS should be preferred.

## What is synchronized

Synchronized:

- stable widget IDs;
- complete single-file HTML;
- widget-owned `host.state` data, such as notes, tasks, and widget preferences.

Kept on each device:

- named canvases, active canvas, and each widget's local canvas assignment, position, size, and z-order;
- detached-window bounds, topmost state, and edge auto-hide settings;
- WebView2 cache, cookies, and sessions;
- application settings, hotkeys, logs, and local files;
- WebDAV URL and credentials.

Widgets first received from another device enter the component library instead of appearing directly on the canvas. Existing widgets retain each device's local layout and window options.

## Automatic schedule

When enabled, synchronization is checked:

- shortly after startup;
- about 15 seconds after widget data settles;
- every five minutes while the application is running.

A network failure never deletes or rolls back local data. The error is logged and a later automatic or manual sync can retry.

## Deletions and conflicts

WidgetCanvas performs a three-way merge against the last successful local sync baseline:

- additions, edits, and deletions made on only one device propagate normally;
- if one device deletes a widget while another edits it, the edited widget is retained;
- if both devices edit the same widget differently, the local version is kept and the remote version is imported into the library as a separate “from another device” copy;
- title collisions receive a numeric suffix so `--widget "title"` remains unambiguous.

Uploads use the ETag returned by WebDAV for conditional writes. If another device changes the remote file just before upload, WidgetCanvas reads and merges again instead of blindly overwriting it.

## Local security

The WebDAV password is protected with Windows DPAPI for the current user and stored in:

```text
%LocalAppData%\浮岛\Settings\settings.json
```

The local merge baseline is stored in:

```text
%LocalAppData%\浮岛\Sync\webdav-base.json
```

A DPAPI-protected password cannot simply be copied to another computer; credentials must be entered on each device. The remote sync file contains widget HTML and widget state, so use a WebDAV provider you trust if widgets contain sensitive data.
