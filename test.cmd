@echo off
setlocal
pushd "%~dp0"
set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" exit /b 1
if not exist release mkdir release
"%CSC%" /nologo /noconfig /nostdlib- /codepage:65001 /langversion:5 /target:exe /main:Nabz.Tests /platform:anycpu /optimize+ /resource:assets\catalog.tsv,Nabz.Catalog /resource:assets\Vazirmatn-Regular.ttf,Orbit.Regular /resource:assets\Vazirmatn-Bold.ttf,Orbit.Bold /resource:assets\orbit-cover.jpg,Orbit.Cover /resource:assets\OFL.txt,Orbit.FontLicense /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /out:release\Nabz.Tests.exe src\*.cs tests\*.cs
if errorlevel 1 exit /b 1
release\Nabz.Tests.exe %*
set "RC=%ERRORLEVEL%"
popd
exit /b %RC%
