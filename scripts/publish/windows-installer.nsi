!include "MUI2.nsh"
!include "LogicLib.nsh"

!define APP_NAME "SimpleVMS"
!define EXE_NAME "Client.Desktop.exe"
!define UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}"
!define VCRT_REGKEY "SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\${VCRT_ARCH}"

Name "${APP_NAME}"
OutFile "${OUT_FILE}"
InstallDir "$PROGRAMFILES64\${APP_NAME}"
InstallDirRegKey HKLM "${UNINST_KEY}" "InstallLocation"
RequestExecutionLevel admin
Unicode true
SetCompressor /SOLID lzma

!define MUI_ICON "${ICON_PATH}"
!define MUI_UNICON "${ICON_PATH}"
!define MUI_ABORTWARNING

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\${EXE_NAME}"
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

Function .onInit
  InitPluginsDir
  SetRegView 64
  ReadRegStr $R0 HKLM "${UNINST_KEY}" "UninstallString"
  ${If} $R0 != ""
    MessageBox MB_OKCANCEL|MB_ICONEXCLAMATION \
      "${APP_NAME} is already installed. Uninstall the previous version and continue?" \
      /SD IDOK IDOK uninst
    Abort
    uninst:
    ClearErrors
    ExecWait '"$R0" /S _?=$INSTDIR'
  ${EndIf}
FunctionEnd

Function InstallVCRedist
  SetRegView 64
  ReadRegDWORD $0 HKLM "${VCRT_REGKEY}" "Installed"
  ${If} $0 == 1
    DetailPrint "Microsoft Visual C++ Redistributable already installed"
    Return
  ${EndIf}

  DetailPrint "Downloading Microsoft Visual C++ Redistributable..."
  NSISdl::download "${VCRT_URL}" "$PLUGINSDIR\vc_redist.exe"
  Pop $0
  ${If} $0 != "success"
    MessageBox MB_ICONEXCLAMATION \
      "Failed to download Microsoft Visual C++ Redistributable: $0$\n$\nInstall will continue but ${APP_NAME} may not run until it is installed manually from https://aka.ms/vs/17/release/"
    Return
  ${EndIf}

  DetailPrint "Installing Microsoft Visual C++ Redistributable..."
  ExecWait '"$PLUGINSDIR\vc_redist.exe" /install /quiet /norestart' $0
  ${If} $0 != 0
  ${AndIf} $0 != 1638
  ${AndIf} $0 != 3010
    MessageBox MB_ICONEXCLAMATION \
      "Microsoft Visual C++ Redistributable install returned exit code $0.$\n$\n${APP_NAME} may not run until it is installed manually from https://aka.ms/vs/17/release/"
  ${EndIf}
FunctionEnd

Section "Install"
  SetRegView 64
  Call InstallVCRedist

  SetOutPath "$INSTDIR"
  File /r "${STAGE_DIR}\*"

  WriteUninstaller "$INSTDIR\Uninstall.exe"

  CreateDirectory "$SMPROGRAMS\${APP_NAME}"
  CreateShortcut "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" "$INSTDIR\${EXE_NAME}"
  CreateShortcut "$SMPROGRAMS\${APP_NAME}\Uninstall.lnk" "$INSTDIR\Uninstall.exe"

  WriteRegStr HKLM "${UNINST_KEY}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKLM "${UNINST_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "${UNINST_KEY}" "DisplayIcon" "$INSTDIR\${EXE_NAME}"
  WriteRegStr HKLM "${UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${UNINST_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKLM "${UNINST_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKLM "${UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKLM "${UNINST_KEY}" "NoRepair" 1
SectionEnd

Section "Uninstall"
  SetRegView 64

  Delete "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"
  Delete "$SMPROGRAMS\${APP_NAME}\Uninstall.lnk"
  RMDir "$SMPROGRAMS\${APP_NAME}"

  RMDir /r "$INSTDIR"

  DeleteRegKey HKLM "${UNINST_KEY}"
SectionEnd
