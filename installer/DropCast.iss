; DropCast Windows Installer - Inno Setup Script
; https://jrsoftware.org/isinfo.php

#define MyAppName "DropCast"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "DropCast"
#define MyAppExeName "DropCast.exe"
#define MyAppURL "https://github.com/Loic-Lemarchand/DropCast"

; Paths relative to this .iss file
#define BuildOutput "..\bin\Release"
#define IconFile "..\DropCast.ico"

[Setup]
AppId={{E7A3F2C1-48D6-4B5A-9C1E-3D7F8A2B6E09}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=output
OutputBaseFilename=DropCast-Setup-{#MyAppVersion}
SetupIconFile={#IconFile}
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
MinVersion=6.1sp1
DisableProgramGroupPage=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Main executable and config
Source: "{#BuildOutput}\DropCast.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\DropCast.exe.config"; DestDir: "{app}"; Flags: ignoreversion

; NLog config
Source: "{#BuildOutput}\NLog.config"; DestDir: "{app}"; Flags: ignoreversion

; Bundled encrypted token (optional - only if it was built with one)
Source: "{#BuildOutput}\token.enc"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

; Managed DLLs (exclude XML doc files and PDBs to save space)
Source: "{#BuildOutput}\AngleSharp.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Discord.Net.Commands.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Discord.Net.Core.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Discord.Net.Interactions.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Discord.Net.Rest.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Discord.Net.Webhook.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Discord.Net.WebSocket.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\LibVLCSharp.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\LibVLCSharp.WinForms.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Microsoft.Bcl.AsyncInterfaces.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Microsoft.Extensions.Configuration.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Microsoft.Extensions.Configuration.Abstractions.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Microsoft.Extensions.Configuration.Binder.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Microsoft.Extensions.DependencyInjection.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Microsoft.Extensions.DependencyInjection.Abstractions.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Microsoft.Extensions.Logging.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Microsoft.Extensions.Logging.Abstractions.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Microsoft.Extensions.Logging.Configuration.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Microsoft.Extensions.Logging.Console.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Microsoft.Extensions.Options.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Microsoft.Extensions.Options.ConfigurationExtensions.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Microsoft.Extensions.Primitives.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\Newtonsoft.Json.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\NLog.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\NLog.Extensions.Logging.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\System.Buffers.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\System.Collections.Immutable.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\System.Diagnostics.DiagnosticSource.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\System.Interactive.Async.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\System.IO.Pipelines.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\System.Linq.Async.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\System.Memory.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\System.Numerics.Vectors.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\System.Reactive.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\System.Runtime.CompilerServices.Unsafe.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\System.Text.Encoding.CodePages.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\System.Text.Encodings.Web.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\System.Text.Json.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\System.Threading.Tasks.Extensions.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\System.ValueTuple.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\websocket-sharp.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildOutput}\YoutubeExplode.dll"; DestDir: "{app}"; Flags: ignoreversion

; LibVLC native libraries (x64 only - modern Windows is 64-bit)
Source: "{#BuildOutput}\libvlc\win-x64\*"; DestDir: "{app}\libvlc\win-x64"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
function IsDotNetFramework472Installed(): Boolean;
var
  Release: Cardinal;
begin
  Result := False;
  if RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) then
    Result := (Release >= 461808); // 4.7.2 = 461808
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not IsDotNetFramework472Installed() then
  begin
    MsgBox('DropCast requires .NET Framework 4.7.2 or later.'#13#10#13#10
      + 'Please install it from:'#13#10
      + 'https://dotnet.microsoft.com/download/dotnet-framework/net472'#13#10#13#10
      + 'Setup will now exit.', mbCriticalError, MB_OK);
    Result := False;
  end;
end;
