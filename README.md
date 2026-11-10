# DigitalLogin

A small Windows desktop app for testing the sign-in against the agyo auth service. It runs the same
two-call flow as the Bruno collection (`agyoUrl` + `/api/v3/nonces` and `/api/v3/tokens`), shows every step
with its status, timing and response, and keeps a history of past runs.

Built with .NET MAUI + Blazor Hybrid and Radzen, with the same look and structure as the AlyCE Log Analyzer.

---

## What it does

1. **Request a nonce** — `POST {Environment URL}/api/v3/nonces` with `{ "id": <Client ID> }`.
2. **Compute the digest** (local):

   ```
   digest = SHA256( SHA256( lower(id) + lower(secret) ) + nonce )
   ```

   Both hashes are lowercase hex over UTF-8. The formula matches the Bruno pre-request script exactly.
3. **Request an access token** — `POST {Environment URL}/api/v3/tokens` with `{ "id": <Client ID>, "digest": <digest> }`.

Each request sends the same headers as the Bruno script: `User-Agent`, `X-User-ID`, `X-App-Name`,
`X-App-Version`, `X-Request-ID` (a GUID without dashes) and `X-Correlation-ID` (the same GUID with dashes).
If a step fails, the run stops there and the later steps are marked as skipped.

## Fields

| Field | Sent to the service | Notes |
| --- | --- | --- |
| Environment (PROD / STAGE) | — | Chosen once for both pages. Login uses `b2b-auth-service(-test).agyo.io`, workspace uses `workspace-read(-test).agyo.io`. Default STAGE. |
| Client ID (`Id`) | Yes: body `id` and header `X-User-ID` | Also part of the digest. |
| Company VAT (`CompanyVat`) | No | Shown in the UI and the history only. |
| Secret | No (used only to compute the digest) | Kept in memory for the session; never saved to disk. |
| Request headers | Yes | `X-App-Name`, `X-App-Version`, `User-Agent`. Collapsed by default. |

## Security

- The **secret is never written** to disk, to the history database, or to the step log.
- The **access token** is shown masked (`eyJabc…wxyz`) with reveal and copy buttons. It is held in memory
  for the current run only and is **not stored** in the history.
- Response bodies shown in the UI have the `accessToken` value replaced with `[redacted]`.

## Run history

Every run is saved to a local SQLite database:

```
%LOCALAPPDATA%\DigitalLogin\history.db
```

It stores the time, environment, Client ID, VAT, request id, result, the failing step and HTTP status, duration
and error text. The newest **200** runs are kept. **Clear history** empties the table.

## Requirements

- Windows 10 (17763) or later, x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download) and the **.NET MAUI** workload (Windows target)
- **Microsoft Edge WebView2 Runtime** (included in Windows 11; the app shows a prompt if it is missing)

## Build and run

Visual Studio 2022/2026 with the .NET MAUI workload:

1. Open `DigitalLogin.sln`.
2. Set `DigitalLogin.Maui` as the startup project and press **F5**.

From a terminal:

```powershell
dotnet build DigitalLogin.sln
dotnet run --project DigitalLogin.Maui
```

Run the tests:

```powershell
dotnet test DigitalLogin.Tests
```

## Using it

1. Open **Login test**.
2. Check the Environment URL, enter the Client ID and Secret, and adjust the request headers if the service
   expects different values (expand **Request headers**).
3. Press **Run login test**. Each step updates as it runs.
4. On success, reveal or copy the token. On failure, the failing step and the service's error body are shown.
5. Open **Run history** to see past runs.

## Project layout

```
DigitalLogin.sln
├── DigitalLogin.Core/            Auth flow, digest, run model, history store, session state (no UI)
│   ├── Auth/                     AgyoAuthClient, AgyoDigest, AgyoLoginRun, AgyoOptions
│   └── Services/                 LoginRunHistory (SQLite), LoginSessionState
├── DigitalLogin.Maui/            Blazor Hybrid app (MAUI host, pages, layout, styles)
│   ├── Components/Pages/         LoginTest.razor, History.razor
│   ├── Components/Layout/        MainLayout, NavMenu
│   ├── wwwroot/                  app.css, index.html, storage.js (clipboard + local storage)
│   └── Platforms/Windows/        Unpackaged Windows host, WebView2 runtime check
└── DigitalLogin.Tests/           xUnit: digest, auth flow (fake HTTP), history store
```

## Notes

- The digest lowercases both the id and the secret, as the Bruno script does. If the real service treats the
  secret as case-sensitive, this is the first thing to check.
- The app is unpackaged and Windows-only. The app icon is a placeholder padlock (`Resources/AppIcon/`).

## Workspace info

The **Workspace info** page reads one workspace from the Workspace Read API (`GET {Workspace URL}/api/v2/workspaces/{id}`,
spec at `https://workspace-read.agyo.io/v3/api-docs`). It shows the main fields and the raw response.

- The bearer token is the access token from the last **successful** login test in this session. It is kept in memory
  only and can be replaced by pasting another token.
- The gateway requires an `X-Workspace-ID` header on this route, so the page sends the workspace ID there as well.
- Only the workspace is shown for now; the other Workspace Read endpoints can be added as more sections.

## Company registry

The **Company registry** page finds the company by its VAT number on the Registry Read API
(`GET {Registry URL}/api/v3/items?identifier.vatNumber={VAT}&packageType=BASE`, spec in the `registry-read` swagger).

- The VAT is the **Company VAT** typed on the Login test page (shared through the session).
- The access token is the one from the last successful login test.
- A single match is set as the workspace ID automatically; with several matches, pick one with "Use as workspace ID".
- Registry URLs: PROD `https://registry-read.agyo.io`, STAGE `https://registry-read-test.agyo.io`.
