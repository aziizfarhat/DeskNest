#ifndef AppVersion
  #define AppVersion "0.7.2.1"
#endif

[Setup]
AppId={{A7C4E1F2-5B3D-4A8E-9C61-2D7F8B4A1E90}
AppName=DeskNest
AppVersion={#AppVersion}
AppPublisher=DeskNest
DefaultDirName={autopf}\DeskNest
DefaultGroupName=DeskNest
PrivilegesRequired=lowest
OutputDir=installer
OutputBaseFilename=DeskNest-Setup
SetupIconFile=app.ico
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName=DeskNest
UninstallDisplayIcon={app}\DeskNest.exe
AppMutex=NestsLite.SingleInstance,DeskNest.SingleInstance
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "startup"; Description: "Lancer DeskNest au demarrage de Windows"; GroupDescription: "Options :"

[InstallDelete]
; Remove the old executable after the previous version has been closed.
Type: files; Name: "{app}\NestsLite.exe"

[Files]
Source: "publish\DeskNest.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\DeskNest"; Filename: "{app}\DeskNest.exe"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "DeskNest"; ValueData: """{app}\DeskNest.exe"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\DeskNest.exe"; Description: "Lancer DeskNest"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Remet sur le bureau les éléments rangés dans %USERPROFILE%\DeskNest\Items
; avant que l'exécutable ne soit supprimé.
Filename: "{app}\DeskNest.exe"; Parameters: "--restore-all"; RunOnceId: "RestoreAllItems"; Flags: runhidden waituntilterminated skipifdoesntexist

[Code]
var
  DeleteNestData: Boolean;

function InitializeUninstall(): Boolean;
var
  Form: TSetupForm;
  Info: TNewStaticText;
  CheckBox: TNewCheckBox;
  OKButton, CancelButton: TNewButton;
  W: Integer;
begin
  Result := True;
  DeleteNestData := False;

  { Desinstallation silencieuse : aucune question et aucune suppression de donnees. }
  if UninstallSilent() then Exit;

  Form := CreateCustomForm(440, 190, False, True);
  try
    Form.Caption := 'DeskNest';

    Info := TNewStaticText.Create(Form);
    Info.Parent := Form;
    Info.Left := ScaleX(16);
    Info.Top := ScaleY(14);
    Info.Width := Form.ClientWidth - ScaleX(32);
    Info.AutoSize := False;
    Info.WordWrap := True;
    Info.Caption := 'DeskNest va etre desinstalle.' + #13#10 + #13#10 +
      'Les zones, les reglages et les sauvegardes sont conserves par defaut.';
    Info.AdjustHeight;

    CheckBox := TNewCheckBox.Create(Form);
    CheckBox.Parent := Form;
    CheckBox.Left := ScaleX(16);
    CheckBox.Top := Info.Top + Info.Height + ScaleY(16);
    CheckBox.Width := Form.ClientWidth - ScaleX(32);
    CheckBox.Caption := 'Supprimer aussi les zones et leurs reglages (donnees DeskNest)';
    CheckBox.Checked := False;

    OKButton := TNewButton.Create(Form);
    OKButton.Parent := Form;
    OKButton.Caption := 'Desinstaller';
    OKButton.ModalResult := mrOk;
    OKButton.Default := True;
    OKButton.Height := ScaleY(25);
    OKButton.Top := Form.ClientHeight - ScaleY(12) - OKButton.Height;

    CancelButton := TNewButton.Create(Form);
    CancelButton.Parent := Form;
    CancelButton.Caption := 'Annuler';
    CancelButton.ModalResult := mrCancel;
    CancelButton.Cancel := True;
    CancelButton.Height := OKButton.Height;
    CancelButton.Top := OKButton.Top;

    W := Form.CalculateButtonWidth([OKButton.Caption, CancelButton.Caption]);
    OKButton.Width := W;
    CancelButton.Width := W;
    OKButton.Left := Form.ClientWidth - ScaleX(12) - W;
    CancelButton.Left := OKButton.Left - ScaleX(8) - W;

    Form.ActiveControl := OKButton;
    if Form.ShowModal() = mrOk then
      DeleteNestData := CheckBox.Checked
    else
      Result := False;
  finally
    Form.Free;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  { Uniquement si l'utilisateur a coche la case : les donnees sont conservees sinon. }
  if (CurUninstallStep = usPostUninstall) and DeleteNestData then
  begin
    DelTree(ExpandConstant('{userappdata}\DeskNest'), True, True, True);
    DelTree(ExpandConstant('{userappdata}\NestsLite'), True, True, True);
  end;
end;
