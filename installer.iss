#ifndef AppVersion
  #define AppVersion "0.8.2.0"
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
OutputBaseFilename=DeskNest-Setup-{#AppVersion}
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
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "portuguese"; MessagesFile: "compiler:Languages\Portuguese.isl"
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"
Name: "chinesesimplified"; MessagesFile: "installer-lang\ChineseSimplified.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "ukrainian"; MessagesFile: "compiler:Languages\Ukrainian.isl"

[CustomMessages]
english.StartupTask=Start DeskNest with Windows
french.StartupTask=Lancer DeskNest au démarrage de Windows
italian.StartupTask=Avvia DeskNest con Windows
german.StartupTask=DeskNest mit Windows starten
spanish.StartupTask=Iniciar DeskNest con Windows
portuguese.StartupTask=Iniciar o DeskNest com o Windows
arabic.StartupTask=تشغيل DeskNest مع Windows
chinesesimplified.StartupTask=随 Windows 启动 DeskNest
japanese.StartupTask=Windows と同時に DeskNest を起動
korean.StartupTask=Windows와 함께 DeskNest 시작
russian.StartupTask=Запускать DeskNest при запуске Windows
ukrainian.StartupTask=Запускати DeskNest разом із Windows

english.StartupGroup=Options:
french.StartupGroup=Options :
italian.StartupGroup=Opzioni:
german.StartupGroup=Optionen:
spanish.StartupGroup=Opciones:
portuguese.StartupGroup=Opções:
arabic.StartupGroup=الخيارات:
chinesesimplified.StartupGroup=选项：
japanese.StartupGroup=オプション:
korean.StartupGroup=옵션:
russian.StartupGroup=Параметры:
ukrainian.StartupGroup=Параметри:

english.LaunchApp=Launch DeskNest
french.LaunchApp=Lancer DeskNest
italian.LaunchApp=Avvia DeskNest
german.LaunchApp=DeskNest starten
spanish.LaunchApp=Iniciar DeskNest
portuguese.LaunchApp=Iniciar o DeskNest
arabic.LaunchApp=تشغيل DeskNest
chinesesimplified.LaunchApp=启动 DeskNest
japanese.LaunchApp=DeskNest を起動
korean.LaunchApp=DeskNest 시작
russian.LaunchApp=Запустить DeskNest
ukrainian.LaunchApp=Запустити DeskNest

english.UninstallIntro=DeskNest is about to be uninstalled.%n%nZones, settings and backups are kept by default.
french.UninstallIntro=DeskNest va être désinstallé.%n%nLes zones, les réglages et les sauvegardes sont conservés par défaut.
italian.UninstallIntro=DeskNest sta per essere disinstallato.%n%nLe zone, le impostazioni e i backup vengono conservati per impostazione predefinita.
german.UninstallIntro=DeskNest wird deinstalliert.%n%nBereiche, Einstellungen und Sicherungen bleiben standardmäßig erhalten.
spanish.UninstallIntro=DeskNest se va a desinstalar.%n%nLas zonas, los ajustes y las copias de seguridad se conservan de forma predeterminada.
portuguese.UninstallIntro=O DeskNest vai ser desinstalado.%n%nAs zonas, as configurações e os backups são mantidos por predefinição.
arabic.UninstallIntro=سيتم إلغاء تثبيت DeskNest.%n%nيتم الاحتفاظ بالمناطق والإعدادات والنسخ الاحتياطية افتراضيًا.
chinesesimplified.UninstallIntro=即将卸载 DeskNest。%n%n默认会保留区域、设置和备份。
japanese.UninstallIntro=DeskNest をアンインストールします。%n%nゾーン、設定、バックアップは既定で保持されます。
korean.UninstallIntro=DeskNest를 제거합니다.%n%n영역, 설정 및 백업은 기본적으로 유지됩니다.
russian.UninstallIntro=DeskNest будет удалён.%n%nЗоны, настройки и резервные копии по умолчанию сохраняются.
ukrainian.UninstallIntro=DeskNest буде видалено.%n%nЗони, налаштування та резервні копії за замовчуванням зберігаються.

english.UninstallDeleteData=Also delete zones and their settings (DeskNest data)
french.UninstallDeleteData=Supprimer aussi les zones et leurs réglages (données DeskNest)
italian.UninstallDeleteData=Elimina anche le zone e le loro impostazioni (dati DeskNest)
german.UninstallDeleteData=Auch Bereiche und ihre Einstellungen löschen (DeskNest-Daten)
spanish.UninstallDeleteData=Eliminar también las zonas y sus ajustes (datos de DeskNest)
portuguese.UninstallDeleteData=Eliminar também as zonas e as suas configurações (dados do DeskNest)
arabic.UninstallDeleteData=حذف المناطق وإعداداتها أيضًا (بيانات DeskNest)
chinesesimplified.UninstallDeleteData=同时删除区域及其设置（DeskNest 数据）
japanese.UninstallDeleteData=ゾーンとその設定も削除する（DeskNest のデータ）
korean.UninstallDeleteData=영역과 해당 설정도 삭제(DeskNest 데이터)
russian.UninstallDeleteData=Также удалить зоны и их настройки (данные DeskNest)
ukrainian.UninstallDeleteData=Також видалити зони та їхні налаштування (дані DeskNest)

english.UninstallButton=Uninstall
french.UninstallButton=Désinstaller
italian.UninstallButton=Disinstalla
german.UninstallButton=Deinstallieren
spanish.UninstallButton=Desinstalar
portuguese.UninstallButton=Desinstalar
arabic.UninstallButton=إلغاء التثبيت
chinesesimplified.UninstallButton=卸载
japanese.UninstallButton=アンインストール
korean.UninstallButton=제거
russian.UninstallButton=Удалить
ukrainian.UninstallButton=Видалити

english.UninstallCancel=Cancel
french.UninstallCancel=Annuler
italian.UninstallCancel=Annulla
german.UninstallCancel=Abbrechen
spanish.UninstallCancel=Cancelar
portuguese.UninstallCancel=Cancelar
arabic.UninstallCancel=إلغاء
chinesesimplified.UninstallCancel=取消
japanese.UninstallCancel=キャンセル
korean.UninstallCancel=취소
russian.UninstallCancel=Отмена
ukrainian.UninstallCancel=Скасувати

[Tasks]
Name: "startup"; Description: "{cm:StartupTask}"; GroupDescription: "{cm:StartupGroup}"

[InstallDelete]
; Remove the old executable after the previous version has been closed.
Type: files; Name: "{app}\NestsLite.exe"

[Files]
Source: "publish\DeskNest.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\DeskNest"; Filename: "{app}\DeskNest.exe"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "DeskNest"; ValueData: """{app}\DeskNest.exe"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\DeskNest.exe"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

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
    Info.Caption := ExpandConstant('{cm:UninstallIntro}');
    Info.AdjustHeight;

    CheckBox := TNewCheckBox.Create(Form);
    CheckBox.Parent := Form;
    CheckBox.Left := ScaleX(16);
    CheckBox.Top := Info.Top + Info.Height + ScaleY(16);
    CheckBox.Width := Form.ClientWidth - ScaleX(32);
    CheckBox.Caption := ExpandConstant('{cm:UninstallDeleteData}');
    CheckBox.Checked := False;

    OKButton := TNewButton.Create(Form);
    OKButton.Parent := Form;
    OKButton.Caption := ExpandConstant('{cm:UninstallButton}');
    OKButton.ModalResult := mrOk;
    OKButton.Default := True;
    OKButton.Height := ScaleY(25);
    OKButton.Top := Form.ClientHeight - ScaleY(12) - OKButton.Height;

    CancelButton := TNewButton.Create(Form);
    CancelButton.Parent := Form;
    CancelButton.Caption := ExpandConstant('{cm:UninstallCancel}');
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
