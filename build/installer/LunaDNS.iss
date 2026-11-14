; LunaDNS Setup (KH7). Gọi từ build/publish.ps1:
;   ISCC.exe /DAppVersion=0.9.0 /DPublishDir=<artifacts\publish\win-x64> /O<artifacts> LunaDNS.iss
#ifndef AppVersion
  #error Thiếu /DAppVersion
#endif
#ifndef PublishDir
  #error Thiếu /DPublishDir
#endif

[Setup]
AppId={{6F3C2B9E-1D4A-4E7B-9C55-5A0E8D2F7B31}
AppName=LunaDNS
AppVersion={#AppVersion}
AppVerName=LunaDNS {#AppVersion}
AppPublisher=LunaDNS
AppPublisherURL=https://github.com/lunavo/LunaDNS
DefaultDirName={autopf}\LunaDNS
; Thư mục cố định trong Program Files: --install-system từ chối chỗ khác (K7).
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
AppMutex=LunaDNS,Global\LunaDNS
CloseApplications=no
OutputBaseFilename=LunaDNS-Setup-{#AppVersion}
SetupIconFile=..\..\src\LunaDns.App\Assets\lunadns.ico
UninstallDisplayIcon={app}\LunaDNS.exe
UninstallDisplayName=LunaDNS
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
en.OldCleanupFailed=Không khôi phục được cấu hình mạng của bản LunaDNS đang cài (mã %1). Mở LunaDNS, bấm "Tắt hết & khôi phục", rồi chạy lại Setup. Chi tiết: %ProgramData%\LunaDNS\logs.
en.InstallSystemFailed=LunaDNS đã cài, nhưng chưa đăng ký được tác vụ dọn dẹp lúc khởi động máy (mã %1). App vẫn chạy được. Chạy lại Setup để thử lại. Chi tiết: %ProgramData%\LunaDNS\logs.
en.UninstallFailed=Chưa khôi phục được hết cấu hình mạng (mã %1), nên chưa gỡ LunaDNS. Xem hướng dẫn "Gỡ và dọn" (docs/guide/07) rồi thử lại.
en.LaunchApp=Mở LunaDNS

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\LunaDNS"; Filename: "{app}\LunaDNS.exe"

[Run]
Filename: "{app}\LunaDNS.exe"; Description: "{cm:LaunchApp}"; Flags: postinstall nowait skipifsilent

[Code]
function RunLuna(const Exe, Args: String): Integer;
var
  Code: Integer;
begin
  if not Exec(Exe, Args, '', SW_HIDE, ewWaitUntilTerminated, Code) then
    Code := -1;
  Result := Code;
end;

{ K11: hoàn tác bằng exe cũ trước khi ghi đè. Không có bản cũ thì bỏ qua. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  OldExe: String;
  Code: Integer;
begin
  Result := '';
  OldExe := ExpandConstant('{app}\LunaDNS.exe');
  if FileExists(OldExe) then
  begin
    Code := RunLuna(OldExe, '--cleanup');
    if Code <> 0 then
      Result := FmtMessage(CustomMessage('OldCleanupFailed'), [IntToStr(Code)]);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    Code := RunLuna(ExpandConstant('{app}\LunaDNS.exe'), '--install-system');
    if Code <> 0 then
      SuppressibleMsgBox(FmtMessage(CustomMessage('InstallSystemFailed'), [IntToStr(Code)]), mbError, MB_OK, IDOK);
  end;
end;

{ Hoàn tác + xoá task + xoá %ProgramData%\LunaDNS trước khi xoá file. Lỗi thì dừng gỡ, giữ exe để thử lại. }
function InitializeUninstall(): Boolean;
var
  Code: Integer;
begin
  Result := True;
  Code := RunLuna(ExpandConstant('{app}\LunaDNS.exe'), '--uninstall');
  if Code <> 0 then
  begin
    MsgBox(FmtMessage(CustomMessage('UninstallFailed'), [IntToStr(Code)]), mbError, MB_OK);
    Result := False;
  end;
end;
