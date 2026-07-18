# Security

## Reporting a vulnerability

Please report security issues privately through GitHub's security advisory feature rather than opening a public issue. Include a minimal reproduction, affected version, and expected impact.

## Widget trust model

Widgets are local HTML and JavaScript with visible source, but visible source is not automatically safe source. A widget may use documented host methods to read local files, access the network, copy text, and launch programs.

Review third-party widget HTML before running it. Do not run widgets that contain unknown executable paths, encoded scripts you cannot inspect, credential collection, or unexpected network destinations.

WidgetCanvas does not expose arbitrary file writes, file deletion, registry writes, or a shell-command-string API. `process.start` and `process.run` can still launch any available executable, including command interpreters, and should only be used by trusted widgets.
