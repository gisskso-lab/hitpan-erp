; ============================================================
; HitPan ERP — Simplified Installer (Inno Setup 6.x)
; 2026-06-09 첫 EXE 시험용 단순 버전
; 멀티사업자 BootstrapInstall.ps1 호출 방식
; ============================================================

#ifndef AppVersion
  #define AppVersion "1.2.0"
#endif

#ifndef BackofficeApi
  #define BackofficeApi "https://back.hitpan.kr"
#endif

#define OutputName "HitPan-ERP-Setup-" + AppVersion
#define OutputDir "..\dist"
#define BundleDir "..\installer-build\bundle"

[Setup]
AppId={{F4E2A1D0-7BC8-4F31-9E5A-6D8C4B1E2F36}
AppName=HitPan ERP
AppVersion={#AppVersion}
AppPublisher=HitPan
DefaultDirName={autopf}\HitPan
DefaultGroupName=HitPan ERP
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputDir={#OutputDir}
OutputBaseFilename={#OutputName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
WizardSizePercent=120
ShowLanguageDialog=no
LanguageDetectionMethod=none
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Files]
Source: "{#BundleDir}\api\*"; DestDir: "{app}\api"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#BundleDir}\web\*"; DestDir: "{app}\web"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#BundleDir}\watchdog\*"; DestDir: "{app}\watchdog"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#BundleDir}\scripts\*"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "{#BundleDir}\cloudflared.exe"; DestDir: "{app}"; Flags: ignoreversion

[Code]
var
  SerialPage: TInputQueryWizardPage;
  G_LicenseKey: String;

procedure InitializeWizard;
begin
  SerialPage := CreateInputQueryPage(wpWelcome,
    'Serial Key Input',
    'Please enter the serial key received via email',
    'Format: HITP-XXXX-XXXX-XXXX-XXXX');
  SerialPage.Add('Serial Key:', False);
  SerialPage.Values[0] := 'HITP-';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = SerialPage.ID then
  begin
    G_LicenseKey := Trim(SerialPage.Values[0]);
    if Length(G_LicenseKey) < 10 then
    begin
      MsgBox('Please enter a valid serial key.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  PsCommand: String;
begin
  if CurStep = ssPostInstall then
  begin
    PsCommand := '-NoProfile -ExecutionPolicy Bypass -File "' +
                 ExpandConstant('{app}\scripts\BootstrapInstall.ps1') + '"' +
                 ' -LicenseKey "' + G_LicenseKey + '"' +
                 ' -BackofficeApi "{#BackofficeApi}"';
    Exec('powershell.exe', PsCommand, '', SW_SHOW, ewWaitUntilTerminated, ResultCode);
  end;
end;
