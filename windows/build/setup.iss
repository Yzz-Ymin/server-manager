#if Ver < EncodeVer(6,7,3)
  #error Inno Setup 6.7.3 or later is required.
#endif
#ifndef PayloadDir
  #error Pass /DPayloadDir with the extracted Windows package directory.
#endif
#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\packages"
#endif

[Setup]
AppId={{A04B3F6D-8E54-4EC5-8C08-7719D66F5EB2}
AppName=服务器管理
AppVersion={#AppVersion}
AppPublisher=Server Manager contributors
AppPublisherURL=https://github.com/Yzz-Ymin/server-manager
DefaultDirName={code:DefaultInstallDir}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
WizardStyle=modern
CloseApplications=no
RestartApplications=no
SourceDir={#PayloadDir}
OutputDir={#OutputDir}
OutputBaseFilename=ServerManager-{#AppVersion}-windows-setup-unsigned
SetupIconFile={#PayloadDir}\bin\AppIcon.ico
UninstallDisplayIcon={app}\ServerManager.exe
VersionInfoVersion={#AppVersion}.0
Compression=lzma2
SolidCompression=yes

[Languages]
Name: "chinesesimp"; MessagesFile: "{#ChineseLanguageFile}"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "bin\ServerManager.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\server-access.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\server-askpass.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\ServerManager.Core.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\*.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\AppIcon.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userdesktop}\服务器管理"; Filename: "{app}\ServerManager.exe"; WorkingDir: "{app}"
Name: "{userprograms}\服务器管理"; Filename: "{app}\ServerManager.exe"; WorkingDir: "{app}"

[InstallDelete]
Type: files; Name: "{app}\uninstall.ps1"

[UninstallDelete]
Type: files; Name: "{app}\installation.json"

[Run]
Filename: "{app}\ServerManager.exe"; Description: "启动服务器管理"; Flags: nowait postinstall skipifsilent

[Code]
function DefaultInstallDir(Param: String): String;
var
  Shell, Shortcut: Variant;
  LinkPath, Target: String;
begin
  Result := ExpandConstant('{localappdata}\Programs\ServerManager');
  LinkPath := ExpandConstant('{userdesktop}\服务器管理.lnk');
  if not FileExists(LinkPath) then LinkPath := ExpandConstant('{userprograms}\服务器管理.lnk');
  if not FileExists(LinkPath) then Exit;
  try
    Shell := CreateOleObject('WScript.Shell');
    Shortcut := Shell.CreateShortcut(LinkPath);
    Target := Shortcut.TargetPath;
    if (CompareText(ExtractFileName(Target), 'ServerManager.exe') = 0) and
       FileExists(Target) and FileExists(ExtractFilePath(Target) + 'installation.json') then
      Result := ExtractFileDir(Target);
  except
    Log('Existing shortcut could not be read; using the per-user installation directory.');
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
  FrameworkRelease: Cardinal;
begin
  Result := '';
  if FindWindowByWindowName('服务器管理') <> 0 then begin
    Result := '请先完成或取消编辑，并从托盘菜单退出服务器管理，再继续安装。';
    Exit;
  end;
  if not RegQueryDWordValue(HKLM32, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full',
      'Release', FrameworkRelease) or (FrameworkRelease < 528040) then begin
    Result := '需要 .NET Framework 4.8 或更高版本，请通过 Windows 功能或微软官方安装包安装。';
    Exit;
  end;
  if not FileExists(ExpandConstant('{sys}\OpenSSH\ssh.exe')) then begin
    Result := '需要 Windows OpenSSH 客户端，请在 Windows 可选功能中安装后重试。';
    Exit;
  end;
  ExtractTemporaryFile('server-access.exe');
  ExtractTemporaryFile('server-access.exe.config');
  ExtractTemporaryFile('ServerManager.Core.dll');
  if not Exec(ExpandConstant('{tmp}\server-access.exe'), '--help', ExpandConstant('{tmp}'),
      SW_HIDE, ewWaitUntilTerminated, ExitCode) then begin
    Result := '新版程序无法启动，可能被 Windows 应用控制拦截。请使用受信任签名的构建。现有安装尚未修改。';
    Exit;
  end;
  if ExitCode <> 0 then
    Result := '新版执行助手未通过启动检查。请检查安装依赖或应用控制记录。现有安装尚未修改。';
end;

function JsonEscape(Value: String): String;
begin
  StringChangeEx(Value, '\', '\\', True);
  StringChangeEx(Value, '"', '\"', True);
  Result := Value;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Metadata: String;
  Lines: TArrayOfString;
begin
  if CurStep = ssPostInstall then begin
    Metadata := '{"product":"Windows-ServerManager","version":"{#AppVersion}",' +
      '"installer":"inno","installDirectory":"' + JsonEscape(ExpandConstant('{app}')) + '"}';
    SetArrayLength(Lines, 1);
    Lines[0] := Metadata;
    if not SaveStringsToUTF8File(ExpandConstant('{app}\installation.json'), Lines, False) then
      RaiseException('无法保存安装记录，请检查安装目录权限。');
  end;
end;

function InitializeUninstall: Boolean;
begin
  Result := FindWindowByWindowName('服务器管理') = 0;
  if not Result then
    MsgBox('请先完成或取消编辑，并从托盘菜单退出服务器管理，再卸载。', mbError, MB_OK);
end;
