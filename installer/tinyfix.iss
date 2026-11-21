#define MyAppName "TinyFix Hotkey"
#define MyAppVersion "1.3.1"
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
WizardSizePercent=120
WizardImageFile=..\assets\wizard.bmp
WizardSmallImageFile=..\assets\wizard-small.bmp
SetupLogging=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Do not close every TinyFix.exe on the machine. PrepareToInstall and
; InitializeUninstall call StopTinyFixInApp, which only stops {app}\TinyFix.exe.
CloseApplications=no
MinVersion=10.0
UsedUserAreasWarning=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "Start TinyFix when I sign in to Windows"; GroupDescription: "Startup options:"; Flags: checkedonce

[Files]
Source: "..\dist\TinyFix\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\scripts\stop-tinyfix-in-dir.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\scripts\stop-tinyfix-in-dir.ps1"; DestDir: "{tmp}"; Flags: dontcopy

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
Type: files; Name: "{app}\secrets.dpapi"
Type: files; Name: "{app}\prompt_fix.txt"
Type: files; Name: "{app}\prompt_translate.txt"
Type: files; Name: "{app}\skills.json"

[Code]
const
  DefaultHotkey = 'Ctrl+Alt+Space';
  DefaultHotkeyAll = 'Ctrl+F3';
  DefaultHotkeyTranslate = 'Ctrl+Alt+T';
  DisabledHotkey = 'none';
  DisabledKeyLabel = 'None/Disabled';

var
  HotkeyPage: TWizardPage;
  SelCtrl, SelAlt, SelShift, SelWin: TNewCheckBox;
  AllCtrl, AllAlt, AllShift, AllWin: TNewCheckBox;
  SelKey, AllKey: TNewComboBox;
  SelPreview, AllPreview: TNewStaticText;
  HintLabel: TNewStaticText;
  ChosenHotkey: String;
  ChosenHotkeyAll: String;
  PrefillDone: Boolean;
  AppWasRunning: Boolean;

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

function HasCommandlineFlag(const Name: String): Boolean;
var
  I: Integer;
  S, Prefix, Value: String;
begin
  Result := False;
  Prefix := '/' + Name + '=';
  for I := 1 to ParamCount do
  begin
    S := ParamStr(I);
    if (CompareText(S, '/' + Name) = 0) or (CompareText(S, '-' + Name) = 0) then
    begin
      Result := True;
      Exit;
    end;
    if CompareText(Copy(S, 1, Length(Prefix)), Prefix) = 0 then
    begin
      Value := LowerCase(Copy(S, Length(Prefix) + 1, MaxInt));
      Result := (Value <> '0') and (Value <> 'no') and (Value <> 'false');
      Exit;
    end;
  end;
end;

function ReadExistingIni(const Key, Fallback: String): String;
var
  Path, Value: String;
begin
  Result := Fallback;
  Path := ExpandConstant('{app}\config.ini');
  if FileExists(Path) then
  begin
    Value := Trim(GetIniString('TinyFix', Key, '', Path));
    if Value <> '' then
      Result := Value;
  end;
end;

function IsDisabledSpec(const Spec: String): Boolean;
var
  U: String;
begin
  U := LowerCase(Trim(Spec));
  StringChangeEx(U, ' ', '', True);
  StringChangeEx(U, '-', '/', True);
  Result := (U = 'none') or (U = 'disabled') or (U = 'off') or (U = 'no') or (U = 'none/disabled');
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

procedure FillKeyCombo(Combo: TNewComboBox);
var
  I: Integer;
  Letters: String;
begin
  Combo.Items.Add(DisabledKeyLabel);
  for I := 1 to 12 do
    Combo.Items.Add('F' + IntToStr(I));
  Combo.Items.Add('Space');
  Letters := 'ABCDEFGHIJKLMNOPQRSTUVWXYZ';
  for I := 1 to Length(Letters) do
    Combo.Items.Add(Letters[I]);
  for I := 0 to 9 do
    Combo.Items.Add(IntToStr(I));
  Combo.Items.Add('Tab');
  Combo.Items.Add('Enter');
  Combo.Items.Add('Esc');
  Combo.Items.Add('Backspace');
  Combo.Items.Add('Insert');
  Combo.Items.Add('Delete');
  Combo.Items.Add('Home');
  Combo.Items.Add('End');
  Combo.Items.Add('PageUp');
  Combo.Items.Add('PageDown');
  Combo.Items.Add('Left');
  Combo.Items.Add('Right');
  Combo.Items.Add('Up');
  Combo.Items.Add('Down');
end;

procedure SelectKeyInCombo(Combo: TNewComboBox; const Key: String);
var
  I: Integer;
  Target: String;
begin
  if IsDisabledSpec(Key) then
    Target := DisabledKeyLabel
  else
    Target := Key;
  for I := 0 to Combo.Items.Count - 1 do
  begin
    if CompareText(Combo.Items[I], Target) = 0 then
    begin
      Combo.ItemIndex := I;
      Exit;
    end;
  end;
  if Target <> '' then
  begin
    Combo.Items.Add(Target);
    Combo.ItemIndex := Combo.Items.Count - 1;
  end
  else
    Combo.ItemIndex := Combo.Items.IndexOf(DisabledKeyLabel);
end;

function KeyNameFromCombo(Combo: TNewComboBox): String;
begin
  if Combo.ItemIndex >= 0 then
    Result := Combo.Text
  else
    Result := '';
end;

function HotkeyFromControls(ChkCtrl, ChkAlt, ChkShift, ChkWin: TNewCheckBox; Combo: TNewComboBox): String;
var
  S, Key: String;
begin
  Key := KeyNameFromCombo(Combo);
  if IsDisabledSpec(Key) or (Key = '') then
  begin
    Result := DisabledHotkey;
    Exit;
  end;
  S := '';
  if ChkCtrl.Checked then S := S + 'Ctrl+';
  if ChkAlt.Checked then S := S + 'Alt+';
  if ChkShift.Checked then S := S + 'Shift+';
  if ChkWin.Checked then S := S + 'Win+';
  Result := S + Key;
end;

procedure UpdatePreviews(Sender: TObject);
var
  Sel, AllKeyText, SelKeyText: String;
begin
  Sel := HotkeyFromControls(SelCtrl, SelAlt, SelShift, SelWin, SelKey);
  if IsDisabledSpec(Sel) then
    SelPreview.Caption := 'Fix selection: Disabled'
  else
    SelPreview.Caption := 'Fix selection: ' + Sel;

  Sel := HotkeyFromControls(AllCtrl, AllAlt, AllShift, AllWin, AllKey);
  if IsDisabledSpec(Sel) then
    AllPreview.Caption := 'Fix all: Disabled'
  else
    AllPreview.Caption := 'Fix all: ' + Sel;

  SelKeyText := KeyNameFromCombo(SelKey);
  AllKeyText := KeyNameFromCombo(AllKey);
  if (not IsDisabledSpec(SelKeyText)) and (SelKeyText <> '') and (not IsFunctionKey(SelKeyText)) and
     (not SelCtrl.Checked) and (not SelAlt.Checked) and (not SelShift.Checked) and (not SelWin.Checked) then
    HintLabel.Caption := 'Please add a modifier (Ctrl, Alt, Shift, or Win) for the Fix selection key.'
  else if (not IsDisabledSpec(AllKeyText)) and (AllKeyText <> '') and (not IsFunctionKey(AllKeyText)) and
     (not AllCtrl.Checked) and (not AllAlt.Checked) and (not AllShift.Checked) and (not AllWin.Checked) then
    HintLabel.Caption := 'Please add a modifier (Ctrl, Alt, Shift, or Win) for the Fix all key.'
  else
    HintLabel.Caption := 'Fix selection corrects the highlighted text. Fix all selects the whole field first. Either shortcut can be disabled. After setup, open Settings in the tray for sensitivity, a Translate shortcut (Ctrl+Alt+T), and optional online AI.';
end;

procedure ApplyHotkeyToControls(const Spec: String; ChkCtrl, ChkAlt, ChkShift, ChkWin: TNewCheckBox; Combo: TNewComboBox);
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
  if IsDisabledSpec(U) then
  begin
    SelectKeyInCombo(Combo, DisabledKeyLabel);
    UpdatePreviews(nil);
    Exit;
  end;
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
  SelectKeyInCombo(Combo, Key);
  UpdatePreviews(nil);
end;

function CreateModifier(Page: TWizardPage; const Caption: String; Top, Left: Integer): TNewCheckBox;
begin
  Result := TNewCheckBox.Create(Page);
  Result.Parent := Page.Surface;
  Result.Caption := Caption;
  Result.Top := Top;
  Result.Left := Left;
  Result.Width := ScaleX(70);
  Result.OnClick := @UpdatePreviews;
end;

procedure InitializeWizard;
var
  TitleSel, TitleAll, KeyLabel1, KeyLabel2: TNewStaticText;
  RowSel, RowAll, KeyTop1, KeyTop2: Integer;
begin
  AppWasRunning := False;
  ChosenHotkey := DefaultHotkey;
  ChosenHotkeyAll := DefaultHotkeyAll;
  if Trim(GetCommandlineParam('HOTKEY')) <> '' then
    ChosenHotkey := Trim(GetCommandlineParam('HOTKEY'));
  if Trim(GetCommandlineParam('HOTKEYALL')) <> '' then
    ChosenHotkeyAll := Trim(GetCommandlineParam('HOTKEYALL'));

  HotkeyPage := CreateCustomPage(wpSelectTasks, 'Choose your shortcuts',
    'Pick keyboard shortcuts for fixing a selection or the whole field. Either one can be disabled.');

  TitleSel := TNewStaticText.Create(HotkeyPage);
  TitleSel.Parent := HotkeyPage.Surface;
  TitleSel.Caption := 'Fix selection';
  TitleSel.Font.Style := [fsBold];
  TitleSel.Top := ScaleY(4);
  TitleSel.Left := 0;

  RowSel := TitleSel.Top + TitleSel.Height + ScaleY(6);
  SelCtrl := CreateModifier(HotkeyPage, 'Ctrl', RowSel, 0);
  SelAlt := CreateModifier(HotkeyPage, 'Alt', RowSel, ScaleX(80));
  SelShift := CreateModifier(HotkeyPage, 'Shift', RowSel, ScaleX(160));
  SelShift.Width := ScaleX(80);
  SelWin := CreateModifier(HotkeyPage, 'Win', RowSel, ScaleX(250));

  KeyLabel1 := TNewStaticText.Create(HotkeyPage);
  KeyLabel1.Parent := HotkeyPage.Surface;
  KeyLabel1.Caption := 'Key:';
  KeyTop1 := RowSel + SelCtrl.Height + ScaleY(8);
  KeyLabel1.Top := KeyTop1 + ScaleY(2);
  KeyLabel1.Left := 0;

  SelKey := TNewComboBox.Create(HotkeyPage);
  SelKey.Parent := HotkeyPage.Surface;
  SelKey.Style := csDropDownList;
  SelKey.Top := KeyTop1;
  SelKey.Left := ScaleX(50);
  SelKey.Width := ScaleX(180);
  SelKey.OnChange := @UpdatePreviews;
  FillKeyCombo(SelKey);

  SelPreview := TNewStaticText.Create(HotkeyPage);
  SelPreview.Parent := HotkeyPage.Surface;
  SelPreview.Top := SelKey.Top + SelKey.Height + ScaleY(8);
  SelPreview.Left := 0;
  SelPreview.Caption := 'Fix selection: Ctrl+Alt+Space';
  SelPreview.Font.Style := [fsBold];

  TitleAll := TNewStaticText.Create(HotkeyPage);
  TitleAll.Parent := HotkeyPage.Surface;
  TitleAll.Caption := 'Fix all (select the whole field first)';
  TitleAll.Font.Style := [fsBold];
  TitleAll.Top := SelPreview.Top + SelPreview.Height + ScaleY(16);
  TitleAll.Left := 0;

  RowAll := TitleAll.Top + TitleAll.Height + ScaleY(6);
  AllCtrl := CreateModifier(HotkeyPage, 'Ctrl', RowAll, 0);
  AllAlt := CreateModifier(HotkeyPage, 'Alt', RowAll, ScaleX(80));
  AllShift := CreateModifier(HotkeyPage, 'Shift', RowAll, ScaleX(160));
  AllShift.Width := ScaleX(80);
  AllWin := CreateModifier(HotkeyPage, 'Win', RowAll, ScaleX(250));

  KeyLabel2 := TNewStaticText.Create(HotkeyPage);
  KeyLabel2.Parent := HotkeyPage.Surface;
  KeyLabel2.Caption := 'Key:';
  KeyTop2 := RowAll + AllCtrl.Height + ScaleY(8);
  KeyLabel2.Top := KeyTop2 + ScaleY(2);
  KeyLabel2.Left := 0;

  AllKey := TNewComboBox.Create(HotkeyPage);
  AllKey.Parent := HotkeyPage.Surface;
  AllKey.Style := csDropDownList;
  AllKey.Top := KeyTop2;
  AllKey.Left := ScaleX(50);
  AllKey.Width := ScaleX(180);
  AllKey.OnChange := @UpdatePreviews;
  FillKeyCombo(AllKey);

  AllPreview := TNewStaticText.Create(HotkeyPage);
  AllPreview.Parent := HotkeyPage.Surface;
  AllPreview.Top := AllKey.Top + AllKey.Height + ScaleY(8);
  AllPreview.Left := 0;
  AllPreview.Caption := 'Fix all: Ctrl+F3';
  AllPreview.Font.Style := [fsBold];

  HintLabel := TNewStaticText.Create(HotkeyPage);
  HintLabel.Parent := HotkeyPage.Surface;
  HintLabel.Top := AllPreview.Top + AllPreview.Height + ScaleY(12);
  HintLabel.Left := 0;
  HintLabel.AutoSize := False;
  HintLabel.WordWrap := True;
  HintLabel.Width := HotkeyPage.SurfaceWidth;
  HintLabel.Height := ScaleY(52);
  HintLabel.Caption := 'Fix selection corrects the highlighted text. Fix all selects the whole field first. Either shortcut can be disabled. After setup, open Settings in the tray for sensitivity, a Translate shortcut (Ctrl+Alt+T), and optional online AI.';

  ApplyHotkeyToControls(ChosenHotkey, SelCtrl, SelAlt, SelShift, SelWin, SelKey);
  ApplyHotkeyToControls(ChosenHotkeyAll, AllCtrl, AllAlt, AllShift, AllWin, AllKey);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Key, SelSpec, AllSpec: String;
begin
  Result := True;
  if (HotkeyPage <> nil) and (CurPageID = HotkeyPage.ID) then
  begin
    Key := KeyNameFromCombo(SelKey);
    if (not IsDisabledSpec(Key)) and (Key <> '') and (not IsFunctionKey(Key)) and
       (not SelCtrl.Checked) and (not SelAlt.Checked) and (not SelShift.Checked) and (not SelWin.Checked) then
    begin
      MsgBox('Please add at least one modifier (Ctrl, Alt, Shift, or Win) for Fix selection, unless you use a function key or None/Disabled.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    Key := KeyNameFromCombo(AllKey);
    if (not IsDisabledSpec(Key)) and (Key <> '') and (not IsFunctionKey(Key)) and
       (not AllCtrl.Checked) and (not AllAlt.Checked) and (not AllShift.Checked) and (not AllWin.Checked) then
    begin
      MsgBox('Please add at least one modifier (Ctrl, Alt, Shift, or Win) for Fix all, unless you use a function key or None/Disabled.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    SelSpec := HotkeyFromControls(SelCtrl, SelAlt, SelShift, SelWin, SelKey);
    AllSpec := HotkeyFromControls(AllCtrl, AllAlt, AllShift, AllWin, AllKey);
    if (not IsDisabledSpec(SelSpec)) and (not IsDisabledSpec(AllSpec)) and (CompareText(SelSpec, AllSpec) = 0) then
    begin
      MsgBox('Please choose two different shortcuts, or disable one of them.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    ChosenHotkey := SelSpec;
    ChosenHotkeyAll := AllSpec;
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
      Existing := ReadExistingIni('hotkey', '');
      if Existing <> '' then
        ApplyHotkeyToControls(Existing, SelCtrl, SelAlt, SelShift, SelWin, SelKey);
    end;
    if Trim(GetCommandlineParam('HOTKEYALL')) = '' then
    begin
      Existing := ReadExistingIni('hotkey_all', '');
      if Existing <> '' then
        ApplyHotkeyToControls(Existing, AllCtrl, AllAlt, AllShift, AllWin, AllKey);
    end;
  end;
end;

function ResolveHotkey(const CmdName, ExistingKey, Chosen, DefaultValue: String): String;
var
  Cmd, Existing: String;
begin
  Cmd := Trim(GetCommandlineParam(CmdName));
  if Cmd <> '' then
    Result := Cmd
  else if WizardSilent then
  begin
    Existing := ReadExistingIni(ExistingKey, '');
    if Existing <> '' then
      Result := Existing
    else if Chosen <> '' then
      Result := Chosen
    else
      Result := DefaultValue;
  end
  else if Chosen <> '' then
    Result := Chosen
  else
    Result := DefaultValue;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Path: String;
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    ChosenHotkey := ResolveHotkey('HOTKEY', 'hotkey', ChosenHotkey, DefaultHotkey);
    ChosenHotkeyAll := ResolveHotkey('HOTKEYALL', 'hotkey_all', ChosenHotkeyAll, DefaultHotkeyAll);
    Path := ExpandConstant('{app}\config.ini');
    SetIniString('TinyFix', 'hotkey', ChosenHotkey, Path);
    SetIniString('TinyFix', 'hotkey_all', ChosenHotkeyAll, Path);
    SetIniString('TinyFix', 'hotkey_translate', ResolveHotkey('HOTKEYTRANSLATE', 'hotkey_translate', '', DefaultHotkeyTranslate), Path);
    Log('Wrote hotkey=' + ChosenHotkey + ' hotkey_all=' + ChosenHotkeyAll + ' to ' + Path);
    if AppWasRunning or HasCommandlineFlag('LAUNCH') then
    begin
      if AppWasRunning then
        Log('Relaunching TinyFix because it was running before install')
      else
        Log('Launching TinyFix because /LAUNCH was given');
      Exec(ExpandConstant('{app}\{#MyAppExeName}'), '', ExpandConstant('{app}'), SW_SHOWNORMAL, ewNoWait, ResultCode);
    end;
  end;
end;

function TinyFixStopScript: String;
begin
  Result := ExpandConstant('{tmp}\stop-tinyfix-in-dir.ps1');
  if not FileExists(Result) then
  begin
    try
      ExtractTemporaryFile('stop-tinyfix-in-dir.ps1');
    except
    end;
  end;
  if not FileExists(Result) then
    Result := ExpandConstant('{app}\stop-tinyfix-in-dir.ps1');
end;

function RunTinyFixStopScript(const ListOnly: Boolean): Integer;
var
  Script, AppDir, Ps, Params: String;
  ResultCode: Integer;
begin
  Result := -1;
  Script := TinyFixStopScript;
  if not FileExists(Script) then
  begin
    Log('stop-tinyfix-in-dir.ps1 was not found; not stopping any TinyFix process');
    Exit;
  end;
  AppDir := ExpandConstant('{app}');
  Ps := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
  if not FileExists(Ps) then
    Ps := ExpandConstant('{syswow64}\WindowsPowerShell\v1.0\powershell.exe');
  Params := '-NoProfile -ExecutionPolicy Bypass -File "' + Script + '" -AppDir "' + AppDir + '"';
  if ListOnly then
    Params := Params + ' -RunningCheck';
  if not Exec(Ps, Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Log('Could not run the TinyFix stop script');
    Result := -1;
    Exit;
  end;
  Result := ResultCode;
end;

function TinyFixIsRunningInApp: Boolean;
begin
  { Exit code 10 from -RunningCheck means a TinyFix.exe from the install folder is running. }
  Result := RunTinyFixStopScript(True) = 10;
end;

procedure StopTinyFixInApp;
var
  I, ResultCode: Integer;
begin
  Log('Stopping TinyFix.exe only if it is running from ' + ExpandConstant('{app}'));
  ResultCode := RunTinyFixStopScript(False);
  Log('Targeted TinyFix stop finished with code ' + IntToStr(ResultCode));
  I := 0;
  while (I < 15) and TinyFixIsRunningInApp do
  begin
    Log('TinyFix.exe in the install folder is still running; retrying stop');
    Sleep(200);
    RunTinyFixStopScript(False);
    Inc(I);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  NeedsRestart := False;
  Result := '';
  AppWasRunning := TinyFixIsRunningInApp;
  if AppWasRunning then
    Log('TinyFix was running from the install folder; it will be closed and relaunched after install');
  StopTinyFixInApp;
  if TinyFixIsRunningInApp then
    Log('Warning: TinyFix.exe in the install folder is still running after the close attempt');
end;

function InitializeUninstall(): Boolean;
begin
  StopTinyFixInApp;
  Sleep(400);
  Result := True;
end;
