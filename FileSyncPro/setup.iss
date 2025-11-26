; Inno Setup Script for Petrofac FileSyncPro
; This script creates a Windows installer package

#define MyAppName "Petrofac FileSyncPro"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Petrofac"
#define MyAppURL "https://www.petrofac.com"
#define MyAppExeName "FileSyncPro.exe"
#define MyAppIcon "app.ico"

[Setup]
; Basic App Information
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; Installation Directories
DefaultDirName={autopf}\Petrofac\FileSyncPro
DefaultGroupName=Petrofac FileSyncPro
DisableProgramGroupPage=yes

; Output
OutputDir=.\Installer
OutputBaseFilename=PetrofacFileSyncPro_Setup_v{#MyAppVersion}

; Compression
Compression=lzma2
SolidCompression=yes

; Icons and Images
SetupIconFile={#MyAppIcon}
UninstallDisplayIcon={app}\{#MyAppExeName}

; Privileges
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog

; Visual Style
WizardStyle=modern
WizardImageFile=compiler:WizModernImage-IS.bmp
WizardSmallImageFile=compiler:WizModernSmallImage-IS.bmp

; Other
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Main Application Files
Source: ".\bin\Release\net6.0-windows\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Icon and Logo
Source: ".\app.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: ".\logo.jpg"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Start Menu Icons
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppIcon}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
; Desktop Icon (optional, based on task selection)
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppIcon}"; Tasks: desktopicon

[Run]
; Option to launch application after installation
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
// Check if .NET 6.0 Runtime is installed
function IsDotNet6Installed: Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec('dotnet', '--list-runtimes', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function InitializeSetup: Boolean;
begin
  Result := True;

  // Check for .NET 6.0 Runtime
  if not IsDotNet6Installed then
  begin
    if MsgBox('This application requires .NET 6.0 Desktop Runtime to run.' + #13#10 +
              'Would you like to download and install it now?' + #13#10#13#10 +
              'Click Yes to open the download page, or No to cancel installation.',
              mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/6.0/runtime', '', '', SW_SHOW, ewNoWait, ResultCode);
    end;
    Result := False;
  end;
end;

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
