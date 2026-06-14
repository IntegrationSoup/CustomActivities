@echo on
setlocal

set "CFG=Release"
set "PROJECT=%~dp0Setup.HtmlToPdfActivities.wixproj"
set "ACTIVITY_PROJECT=%~dp0..\HtmlToPdfActivities\HtmlToPdfActivities.csproj"
set "DOWNLOAD_DIR=%USERPROFILE%\hl7soup.com\Development - Documents\Downloads\4.0\Custom Activities"
set "INSTALLER_VERSION=%~1"

if not exist "%DOWNLOAD_DIR%" mkdir "%DOWNLOAD_DIR%"

dotnet build "%ACTIVITY_PROJECT%" ^
  -c %CFG% ^
  -f net48 ^
  -t:Rebuild ^
  /nologo /v:minimal

if errorlevel 1 (
  echo **************************************
  echo ** HTML to PDF activity build FAILED
  echo **************************************
  pause
  exit /b 1
)

dotnet build "%PROJECT%" ^
  -c %CFG% ^
  -t:Rebuild ^
  /p:InstallerVersion=%INSTALLER_VERSION% ^
  /nologo /v:minimal

if errorlevel 1 (
  echo **************************************
  echo ** HTML to PDF installer FAILED
  echo **************************************
  pause
  exit /b 1
)

set "MSI_OUT=%~dp0bin\%CFG%\en-us\HtmlToPdfActivities.msi"
if not exist "%MSI_OUT%" set "MSI_OUT=%~dp0bin\%CFG%\HtmlToPdfActivities.msi"

copy "%MSI_OUT%" "%DOWNLOAD_DIR%\" /y
if errorlevel 1 (
  echo **************************************
  echo ** Installer built but COPY FAILED
  echo **************************************
  pause
  exit /b 1
)

echo **************************************
echo ** HTML to PDF installer built and copied
echo ** %MSI_OUT%
echo **************************************
pause
