; DigitalLogin - Inno Setup Script
; Compile with: iscc setup.iss
; Or override version: iscc /DMyAppVersion=1.2.3 setup.iss

#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif

#define MyAppName      "DigitalLogin"
#define MyAppPublisher "TeamSystem"
#define MyAppURL       "https://github.com/gianpaoloi/DigitalLogin"
#define MyAppExeName   "DigitalLogin.Maui.exe"

; Source directory: output of `dotnet publish -c Release -f net10.0-windows10.0.19041.0 --runtime win-x64 --self-contained`
; This path is relative to the location of this .iss file (DigitalLogin.Maui/installer/)
#define PublishDir "..\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"

[Setup]
; Fixed for the life of the product: Inno uses it to recognise an earlier install and upgrade it
; in place. Do not regenerate it.
AppId={{4C2DBDF1-44FB-4307-8B8F-3D8AD58C0F6C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={localappdata}\TeamSystem\DigitalLogin
DefaultGroupName=TeamSystem\DigitalLogin
AllowNoIcons=yes
OutputDir=..\dist
OutputBaseFilename=DigitalLogin-Setup-{#MyAppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
; Per-user install - no UAC / admin rights required
PrivilegesRequired=lowest
; Windows-only, x64
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Minimum: Windows 10 1809 (matches MAUI project SupportedOSPlatformVersion)
MinVersion=10.0.17763

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; All self-contained publish output (runtime, native libs, assets)
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}";                            Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}";      Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}";                      Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; \
  Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; \
  Flags: nowait postinstall skipifsilent

; Restart the app after it updated itself.
;
; The entry above is "postinstall skipifsilent", so a silent install offers nothing and starts nothing,
; which is correct for winget but would mean the app downloads an update, closes to let Setup replace it,
; and never comes back. This entry has no "postinstall", so it fires in silent mode too, and only when the
; app asked for the install by passing /UPDATED=1.
Filename: "{app}\{#MyAppExeName}"; Flags: nowait; Check: RelaunchAfterSelfUpdate

; ---------------------------------------------------------------------------
; Microsoft Edge WebView2 Runtime
;
; The app's UI is Blazor in a WebView2, and that runtime is a separate OS
; component: bundled with Windows 11, but frequently absent on Windows 10. If
; it is missing the app cannot start, so Setup fetches the Evergreen
; bootstrapper and runs it silently. PrepareToInstall is used (rather than a
; [Run] entry) because it also executes during /SILENT and /VERYSILENT
; installs, which is how winget would install the package.
;
; A failure here is reported but does not abort the install - the app then
; explains the same thing on first launch (Platforms/Windows/WebView2Runtime.cs).
; ---------------------------------------------------------------------------
[Code]
const
  WebView2ClientKey = 'Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  WebView2BootstrapperUrl = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703';
  WebView2BootstrapperFile = 'MicrosoftEdgeWebview2Setup.exe';

{ True when Setup was started by the app's own updater (WindowsUpdateInstaller passes /UPDATED=1), which is
  the only case where Setup should start the app by itself. A hand-run silent install stays as it was. }
function RelaunchAfterSelfUpdate: Boolean;
begin
  Result := ExpandConstant('{param:UPDATED|0}') = '1';
end;

{ True when the key holds a real version; an empty or all-zero "pv" is left behind by an uninstall. }
function HasWebView2Version(RootKey: Integer; const SubKeyName: String): Boolean;
var
  Version: String;
begin
  Result := RegQueryStringValue(RootKey, SubKeyName, 'pv', Version) and
            (Version <> '') and (Version <> '0.0.0.0');
end;

function WebView2Installed: Boolean;
begin
  Result := HasWebView2Version(HKLM, 'SOFTWARE\WOW6432Node\' + WebView2ClientKey) or
            HasWebView2Version(HKLM, 'SOFTWARE\' + WebView2ClientKey) or
            HasWebView2Version(HKCU, 'SOFTWARE\' + WebView2ClientKey);
end;

function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  if ProgressMax <> 0 then
    Log(Format('WebView2 bootstrapper: %d of %d bytes downloaded.', [Progress, ProgressMax]))
  else
    Log(Format('WebView2 bootstrapper: %d bytes downloaded.', [Progress]));
  Result := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  if WebView2Installed then
    Exit;

  try
    DownloadTemporaryFile(WebView2BootstrapperUrl, WebView2BootstrapperFile, '', @OnDownloadProgress);
  except
    SuppressibleMsgBox(
      'Setup could not download the Microsoft Edge WebView2 Runtime:' + #13#10#13#10 +
      GetExceptionMessage + #13#10#13#10 +
      'Installation will continue, but DigitalLogin will not start until the runtime ' +
      'is installed from https://developer.microsoft.com/microsoft-edge/webview2/',
      mbError, MB_OK, IDOK);
    Exit;
  end;

  { The bootstrapper installs per-user when not elevated, which suits this per-user setup. }
  if not Exec(ExpandConstant('{tmp}\' + WebView2BootstrapperFile), '/silent /install', '',
              SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
    SuppressibleMsgBox(
      'The Microsoft Edge WebView2 Runtime could not be installed automatically (code ' +
      IntToStr(ResultCode) + ').' + #13#10#13#10 +
      'Installation will continue. If the app does not start, install the runtime from ' +
      'https://developer.microsoft.com/microsoft-edge/webview2/',
      mbError, MB_OK, IDOK);
end;
