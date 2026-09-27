# Security and data handling

NoteSpace 0.1.0 is an early local-first application, not a security-qualified enterprise notebook service. Do not store credentials or other high-sensitivity information without an independent review of your environment and requirements.

## Storage boundary

Browser notebooks live in IndexedDB for the current origin/profile. They are not encrypted by NoteSpace. Other scripts executing with the same origin privileges may access them. Desktop notebooks are JSON files in the current user's application-data directory and rely on operating-system access controls. A cleared browser profile or storage eviction can delete local notes. Export backups regularly.

## Input and output

The model validates schema versions, duplicate identifiers, collection sizes, text ranges, finite geometry, and image/attachment bounds. HTML exports encode user text and accept only explicit http/https/mailto link schemes. Raster images are decoded only after inspecting their dimensions. Attachments are retained as bytes and are not executed by the app. Downloading and opening an attachment transfers responsibility to the host application and operating system.

There is no arbitrary HTML/JavaScript execution feature, server credential store, account impersonation, or pretend authentication. Microsoft notebook formats and cloud synchronization are not implemented.

## Conflicts and failures

Cross-tab writes use atomic IndexedDB token comparison. A conflict does not merge content; the editor stops saving and asks for an export/reload. Invalid existing storage is not silently replaced. Failures are surfaced in the application status and dialogs. Closing the tab while a draft is dirty may trigger a browser unload warning, but browser shutdown/crashes can still lose unsaved work.

## Reporting

Report non-sensitive defects through GitHub issues. For a security vulnerability, use the repository's private security reporting feature where enabled; do not post notebook contents, secrets, or exploitable private data in a public issue. Include the commit, target platform, steps, and a synthetic sample when possible.
