#define MyAppName "TinyFix Hotkey"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Aryan Moradi"
#define MyAppURL "https://github.com/Aryan-mor/tinyfix-hotkey"
#define MyAppExeName "TinyFix.exe"

[Setup]
AppId={{8E6B2C1A-4F7D-4B9E-9A31-7C5E2D8F4A10}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
AppCopyright=Copyright (C) 2026 Aryan Moradi
DefaultDirName={localappdata}\Programs\TinyFix Hotkey
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
InfoBeforeFile=INFO.txt
OutputDir=..\dist
OutputBaseFilename=TinyFix-Setup
SetupIconFile=..\assets\tinyfix.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=force
CloseApplicationsFilter=TinyFix.exe
MinVersion=10.0
UsedUserAreasWarning=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "Start TinyFix when I sign in to Windows"; GroupDescription: "Startup options:"; Flags: checkedonce

[Files]
Source: "..\dist\TinyFix\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\TinyFix"; Filename: "{app}\{#MyAppExeName}"; Comment: "Correct selected English text with a shortcut"
Name: "{group}\Uninstall TinyFix"; Filename: "{uninstallexe}"
Name: "{userstartup}\TinyFix"; Filename: "{app}\{#MyAppExeName}"; Tasks: autostart; Comment: "TinyFix Hotkey"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch TinyFix"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\cache"
Type: files; Name: "{app}\tinyfix.log"
Type: files; Name: "{app}\tinyfix.pid"
Type: files; Name: "{app}\config.ini"

[Code]
const
  DefaultHotkey = 'Ctrl+Alt+Space';

var
  HotkeyPage: TWizardPage;
  ChkCtrl, ChkAlt, ChkShift, ChkWin: TNewCheckBox;
  KeyCombo: TNewComboBox;
  PreviewLabel: TNewStaticText;
  HintLabel: TNewStaticText;
  ChosenHotkey: String;
  PrefillDone: Boolean;

function GetCommandlineParam(const Name: String): String;
var
  I, PrefixLen: Integer;
  S, Prefix: String;
begin
  Result := '';
  Prefix := '/' + Name + '=';
  PrefixLen := Length(Prefix);
  for I := 1 to ParamCount do
  begin
    S := ParamStr(I);
    if CompareText(Copy(S, 1, PrefixLen), Prefix) = 0 then
    begin
      Result := Copy(S, PrefixLen + 1, MaxInt);
      Exit;
    end;
  end;
end;

function ReadExistingHotkey: String;
var
  Path: String;
begin
  Result := '';
  Path := ExpandConstant('{app}\config.ini');
  if FileExists(Path) then
    Result := Trim(GetIniString('TinyFix', 'hotkey', '', Path));
end;

function GetHotkeyFromCommandlineOrDefault: String;
var
  Cmd: String;
begin
  Cmd := Trim(GetCommandlineParam('HOTKEY'));
  if Cmd <> '' then
    Result := Cmd
  else
    Result := DefaultHotkey;
end;

procedure FillKeyCombo;
var
  I: Integer;
  Letters: String;
begin
  for I := 1 to 12 do
    KeyCombo.Items.Add('F' + IntToStr(I));
  KeyCombo.Items.Add('Space');
  Letters := 'ABCDEFGHIJKLMNOPQRSTUVWXYZ';
  for I := 1 to Length(Letters) do
    KeyCombo.Items.Add(Letters[I]);
  for I := 0 to 9 do
    KeyCombo.Items.Add(IntToStr(I));
  KeyCombo.Items.Add('Tab');
  KeyCombo.Items.Add('Enter');
  KeyCombo.Items.Add('Esc');
  KeyCombo.Items.Add('Backspace');
  KeyCombo.Items.Add('Insert');
  KeyCombo.Items.Add('Delete');
  KeyCombo.Items.Add('Home');
  KeyCombo.Items.Add('End');
  KeyCombo.Items.Add('PageUp');
  KeyCombo.Items.Add('PageDown');
  KeyCombo.Items.Add('Left');
  KeyCombo.Items.Add('Right');
  KeyCombo.Items.Add('Up');
  KeyCombo.Items.Add('Down');
end;

procedure SelectKeyInCombo(const Key: String);
var
  I: Integer;
begin
  for I := 0 to KeyCombo.Items.Count - 1 do
  begin
    if CompareText(KeyCombo.Items[I], Key) = 0 then
    begin
      KeyCombo.ItemIndex := I;
      Exit;
    end;
  end;
  if Key <> '' then
  begin
    KeyCombo.Items.Add(Key);
    KeyCombo.ItemIndex := KeyCombo.Items.Count - 1;
  end
  else
    KeyCombo.ItemIndex := KeyCombo.Items.IndexOf('Space');
end;

function IsFunctionKey(const Key: String): Boolean;
var
  N: Integer;
begin
  Result := False;
  if (Length(Key) < 2) or (Length(Key) > 3) then
    Exit;
  if CompareText(Copy(Key, 1, 1), 'F') <> 0 then
    Exit;
  N := StrToIntDef(Copy(Key, 2, 2), 0);
  Result := (N >= 1) and (N <= 24);
end;

function HotkeyFromControls: String;
var
  S: String;
begin
  S := '';
  if ChkCtrl.Checked then S := S + 'Ctrl+';
  if ChkAlt.Checked then S := S + 'Alt+';
  if ChkShift.Checked then S := S + 'Shift+';
  if ChkWin.Checked then S := S + 'Win+';
  if KeyCombo.ItemIndex >= 0 then
    S := S + KeyCombo.Text
  else
    S := S + 'Space';
  Result := S;
end;

procedure UpdatePreview(Sender: TObject);
var
  S, Key: String;
begin
  S := HotkeyFromControls;
  PreviewLabel.Caption := 'Shortcut: ' + S;
  if KeyCombo.ItemIndex >= 0 then
    Key := KeyCombo.Text
  else
    Key := '';
  if (not IsFunctionKey(Key)) and
     (not ChkCtrl.Checked) and (not ChkAlt.Checked) and
     (not ChkShift.Checked) and (not ChkWin.Checked) then
    HintLabel.Caption := 'Please add a modifier (Ctrl, Alt, Shift, or Win) for this key.'
  else
    HintLabel.Caption := 'Select text in any app, then press this shortcut to correct English typos.';
end;

procedure ApplyHotkeyToControls(const Spec: String);
var
  U, Part, Key: String;
  P: Integer;
begin
  ChkCtrl.Checked := False;
  ChkAlt.Checked := False;
  ChkShift.Checked := False;
  ChkWin.Checked := False;
  Key := '';
  U := Trim(Spec);
  StringChangeEx(U, '-', '+', True);
  while U <> '' do
  begin
    P := Pos('+', U);
    if P = 0 then
    begin
      Part := Trim(U);
      U := '';
    end
    else
    begin
      Part := Trim(Copy(U, 1, P - 1));
      Delete(U, 1, P);
    end;
    if Part <> '' then
    begin
      if (CompareText(Part, 'Ctrl') = 0) or (CompareText(Part, 'Control') = 0) then
        ChkCtrl.Checked := True
      else if CompareText(Part, 'Alt') = 0 then
        ChkAlt.Checked := True
      else if CompareText(Part, 'Shift') = 0 then
        ChkShift.Checked := True
      else if (CompareText(Part, 'Win') = 0) or (CompareText(Part, 'Windows') = 0) or (CompareText(Part, 'Meta') = 0) then
        ChkWin.Checked := True
      else
        Key := Part;
    end;
  end;
  if Key = '' then
    Key := 'Space';
  SelectKeyInCombo(Key);
  UpdatePreview(nil);
end;

procedure InitializeWizard;
var
  ModsLabel, KeyLabel: TNewStaticText;
begin
  ChosenHotkey := GetHotkeyFromCommandlineOrDefault;

  HotkeyPage := CreateCustomPage(wpSelectTasks, 'Choose your shortcut',
    'Pick the keyboard shortcut used to correct selected text.');

  ModsLabel := TNewStaticText.Create(HotkeyPage);
  ModsLabel.Parent := HotkeyPage.Surface;
  ModsLabel.Caption := 'Modifiers:';
  ModsLabel.Top := ScaleY(8);
  ModsLabel.Left := 0;

  ChkCtrl := TNewCheckBox.Create(HotkeyPage);
  ChkCtrl.Parent := HotkeyPage.Surface;
  ChkCtrl.Caption := 'Ctrl';
  ChkCtrl.Top := ModsLabel.Top + ModsLabel.Height + ScaleY(8);
  ChkCtrl.Left := 0;
  ChkCtrl.Width := ScaleX(70);
  ChkCtrl.OnClick := @UpdatePreview;

  ChkAlt := TNewCheckBox.Create(HotkeyPage);
  ChkAlt.Parent := HotkeyPage.Surface;
  ChkAlt.Caption := 'Alt';
  ChkAlt.Top := ChkCtrl.Top;
  ChkAlt.Left := ScaleX(80);
  ChkAlt.Width := ScaleX(70);
  ChkAlt.OnClick := @UpdatePreview;

  ChkShift := TNewCheckBox.Create(HotkeyPage);
  ChkShift.Parent := HotkeyPage.Surface;
  ChkShift.Caption := 'Shift';
  ChkShift.Top := ChkCtrl.Top;
  ChkShift.Left := ScaleX(160);
  ChkShift.Width := ScaleX(80);
  ChkShift.OnClick := @UpdatePreview;

  ChkWin := TNewCheckBox.Create(HotkeyPage);
  ChkWin.Parent := HotkeyPage.Surface;
  ChkWin.Caption := 'Win';
  ChkWin.Top := ChkCtrl.Top;
  ChkWin.Left := ScaleX(250);
  ChkWin.Width := ScaleX(70);
  ChkWin.OnClick := @UpdatePreview;

  KeyLabel := TNewStaticText.Create(HotkeyPage);
  KeyLabel.Parent := HotkeyPage.Surface;
  KeyLabel.Caption := 'Key:';
  KeyLabel.Top := ChkCtrl.Top + ChkCtrl.Height + ScaleY(20);
  KeyLabel.Left := 0;

  KeyCombo := TNewComboBox.Create(HotkeyPage);
  KeyCombo.Parent := HotkeyPage.Surface;
  KeyCombo.Style := csDropDownList;
  KeyCombo.Top := KeyLabel.Top - ScaleY(2);
  KeyCombo.Left := ScaleX(50);
  KeyCombo.Width := ScaleX(180);
  KeyCombo.OnChange := @UpdatePreview;
  FillKeyCombo;

  PreviewLabel := TNewStaticText.Create(HotkeyPage);
  PreviewLabel.Parent := HotkeyPage.Surface;
  PreviewLabel.Top := KeyCombo.Top + KeyCombo.Height + ScaleY(24);
  PreviewLabel.Left := 0;
  PreviewLabel.Caption := 'Shortcut: Ctrl+Alt+Space';
  PreviewLabel.Font.Style := [fsBold];
  PreviewLabel.Font.Size := 11;

  HintLabel := TNewStaticText.Create(HotkeyPage);
  HintLabel.Parent := HotkeyPage.Surface;
  HintLabel.Top := PreviewLabel.Top + PreviewLabel.Height + ScaleY(12);
  HintLabel.Left := 0;
  HintLabel.AutoSize := False;
  HintLabel.WordWrap := True;
  HintLabel.Width := HotkeyPage.SurfaceWidth;
  HintLabel.Height := ScaleY(48);
  HintLabel.Caption := 'Select text in any app, then press this shortcut to correct English typos.';

  ApplyHotkeyToControls(ChosenHotkey);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Key: String;
begin
  Result := True;
  if (HotkeyPage <> nil) and (CurPageID = HotkeyPage.ID) then
  begin
    if KeyCombo.ItemIndex >= 0 then
      Key := KeyCombo.Text
    else
      Key := '';
    if Key = '' then
    begin
      MsgBox('Please choose a key for your shortcut.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    if (not IsFunctionKey(Key)) and
       (not ChkCtrl.Checked) and (not ChkAlt.Checked) and
       (not ChkShift.Checked) and (not ChkWin.Checked) then
    begin
      MsgBox('Please add at least one modifier (Ctrl, Alt, Shift, or Win) unless you use a function key.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    ChosenHotkey := HotkeyFromControls;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
var
  Existing: String;
begin
  if (HotkeyPage <> nil) and (CurPageID = HotkeyPage.ID) and (not PrefillDone) then
  begin
    PrefillDone := True;
    if Trim(GetCommandlineParam('HOTKEY')) = '' then
    begin
      Existing := ReadExistingHotkey;
      if Existing <> '' then
        ApplyHotkeyToControls(Existing);
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Path, Cmd, Existing: String;
begin
  if CurStep = ssPostInstall then
  begin
    Cmd := Trim(GetCommandlineParam('HOTKEY'));
    if Cmd <> '' then
      ChosenHotkey := Cmd
    else if WizardSilent then
    begin
      Existing := ReadExistingHotkey;
      if Existing <> '' then
        ChosenHotkey := Existing
      else if ChosenHotkey = '' then
        ChosenHotkey := DefaultHotkey;
    end
    else if ChosenHotkey = '' then
      ChosenHotkey := DefaultHotkey;
    Path := ExpandConstant('{app}\config.ini');
    SetIniString('TinyFix', 'hotkey', ChosenHotkey, Path);
    Log('Wrote hotkey ' + ChosenHotkey + ' to ' + Path);
  end;
end;

function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM TinyFix.exe /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(400);
end;
