; MedControl Inno Setup installer (§46)
#define MyAppName "MedControl"
#define MyAppVersion "1.2.0"
#define MyAppExe "MedControl.exe"

[Setup]
AppId={{8E2B4A10-6C3F-4A9B-9E1D-MEDCONTROL1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\MedControl
DefaultGroupName=MedControl
OutputDir=..\installer\output
OutputBaseFilename=MedControl_Setup_1.2.0
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
WizardStyle=modern
SetupIconFile=..\src\MedControl\Resources\app.ico
UninstallDisplayIcon={app}\{#MyAppExe}

[Files]
Source: "..\src\MedControl\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\MedControl"; Filename: "{app}\{#MyAppExe}"
Name: "{autodesktop}\MedControl"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Tasks]
Name: desktopicon; Description: "Создать значок на рабочем столе"

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "Запустить MedControl"; Flags: nowait postinstall

[Code]
function InitializeUninstall(): Boolean;
var
  DataDir: String;
begin
  Result := True;
  DataDir := ExpandConstant('{localappdata}\MedControl');
  if DirExists(DataDir) then
  begin
    if MsgBox('Удалить все данные MedControl?' + #13#10 +
      '(база измерений и приёмов, фото, инструкции, отчёты, резервные копии)' + #13#10#13#10 +
      '«Да» — удалить всё. «Нет» — оставить данные на компьютере.',
      mbConfirmation, MB_YESNO) = IDYES then
    begin
      DelTree(DataDir, True, True, True);
    end;
  end;
end;

