@echo on
setlocal

set "CFG=Release"
set "PROJECT=%~dp0Setup.HL7ValueTransformers.wixproj"
set "TRANSFORMER_PROJECT=%~dp0..\HL7ValueTransformers\HL7ValueTransformers.csproj"
set "DOWNLOAD_DIR=%USERPROFILE%\hl7soup.com\Development - Documents\Website\downloads\CustomActivities"
set "INSTALLER_VERSION=%~1"

if not exist "%DOWNLOAD_DIR%" mkdir "%DOWNLOAD_DIR%"

dotnet build "%TRANSFORMER_PROJECT%" ^
  -c %CFG% ^
  -t:Rebuild ^
  /nologo /v:minimal

if errorlevel 1 (
  echo **************************************
  echo ** HL7 Value Transformers build FAILED
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
  echo ** HL7 Value Transformers installer FAILED
  echo **************************************
  pause
  exit /b 1
)

set "MSI_OUT=%~dp0bin\%CFG%\en-us\IntegrationSoup.HL7ValueTransformers.msi"
if not exist "%MSI_OUT%" set "MSI_OUT=%~dp0bin\%CFG%\IntegrationSoup.HL7ValueTransformers.msi"

copy "%MSI_OUT%" "%DOWNLOAD_DIR%\" /y
if errorlevel 1 (
  echo **************************************
  echo ** Installer built but COPY FAILED
  echo **************************************
  pause
  exit /b 1
)

echo **************************************
echo ** HL7 Value Transformers installer built and copied
echo ** %MSI_OUT%
echo **************************************
pause
