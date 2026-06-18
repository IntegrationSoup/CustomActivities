@echo off
setlocal

set "SOURCE_ROOT=%~dp0"
set "DESTINATION=C:\Users\jason\hl7soup.com\Development - Documents\Website\downloads\CustomActivities"

if not exist "%DESTINATION%" (
    echo Destination folder not found:
    echo   %DESTINATION%
    exit /b 1
)

if not exist "%SOURCE_ROOT%Setup.HtmlToPdfActivities\bin\Release\IntegrationSoup.HtmlToPdfActivities.msi" (
    echo Missing installer:
    echo   %SOURCE_ROOT%Setup.HtmlToPdfActivities\bin\Release\IntegrationSoup.HtmlToPdfActivities.msi
    exit /b 1
)

if not exist "%SOURCE_ROOT%Setup.RtfToPdfActivities\bin\Release\IntegrationSoup.RtfToPdfActivities.msi" (
    echo Missing installer:
    echo   %SOURCE_ROOT%Setup.RtfToPdfActivities\bin\Release\IntegrationSoup.RtfToPdfActivities.msi
    exit /b 1
)

if not exist "%SOURCE_ROOT%Setup.DataFromPdfActivities\bin\Release\IntegrationSoup.DataFromPdfActivities.msi" (
    echo Missing installer:
    echo   %SOURCE_ROOT%Setup.DataFromPdfActivities\bin\Release\IntegrationSoup.DataFromPdfActivities.msi
    exit /b 1
)

if not exist "%SOURCE_ROOT%Setup.ValidateHl7Transformer\bin\Release\IntegrationSoup.ValidateHl7Transformer.msi" (
    echo Missing installer:
    echo   %SOURCE_ROOT%Setup.ValidateHl7Transformer\bin\Release\IntegrationSoup.ValidateHl7Transformer.msi
    exit /b 1
)

if not exist "%SOURCE_ROOT%Setup.HL7ValueTransformers\bin\Release\IntegrationSoup.HL7ValueTransformers.msi" (
    echo Missing installer:
    echo   %SOURCE_ROOT%Setup.HL7ValueTransformers\bin\Release\IntegrationSoup.HL7ValueTransformers.msi
    exit /b 1
)

if not exist "%SOURCE_ROOT%Setup.AzureActivities\bin\Release\IntegrationSoup.AzureActivities.msi" (
    echo Missing installer:
    echo   %SOURCE_ROOT%Setup.AzureActivities\bin\Release\IntegrationSoup.AzureActivities.msi
    exit /b 1
)

if not exist "%SOURCE_ROOT%Setup.AwsActivities\bin\Release\IntegrationSoup.AwsActivities.msi" (
    echo Missing installer:
    echo   %SOURCE_ROOT%Setup.AwsActivities\bin\Release\IntegrationSoup.AwsActivities.msi
    exit /b 1
)

if not exist "%SOURCE_ROOT%Setup.SftpActivities\bin\Release\IntegrationSoup.SftpActivities.msi" (
    echo Missing installer:
    echo   %SOURCE_ROOT%Setup.SftpActivities\bin\Release\IntegrationSoup.SftpActivities.msi
    exit /b 1
)

if not exist "%SOURCE_ROOT%Setup.EncryptionActivities\bin\Release\IntegrationSoup.EncryptionActivities.msi" (
    echo Missing installer:
    echo   %SOURCE_ROOT%Setup.EncryptionActivities\bin\Release\IntegrationSoup.EncryptionActivities.msi
    exit /b 1
)

echo Copying IntegrationSoup.HtmlToPdfActivities.msi
copy /Y "%SOURCE_ROOT%Setup.HtmlToPdfActivities\bin\Release\IntegrationSoup.HtmlToPdfActivities.msi" "%DESTINATION%\IntegrationSoup.HtmlToPdfActivities.msi" >nul || exit /b 1

echo Copying IntegrationSoup.RtfToPdfActivities.msi
copy /Y "%SOURCE_ROOT%Setup.RtfToPdfActivities\bin\Release\IntegrationSoup.RtfToPdfActivities.msi" "%DESTINATION%\IntegrationSoup.RtfToPdfActivities.msi" >nul || exit /b 1

echo Copying IntegrationSoup.DataFromPdfActivities.msi
copy /Y "%SOURCE_ROOT%Setup.DataFromPdfActivities\bin\Release\IntegrationSoup.DataFromPdfActivities.msi" "%DESTINATION%\IntegrationSoup.DataFromPdfActivities.msi" >nul || exit /b 1

echo Copying IntegrationSoup.ValidateHl7Transformer.msi
copy /Y "%SOURCE_ROOT%Setup.ValidateHl7Transformer\bin\Release\IntegrationSoup.ValidateHl7Transformer.msi" "%DESTINATION%\IntegrationSoup.ValidateHl7Transformer.msi" >nul || exit /b 1

echo Copying IntegrationSoup.HL7ValueTransformers.msi
copy /Y "%SOURCE_ROOT%Setup.HL7ValueTransformers\bin\Release\IntegrationSoup.HL7ValueTransformers.msi" "%DESTINATION%\IntegrationSoup.HL7ValueTransformers.msi" >nul || exit /b 1

echo Copying IntegrationSoup.AzureActivities.msi
copy /Y "%SOURCE_ROOT%Setup.AzureActivities\bin\Release\IntegrationSoup.AzureActivities.msi" "%DESTINATION%\IntegrationSoup.AzureActivities.msi" >nul || exit /b 1

echo Copying IntegrationSoup.AwsActivities.msi
copy /Y "%SOURCE_ROOT%Setup.AwsActivities\bin\Release\IntegrationSoup.AwsActivities.msi" "%DESTINATION%\IntegrationSoup.AwsActivities.msi" >nul || exit /b 1

echo Copying IntegrationSoup.SftpActivities.msi
copy /Y "%SOURCE_ROOT%Setup.SftpActivities\bin\Release\IntegrationSoup.SftpActivities.msi" "%DESTINATION%\IntegrationSoup.SftpActivities.msi" >nul || exit /b 1

echo Copying IntegrationSoup.EncryptionActivities.msi
copy /Y "%SOURCE_ROOT%Setup.EncryptionActivities\bin\Release\IntegrationSoup.EncryptionActivities.msi" "%DESTINATION%\IntegrationSoup.EncryptionActivities.msi" >nul || exit /b 1

echo.
echo Installers copied to:
echo   %DESTINATION%

endlocal
exit /b 0
