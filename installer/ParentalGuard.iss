; ParentalGuard — trình cài đặt (Inno Setup 6). Thiết kế: Architecture/11-deployment-release-architecture.md.
; Build:  ISCC.exe /DAppVersion=0.9.0 installer\ParentalGuard.iss
; Đầu vào: publish\release\ParentalGuard (bản phát hành ParentalGuardDeveloperMode=false, chỉ model Marqo) — xem docs/BUILD-FLAGS.md.

#ifndef AppVersion
  #define AppVersion "0.9.0"
#endif
#define SourceDir "..\publish\release\ParentalGuard"

[Setup]
AppId={{6F7C2B1E-4D3A-4F8B-9C21-5A0E7D3B9F42}
AppName=ParentalGuard
AppVersion={#AppVersion}
AppVerName=ParentalGuard {#AppVersion}
AppPublisher=ParentalGuard (open-source community project)
AppPublisherURL=https://github.com/loiefy/ParentalGuard
AppSupportURL=https://github.com/loiefy/ParentalGuard/issues
VersionInfoVersion={#AppVersion}
; Đường dẫn cố định: Service/Watchdog/Uninstaller dùng đúng %ProgramFiles%\ParentalGuard (InstallPaths/UninstallerPaths).
DefaultDirName={autopf64}\ParentalGuard
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
; ANTI (chống gỡ trái phép): KHÔNG dùng trình gỡ của Inno (gỡ được mà không cần mật khẩu). Mục gỡ cài đặt trong Settings/
; Control Panel trỏ tới ParentalGuard.Uninstaller.exe (bắt mật khẩu phụ huynh) — xem [Registry]; Uninstaller tự xoá mục này.
Uninstallable=no
CloseApplications=no
OutputDir=..\dist
OutputBaseFilename=ParentalGuard-Setup-{#AppVersion}-win-x64
SetupIconFile=..\src\ParentalGuard.UI\Assets\ParentalGuard.ico
WizardStyle=modern
; lzma2/ultra64 làm ISCC (32-bit) hết bộ nhớ với ~310 MB đầu vào — dùng mức max, nén trong tiến trình riêng.
Compression=lzma2/max
SolidCompression=yes
LZMAUseSeparateProcess=yes

[Languages]
Name: "vi"; MessagesFile: "Languages\Vietnamese.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "pt"; MessagesFile: "compiler:Languages\Portuguese.isl"

[CustomMessages]
vi.DesktopIcon=Tạo biểu tượng trên màn hình Desktop
en.DesktopIcon=Create a desktop shortcut
fr.DesktopIcon=Créer un raccourci sur le Bureau
es.DesktopIcon=Crear un acceso directo en el escritorio
pt.DesktopIcon=Criar um atalho no ambiente de trabalho
vi.OpenDashboard=Mở ParentalGuard
en.OpenDashboard=Open ParentalGuard
fr.OpenDashboard=Ouvrir ParentalGuard
es.OpenDashboard=Abrir ParentalGuard
pt.OpenDashboard=Abrir o ParentalGuard
vi.StartingServices=Đang khởi động dịch vụ bảo vệ…
en.StartingServices=Starting the protection services…
fr.StartingServices=Démarrage des services de protection…
es.StartingServices=Iniciando los servicios de protección…
pt.StartingServices=A iniciar os serviços de proteção…
vi.ServicesFailed=Không khởi động được dịch vụ ParentalGuard. Vui lòng khởi động lại máy; nếu vẫn lỗi, hãy cài lại.
en.ServicesFailed=The ParentalGuard services could not be started. Please restart the computer; if the problem persists, reinstall.
fr.ServicesFailed=Impossible de démarrer les services ParentalGuard. Redémarrez l'ordinateur ; si le problème persiste, réinstallez.
es.ServicesFailed=No se pudieron iniciar los servicios de ParentalGuard. Reinicie el equipo; si el problema continúa, vuelva a instalar.
pt.ServicesFailed=Não foi possível iniciar os serviços do ParentalGuard. Reinicie o computador; se o problema persistir, reinstale.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "stop-services.ps1"; Flags: dontcopy
Source: "setup-services.ps1"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\ParentalGuard"; Filename: "{app}\ParentalGuard.UI.exe"
Name: "{autodesktop}\ParentalGuard"; Filename: "{app}\ParentalGuard.UI.exe"; Tasks: desktopicon

[Registry]
Root: HKLM64; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ParentalGuard"; ValueType: string; ValueName: "DisplayName"; ValueData: "ParentalGuard"
Root: HKLM64; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ParentalGuard"; ValueType: string; ValueName: "DisplayVersion"; ValueData: "{#AppVersion}"
Root: HKLM64; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ParentalGuard"; ValueType: string; ValueName: "Publisher"; ValueData: "ParentalGuard (open-source community project)"
Root: HKLM64; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ParentalGuard"; ValueType: string; ValueName: "DisplayIcon"; ValueData: "{app}\ParentalGuard.UI.exe"
Root: HKLM64; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ParentalGuard"; ValueType: string; ValueName: "InstallLocation"; ValueData: "{app}"
Root: HKLM64; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ParentalGuard"; ValueType: string; ValueName: "UninstallString"; ValueData: """{app}\ParentalGuard.Uninstaller.exe"""
Root: HKLM64; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ParentalGuard"; ValueType: string; ValueName: "URLInfoAbout"; ValueData: "https://github.com/loiefy/ParentalGuard"
Root: HKLM64; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ParentalGuard"; ValueType: dword; ValueName: "NoModify"; ValueData: 1
Root: HKLM64; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ParentalGuard"; ValueType: dword; ValueName: "NoRepair"; ValueData: 1

[Run]
Filename: "{app}\ParentalGuard.UI.exe"; Description: "{cm:OpenDashboard}"; Flags: postinstall nowait skipifsilent runasoriginaluser

[Code]
function RunPowerShellScript(const ScriptName, Arguments: String): Integer;
var
  ResultCode: Integer;
begin
  ExtractTemporaryFile(ScriptName);
  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
      '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\' + ScriptName) + '" ' + Arguments,
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    ResultCode := -1;
  Result := ResultCode;
end;

{ Cài đè bản cũ: dừng Service + Watchdog (chuyển Disabled trước vì 2 bên canh chừng lẫn nhau) và các tiến trình đang giữ file. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  RunPowerShellScript('stop-services.ps1', '');
  Result := '';
end;

{ Chép file xong: tính dung lượng cho mục gỡ cài đặt, đăng ký/cập nhật 2 service rồi khởi động. }
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    WizardForm.StatusLabel.Caption := CustomMessage('StartingServices');
    if RunPowerShellScript('setup-services.ps1', '-AppDir "' + ExpandConstant('{app}') + '"') <> 0 then
      SuppressibleMsgBox(CustomMessage('ServicesFailed'), mbError, MB_OK, IDOK);
  end;
end;
