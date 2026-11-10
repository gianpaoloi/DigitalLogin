# Changelog

All notable changes to **DigitalLogin**, newest first. Entries reference the commit they come from.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Each released version has a
section here, and that section *is* what the release workflow publishes as the release notes — see
[Releasing](README.md#releasing). Anything merged since the last tag is collected under *Unreleased* until
`set-version.ps1` moves it under a version heading.

## [Unreleased]

_Nothing yet._

## [1.0.0] — 2026-11-10

### Added

- **Login test against the agyo auth service.** The **Login test** page runs the same two-call flow as the
  Bruno collection: it requests a nonce, computes the digest locally, then requests an access token. Every
  step shows its status, timing and response. A failing step stops the run and marks the later steps as
  skipped. Environment (PROD / STAGE) is chosen once for the whole app and defaults to STAGE.
- **Secret handling.** The secret is used only to compute the digest. It is kept in memory for the session
  and is never written to disk, to the history database or to the step log. The access token is shown
  masked, with reveal and copy buttons, and is not stored in the history. Response bodies have the
  `accessToken` value replaced with `[redacted]`.
- **Copy as cURL.** Each request can be copied to the clipboard as a ready-to-run `curl` command.
- **Run history.** Every run is saved to a local SQLite database at `%LOCALAPPDATA%\DigitalLogin\history.db`.
  The newest 200 runs are kept, and **Clear history** empties the table. The **History** page lists them.
- **Workspace info.** The **Workspace info** page reads one workspace from the Workspace Read API, using the
  access token from the last successful login test in the session. It can also be given a token by hand.
- **Company registry.** The **Company registry** page finds a company by its VAT number on the Registry Read
  API. A single match is set as the workspace ID automatically.
- **WebView2 runtime check.** The app explains and offers the download page when the Microsoft Edge WebView2
  Runtime is missing, instead of failing before a window appears.
- **Windows installer.** `DigitalLogin-Setup-<version>.exe` installs per user (no administrator rights),
  adds Start-menu entries, and installs the WebView2 runtime when it is missing.
- **Automatic updates.** The app checks GitHub for a newer release on start-up (at most every six hours) and
  shows **Update to vX** in the sidebar. **Download and install** fetches the setup package, checks it against
  the SHA256 published in the release notes, and runs it. The app closes, the installer replaces it, and the
  new version starts. The **check for updates** button in the sidebar forces a fresh check. Only copies
  installed by the setup package update themselves; a portable copy is pointed at the releases page.
- **Portable ZIP.** Each release also ships `DigitalLogin-<version>-win-x64.zip`: extract it anywhere and run
  `Launch.bat`. It is built from the same publish output as the installer.

### Fixed

- **Environment selector: the options were unreadable.** The dropdown list opened white with light text, so
  PROD could not be seen. The browser now draws the list dark.