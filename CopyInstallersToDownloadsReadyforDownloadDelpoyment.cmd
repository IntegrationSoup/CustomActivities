@echo off
setlocal

set "SOURCE_ROOT=%~dp0"
set "DESTINATION=C:\Users\jason\hl7soup.com\Development - Documents\Website\downloads\CustomActivities"

if not exist "%DESTINATION%" (
    echo Destination folder not found:
    echo   %DESTINATION%
    exit /b 1
)

set "FILES=^
Setup.HtmlToPdfActivities\bin\Release\IntegrationSoup.HtmlToPdfActivities.msi^
 Setup.ValidateHl7Transformer\bin\Release\IntegrationSoup.ValidateHl7Transformer.msi^
 Setup.AzureActivities\bin\Release\IntegrationSoup.AzureActivities.msi^
 Setup.AwsActivities\bin\Release\IntegrationSoup.AwsActivities.msi^
 Setup.EncryptionActivities\bin\Release\IntegrationSoup.EncryptionActivities.msi"

for %%F in (%FILES%) do (
    if not exist "%SOURCE_ROOT%%%~F" (
        echo Missing installer:
        echo   %SOURCE_ROOT%%%~F
        exit /b 1
    )
)

for %%F in (%FILES%) do (
    echo Copying %%~nxF
    copy /Y "%SOURCE_ROOT%%%~F" "%DESTINATION%\%%~nxF" >nul
)

echo.
echo Installers copied to:
echo   %DESTINATION%

endlocal
