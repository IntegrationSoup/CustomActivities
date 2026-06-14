@echo on
setlocal

set "CFG=Release"
set "PROJECT=%~dp0Setup.AwsActivities.wixproj"
set "ACTIVITY_PROJECT=%~dp0..\AmazonActivities\AmazonActivities\AmazonActivities.csproj"
set "DOWNLOAD_DIR=%USERPROFILE%\hl7soup.com\Development - Documents\Downloads\4.0\Custom Activities"
set "INSTALLER_VERSION=%~1"

if not exist "%DOWNLOAD_DIR%" mkdir "%DOWNLOAD_DIR%"

dotnet build "%ACTIVITY_PROJECT%" ^
  -c %CFG% ^
  -t:Rebuild ^
  /nologo /v:minimal

if errorlevel 1 (
  echo **************************************
  echo ** AWS Activities build FAILED
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
  echo ** AWS Activities installer FAILED
  echo **************************************
  pause
  exit /b 1
)

set "MSI_OUT=%~dp0bin\%CFG%\en-us\IntegrationSoup.AwsActivities.msi"
if not exist "%MSI_OUT%" set "MSI_OUT=%~dp0bin\%CFG%\IntegrationSoup.AwsActivities.msi"

copy "%MSI_OUT%" "%DOWNLOAD_DIR%\" /y
if errorlevel 1 (
  echo **************************************
  echo ** Installer built but COPY FAILED
  echo **************************************
  pause
  exit /b 1
)

echo **************************************
echo ** AWS Activities installer built and copied
echo ** %MSI_OUT%
echo **************************************
pause
