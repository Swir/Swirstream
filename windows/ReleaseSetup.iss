#ifndef PublishDir
  #error Pass /DPublishDir=<tested windows/build.ps1 output>
#endif
#define AppVersion GetVersionNumbersString(PublishDir + "\SwirStreamAI.dll")
#define DisplayVersion Copy(AppVersion,1,RPos(".",AppVersion)-1)
[Setup]
AppId=SwirStreamAI
AppName=SwirStream AI
AppPublisher=Swir
AppVersion={#DisplayVersion}
DefaultDirName={localappdata}\Programs\SwirStreamAI
DisableDirPage=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DefaultGroupName=SwirStream AI
DisableProgramGroupPage=yes
OutputBaseFilename=SwirStreamAI_{#DisplayVersion}_Setup_x64
UninstallDisplayIcon={app}\SwirStreamAI.exe
Compression=lzma2/fast
SolidCompression=yes
SetupIconFile={#PublishDir}\assets\swirstream-v070.ico
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
Uninstallable=not IsTestInstall
[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "pl"; MessagesFile: "compiler:Languages\Polish.isl"
[Files]
Source: "{#PublishDir}\*"; Excludes: "libmpv-2.dll,third-party-source\*,data\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs
Source: "{tmp}\libmpv-2.dll"; DestDir: "{app}\player"; Flags: external ignoreversion; Check: InstallEngine; ExternalSize: 121624576
[Icons]
Name: "{userprograms}\SwirStream AI"; Filename: "{app}\SwirStreamAI.exe"; Check: not IsTestInstall
[Run]
Filename: "{app}\SwirStreamAI.exe"; Flags: postinstall nowait skipifsilent; Description: "SwirStream AI"
Filename: "{app}\SwirStreamAI.exe"; Flags: nowait; Check: Relaunch
[Code]
const
  EngineHash = 'c91949577ae9cc2d500208da51f2c4a24cfa7a02d0ce6e130bf67ec738b450bb';
  ArchiveHash = 'df7c433a2d481fdaf6163b201f26abbb0eff0c215c51368397088c6d8f7a001b';
  EngineUrl = 'https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/20261107/mpv-dev-x86_64-20261107-git-9028187538.7z';
var
  CopyEngine: Boolean;
  DownloadPage: TDownloadWizardPage;
function IsTestInstall: Boolean;
begin Result := ExpandConstant('{param:SWIRTEST|0}') = '1'; end;
function Relaunch: Boolean;
begin Result := WizardSilent and (ExpandConstant('{param:RELAUNCH|0}') = '1'); end;
function InstallEngine: Boolean;
begin Result := CopyEngine; end;
procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage('Swir Player', 'libmpv / FFmpeg', nil);
end;
function PrepareToInstall(var NeedsRestart: Boolean): String;
var Existing, Dir: String; Code: Integer;
begin
  Result := '';
  try
    Dir := ExpandConstant('{tmp}');
    Existing := ExpandConstant('{app}\player\libmpv-2.dll');
    CopyEngine := True;
    if FileExists(Existing) then CopyEngine := CompareText(GetSHA256OfFile(Existing), EngineHash) <> 0;
    if not CopyEngine then exit;
    DownloadPage.Clear;
    DownloadPage.Add(EngineUrl, 'swir-mpv.7z', ArchiveHash);
    DownloadPage.Show;
    try DownloadPage.Download; finally DownloadPage.Hide; end;
    if not Exec(ExpandConstant('{sys}\tar.exe'), '-xf "' + Dir + '\swir-mpv.7z" -C "' + Dir + '" libmpv-2.dll', '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0)
      then RaiseException('Could not extract libmpv / Nie udało się rozpakować libmpv.');
    if CompareText(GetSHA256OfFile(Dir + '\libmpv-2.dll'), EngineHash) <> 0 then RaiseException('Invalid libmpv SHA-256.');
  except Result := GetExceptionMessage; end;
end;
