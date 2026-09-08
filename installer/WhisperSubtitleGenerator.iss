; Inno Setup script for Whisper Subtitle Generator.
;
; Build it with installer\build-installer.ps1, which publishes the app first and passes the
; version in. Compiling this file on its own will fail on a missing publish folder, which is
; deliberate - it stops a stale build being packaged silently.

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

#define AppName        "Whisper Subtitle Generator"
#define AppPublisher   "Ramchandra Hegde"
#define AppUrl         "https://github.com/rmhegde/WhisperSubtitleGenerator"
#define AppExe         "WhisperSubtitleGenerator.exe"
#define PublishDir     "..\artifacts\publish"

[Setup]
; AppId must never change once released - it is how Windows recognises an upgrade rather than a
; second parallel installation. Generating a fresh GUID later would leave users with two copies.
AppId={{7C4E2A91-3F6B-4D58-9E2A-1B8D5C0F4A73}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}

; Install per-user by default so no administrator prompt appears, but let the user choose an
; all-users install if they want one. {autopf} and {autoprograms} then resolve correctly for
; whichever mode was picked - hardcoding {pf} would break the per-user path.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\WhisperSubtitleGenerator
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
AllowNoIcons=yes

OutputDir=..\artifacts\installer
OutputBaseFilename=WhisperSubtitleGenerator-{#AppVersion}-setup
SetupIconFile=
Compression=lzma2/max
SolidCompression=yes
; The payload is a self-contained .NET app - a few hundred near-identical framework DLLs, which
; solid LZMA2 compresses far better than the default.

WizardStyle=modern
DisableWelcomePage=no
LicenseFile=..\LICENSE
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
; The whole self-contained publish. recursesubdirs picks up runtimes\win-x64, which holds the
; native whisper.cpp binaries - without them the app compiles and starts but throws on the first
; transcription with a missing-native-library error.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
// The app caches downloaded ffmpeg (~100 MB) and Whisper models (up to ~3 GB) under LocalAppData.
// Those are deliberately NOT removed automatically: a user reinstalling or upgrading would
// otherwise have to re-download several gigabytes. Ask instead, and only on a real uninstall.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\WhisperSubtitleGenerator');
    if DirExists(DataDir) then
    begin
      if MsgBox('Also delete the downloaded speech models and ffmpeg?' + #13#10 + #13#10 +
                'These live in:' + #13#10 + DataDir + #13#10 + #13#10 +
                'They can be several gigabytes. Keep them if you plan to reinstall - they will '
                + 'be reused instead of downloaded again.',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      begin
        DelTree(DataDir, True, True, True);
      end;
    end;
  end;
end;
