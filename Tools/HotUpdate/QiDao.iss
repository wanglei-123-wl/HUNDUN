#ifndef ClientSource
  #error ClientSource is required
#endif
#ifndef InstallerOutput
  #error InstallerOutput is required
#endif
#ifndef GameIcon
  #error GameIcon is required
#endif
#ifndef BuildStamp
  #error BuildStamp is required
#endif
#ifndef AppVersionNumber
  #define AppVersionNumber "0.1.3"
#endif
#ifndef ReleaseAppId
  #define ReleaseAppId "{{D226AB43-70DE-446D-B94B-BE2F22C23584}"
#endif
#ifndef AppInstallDir
  #define AppInstallDir "{localappdata}\Programs\QiDao"
#endif
#ifndef DesktopFolder
  #define DesktopFolder "{userdesktop}"
#endif
#ifndef ProgramsFolder
  #define ProgramsFolder "{userprograms}"
#endif
#ifndef ProgramGroupName
  #define ProgramGroupName "齐道"
#endif

[Setup]
AppId={#ReleaseAppId}
AppName=齐道
AppVersion={#AppVersionNumber}
VersionInfoVersion={#AppVersionNumber}
UninstallDisplayName=齐道
VersionInfoProductName=齐道
VersionInfoDescription=齐道安装程序
DefaultDirName={#AppInstallDir}
DefaultGroupName={#ProgramGroupName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#InstallerOutput}
OutputBaseFilename=齐道安装程序_{#BuildStamp}
SetupIconFile={#GameIcon}
UninstallDisplayIcon={app}\齐道.exe
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes
UsePreviousTasks=yes

[Languages]
Name: "chinesesimp"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："

[Files]
Source: "{#ClientSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
Type: filesandordirs; Name: "{app}\齐道_Data\Managed"
Type: filesandordirs; Name: "{app}\齐道_Data\StreamingAssets\aa"

[Icons]
Name: "{#ProgramsFolder}\齐道"; Filename: "{app}\齐道.exe"; WorkingDir: "{app}"
Name: "{#DesktopFolder}\齐道"; Filename: "{app}\齐道.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\齐道.exe"; Description: "启动齐道"; Flags: nowait postinstall skipifsilent
