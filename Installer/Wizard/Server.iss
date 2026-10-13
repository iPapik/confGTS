; ConfGTS Server interactive EXE wizard, using the service-enabled MSI.
#define ConfVersion "0.18.20"
[Setup]
AppId={{3BD7579A-8F73-41DD-97A6-6498CFB23444}
AppName=ConfGTS Server
AppVersion={#ConfVersion}
AppPublisher=СГМУП «Городские тепловые сети»
AppPublisherURL=https://github.com/iPapik/confGTS
DefaultDirName={autopf}\ГТС\ConfGTS Server
DefaultGroupName=ConfGTS Server
DisableProgramGroupPage=yes
DisableWelcomePage=no
DisableDirPage=no
DisableReadyPage=no
Uninstallable=no
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\Assets\ConfGTS.ico
OutputDir=..\..\artifacts\server-setup
OutputBaseFilename=ConfGTS-Server-Setup-{#ConfVersion}-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Files]
Source: "..\..\artifacts\server-msi\ConfGTS-Server-{#ConfVersion}-x64.msi"; Flags: dontcopy
Source: "Configure-ServerStorage.ps1"; Flags: dontcopy

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык настроек сервера на рабочем столе"; Flags: checkedonce
Name: "startmenuicon"; Description: "Создать ярлыки в меню «Пуск»"; Flags: checkedonce
Name: "autostart"; Description: "Автоматически запускать серверную службу при загрузке Windows"; Flags: checkedonce

[Run]
Filename: "{app}\ConfGTS.Server.Settings.exe"; Description: "Открыть настройки ConfGTS Server"; Flags: nowait postinstall skipifsilent

[Code]
var
  RecordingsPage: TInputDirWizardPage;

procedure InitializeWizard;
var
  I, Parameter, Prefix: Integer;
  Value: String;
begin
  RecordingsPage := CreateInputDirPage(wpSelectDir,
    'Каталог записей конференций',
    'Выберите место для хранения видеозаписей ConfGTS.',
    'Рекомендуется отдельный диск с достаточным свободным местом. Уже существующие записи сохраняются при обновлении.',
    False, '');
  RecordingsPage.Add('Каталог для сохранения записей:');
  RecordingsPage.Values[0] := ExpandConstant('{commonappdata}\ConfGTS\Recordings');
  for I := 1 to ParamCount do
  begin
    Parameter := 0;
    Value := ParamStr(I);
    Prefix := Pos('/RECORDINGDIR=', Uppercase(Value));
    if Prefix = 1 then
      RecordingsPage.Values[0] := Copy(Value, 15, Length(Value));
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = RecordingsPage.ID then
  begin
    if Trim(RecordingsPage.Values[0]) = '' then
    begin
      MsgBox('Укажите папку для сохранения записей.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode, ApplyCode: Integer;
  MsiPath, PsPath, Args, AutoStart: String;
begin
  Result := '';
  try
    ExtractTemporaryFile('ConfGTS-Server-{#ConfVersion}-x64.msi');
    ExtractTemporaryFile('Configure-ServerStorage.ps1');
    MsiPath := ExpandConstant('{tmp}\ConfGTS-Server-{#ConfVersion}-x64.msi');
    Args := '/i "' + MsiPath + '" INSTALLFOLDER="' + ExpandConstant('{app}') + '" /qn /norestart';
    if not Exec(ExpandConstant('{sys}\msiexec.exe'), Args, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    begin
      Result := 'Не удалось запустить MSI серверной части.';
      Exit;
    end;
    if (ExitCode <> 0) and (ExitCode <> 3010) then
    begin
      Result := 'Ошибка установки сервера: код MSI ' + IntToStr(ExitCode) + '.';
      Exit;
    end;
    if ExitCode = 3010 then NeedsRestart := True;

    if WizardIsTaskSelected('autostart') then AutoStart := '1' else AutoStart := '0';
    PsPath := ExpandConstant('{tmp}\Configure-ServerStorage.ps1');
    Args := '-NoProfile -ExecutionPolicy Bypass -File "' + PsPath +
            '" -RecordingDirectory "' + RecordingsPage.Values[0] +
            '" -AutoStartService ' + AutoStart;
    if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
      Args, '', SW_HIDE, ewWaitUntilTerminated, ApplyCode) then
      Result := 'Не удалось запустить настройку каталога записей.'
    else if ApplyCode <> 0 then
      Result := 'Сервер установлен, но настройка каталога записей не завершилась (код ' +
        IntToStr(ApplyCode) + '). Проверьте журнал установки.';
  except
    Result := 'Ошибка настройки сервера: ' + GetExceptionMessage;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  DesktopLink, MenuDir: String;
begin
  if CurStep <> ssPostInstall then Exit;
  DesktopLink := ExpandConstant('{commondesktop}\ConfGTS Server Settings.lnk');
  MenuDir := ExpandConstant('{commonprograms}\ConfGTS Server');
  if not WizardIsTaskSelected('desktopicon') then DeleteFile(DesktopLink);
  if not WizardIsTaskSelected('startmenuicon') then
  begin
    DeleteFile(MenuDir + '\ConfGTS Server Settings.lnk');
    DeleteFile(MenuDir + '\ConfGTS Server Web.lnk');
    RemoveDir(MenuDir);
  end;
end;
