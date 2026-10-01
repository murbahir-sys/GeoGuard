; Установщик GeoGuard (Inno Setup 6). Собирается скриптом build\build.ps1.
; Параметры: /DSourceDir=<папка с самодостаточной сборкой>, /DOutputDir=<куда положить Setup.exe>,
;            /DAppVersion=<версия>, /DTESTMODE — пробная сборка без прав администратора, файрвола и планировщика.
; Файл сохранён в UTF-8 с BOM — без него русские строки читаются неправильно.

#ifndef AppVersion
  #define AppVersion "1.0.6"
#endif
#ifndef SourceDir
  #define SourceDir "..\dist\app"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist\installer"
#endif

#define AppName "GeoGuard"
#define AppPublisher "Савицкий Святослав"
#define AppExe "GeoGuard.exe"

[Setup]
#ifdef TESTMODE
AppId={{3F1D2C4A-0B7E-4C59-8A11-7E2F6B9D0C01}
AppName={#AppName} (пробная)
DefaultDirName={autopf}\{#AppName}-test
DefaultGroupName={#AppName} (пробная)
PrivilegesRequired=lowest
OutputBaseFilename=GeoGuard-Setup-TEST
#else
AppId={{B4E0C6F2-7A51-4E0B-9C0E-5D7A1E9B2F31}
AppName={#AppName}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
PrivilegesRequired=admin
OutputBaseFilename=GeoGuard-Setup-{#AppVersion}
#endif
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppCopyright=© 2026 {#AppPublisher}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExe}
OutputDir={#OutputDir}
SetupIconFile=..\GeoGuard\Assets\GeoGuard.ico
; Перед установкой показывается пояснение по-русски и текст лицензии MIT; файл в UTF-8 с BOM.
LicenseFile=LicensePage.txt
WizardStyle=modern
WizardImageFile=WizardLarge.bmp
WizardSmallImageFile=WizardSmall.bmp
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=no
RestartApplications=no
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany={#AppPublisher}
VersionInfoProductName={#AppName}
VersionInfoDescription=Установщик {#AppName}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
#ifndef TESTMODE
Name: "autostart"; Description: "Запускать GeoGuard вместе с Windows"; GroupDescription: "Дополнительно:"
#endif
Name: "desktopicon"; Description: "Создать значок на рабочем столе"; GroupDescription: "Дополнительно:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Защита по стране и VPN"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

#ifndef TESTMODE
[Run]
; Установщик уже работает с правами администратора, поэтому автозапуск создаётся как задача планировщика с наивысшими правами.
Filename: "{app}\{#AppExe}"; Parameters: "--enable-autostart --silent"; Flags: runhidden waituntilterminated; Tasks: autostart; StatusMsg: "Настройка автозапуска..."
Filename: "{app}\{#AppExe}"; Parameters: "--disable-autostart --silent"; Flags: runhidden waituntilterminated; Tasks: not autostart
Filename: "{app}\{#AppExe}"; Description: "Запустить GeoGuard"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Сначала закрыть программу, затем убрать правила файрвола и автозапуск, которые она создавала.
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#AppExe}"; Flags: runhidden; RunOnceId: "KillGeoGuard"
Filename: "{app}\{#AppExe}"; Parameters: "--remove-rules --silent"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveGeoGuardRules"
#else
[Run]
Filename: "{app}\{#AppExe}"; Description: "Запустить GeoGuard"; Flags: nowait postinstall skipifsilent unchecked
#endif

[Code]
procedure StopRunningApp;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

// Обновление поверх старой версии: работающую копию закрываем. Правила файрвола при этом остаются и продолжают защищать.
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopRunningApp;
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and (not UninstallSilent) then
  begin
    if MsgBox('Удалить также настройки GeoGuard (список приложений и параметры)?',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{userappdata}\GeoGuard'), True, True, True);
  end;
end;
