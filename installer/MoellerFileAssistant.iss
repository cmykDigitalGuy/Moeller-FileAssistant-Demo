#define AppVersion "0.2.1"
#define AppExe "Moeller.FileAssistant.exe"

[Setup]
AppId={{86B6FA31-7B83-44B7-9D45-5FB899EA7D31}
AppName=Moeller File Assistant Demo
AppVersion={#AppVersion}
AppPublisher=Moeller Printing
AppComments=Offline fictional-job demonstration. No production systems are contacted.
DefaultDirName={localappdata}\Programs\Moeller\FileAssistantDemo
DefaultGroupName=Moeller File Assistant Demo
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\artifacts
OutputBaseFilename=Moeller-File-Assistant-Demo-{#AppVersion}-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExe}
SetupIconFile=..\src\Moeller.FileAssistant\App.ico
CloseApplications=yes
CloseApplicationsFilter=*.exe,*.dll
RestartApplications=no
AppMutex=Local\Moeller.FileAssistant.Demo.v020
ChangesAssociations=yes
UsePreviousTasks=yes
SetupLogging=yes
VersionInfoVersion={#AppVersion}.0
InfoBeforeFile=..\docs\SETUP-NOTES.txt

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: checkedonce
Name: "startup"; Description: "Start quietly in the system tray when I sign in"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Moeller File Assistant Demo"; Filename: "{app}\{#AppExe}"
Name: "{group}\Demo workspace folders"; Filename: "{localappdata}\Moeller\FileAssistantDemo\Workspace"
Name: "{group}\Uninstall Moeller File Assistant Demo"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Moeller File Assistant Demo"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon
Name: "{userappdata}\Microsoft\Windows\SendTo\Moeller File Assistant"; Filename: "{app}\{#AppExe}"; Parameters: "--select"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "MoellerFileAssistantDemo"; ValueData: """{app}\{#AppExe}"" --tray"; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "Open Moeller File Assistant Demo"; Flags: nowait postinstall skipifsilent

[Code]
var
  InitializationPassed: Boolean;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code, Attempt: Integer;
  ExistingExe: String;
begin
  Result := '';
  ExistingExe := ExpandConstant('{app}\{#AppExe}');
  if FileExists(ExistingExe) then begin
    if not Exec(ExistingExe, '--shutdown', '', SW_HIDE, ewWaitUntilTerminated, Code) then
      Log('Existing assistant shutdown request could not start. Mutex check will protect active data.');
    for Attempt := 1 to 40 do begin
      if not CheckForMutexes('Local\Moeller.FileAssistant.Demo.v020') then Break;
      Sleep(250);
    end;
    if CheckForMutexes('Local\Moeller.FileAssistant.Demo.v020') then
      Result := 'A File Assistant operation is still running. Finish the operation, choose Exit from its tray menu, then retry installation. No database files have been overwritten.';
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
begin
  if CurStep = ssPostInstall then begin
    WizardForm.StatusLabel.Caption := 'Preparing local fictional jobs, database and simulated output folders...';
    InitializationPassed := Exec(ExpandConstant('{app}\{#AppExe}'), '--initialize', '', SW_HIDE, ewWaitUntilTerminated, Code);
    if (not InitializationPassed) or (Code <> 0) then begin
      InitializationPassed := False;
      RaiseException('The application was copied but demo initialization did not complete. Windows application-control policy may have blocked this unsigned build, or initialization reported an error. Existing data was preserved. See %LOCALAPPDATA%\Moeller\FileAssistantDemo\startup-errors.log. The installer did not change security settings.');
    end;
  end;
end;

function InitializeUninstall(): Boolean;
var
  Code, Attempt: Integer;
begin
  Result := True;
  Exec(ExpandConstant('{app}\{#AppExe}'), '--shutdown', '', SW_HIDE, ewWaitUntilTerminated, Code);
  for Attempt := 1 to 40 do begin
    if not CheckForMutexes('Local\Moeller.FileAssistant.Demo.v020') then Break;
    Sleep(250);
  end;
  if CheckForMutexes('Local\Moeller.FileAssistant.Demo.v020') then begin
    if not UninstallSilent then MsgBox('Finish the current file operation and Exit the assistant from its tray before uninstalling.', mbError, MB_OK);
    Result := False;
  end;
  if Result then Exec(ExpandConstant('{app}\{#AppExe}'), '--unregister', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then begin
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'MoellerFileAssistantDemo');
    if not UninstallSilent then
      MsgBox('The application, shortcuts and right-click registration were removed. Your fictional demo workspace and exported backups were preserved under %LOCALAPPDATA%\Moeller\FileAssistantDemo. Reinstalling the same version can reuse that state.', mbInformation, MB_OK);
  end;
end;
