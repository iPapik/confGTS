; ConfGTS interactive EXE wizard; MSI continues to own product servicing and removal.
#define ConfVersion "0.18.18"
[Setup]
AppId={{CC8C0A11-1DC6-44D3-BA24-F2B79EDBF2F6}
AppName=ConfGTS
AppVersion={#ConfVersion}
AppPublisher=СГМУП «Городские тепловые сети»
AppPublisherURL=https://github.com/iPapik/confGTS
DefaultDirName={autopf}\ГТС\ConfGTS
DefaultGroupName=ConfGTS
DisableProgramGroupPage=yes
DisableWelcomePage=no
DisableDirPage=no
DisableReadyPage=no
Uninstallable=no
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\Assets\ConfGTS.ico
OutputDir=..\..\artifacts\setup
OutputBaseFilename=ConfGTS-Setup-{#ConfVersion}-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Files]
Source: "..\..\artifacts\msi\ConfGTS-{#ConfVersion}-x64.msi"; Flags: dontcopy

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; Flags: checkedonce
Name: "startmenuicon"; Description: "Создать ярлык в меню «Пуск»"; Flags: checkedonce
Name: "autostart"; Description: "Автоматически запускать ConfGTS при входе в Windows"; Flags: unchecked

[Run]
Filename: "{app}\ConfGTS.Client.exe"; Description: "Запустить ConfGTS"; Flags: nowait postinstall skipifsilent

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
  MsiPath, Args: String;
begin
  Result := '';
  try
    ExtractTemporaryFile('ConfGTS-{#ConfVersion}-x64.msi');
    MsiPath := ExpandConstant('{tmp}\ConfGTS-{#ConfVersion}-x64.msi');
    Args := '/i "' + MsiPath + '" INSTALLFOLDER="' + ExpandConstant('{app}') + '" /qn /norestart';
    if not Exec(ExpandConstant('{sys}\msiexec.exe'), Args, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
      Result := 'Не удалось запустить установщик MSI (код Windows: ' + IntToStr(ExitCode) + ').'
    else if (ExitCode <> 0) and (ExitCode <> 3010) then
      Result := 'Ошибка установки ConfGTS: MSI завершился с кодом ' + IntToStr(ExitCode) + '.';
    if ExitCode = 3010 then
      NeedsRestart := True;
  except
    Result := 'Не удалось распаковать или запустить MSI: ' + GetExceptionMessage;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  TargetExe, LinkPath, MenuDir: String;
begin
  if CurStep <> ssPostInstall then Exit;
  TargetExe := ExpandConstant('{app}\ConfGTS.Client.exe');
  MenuDir := ExpandConstant('{commonprograms}\ConfGTS');
  if not FileExists(TargetExe) then Exit;

  LinkPath := ExpandConstant('{commondesktop}\ConfGTS.lnk');
  if not WizardIsTaskSelected('desktopicon') then DeleteFile(LinkPath);

  LinkPath := MenuDir + '\ConfGTS.lnk';
  if not WizardIsTaskSelected('startmenuicon') then
  begin
    DeleteFile(LinkPath);
    RemoveDir(MenuDir);
  end;

  LinkPath := ExpandConstant('{commonstartup}\ConfGTS.lnk');
  if WizardIsTaskSelected('autostart') then
    CreateShellLink(LinkPath, 'ConfGTS — запуск при входе в Windows',
      TargetExe, '', ExpandConstant('{app}'), TargetExe, 0, SW_SHOWNORMAL)
  else
    DeleteFile(LinkPath);
end;
