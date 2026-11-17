@echo off
setlocal
pushd "%~dp0"
set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Microsoft .NET Framework C# compiler not found.
  popd
  exit /b 1
)
if not exist release mkdir release
"%CSC%" /nologo /noconfig /nostdlib- /codepage:65001 /langversion:5 /target:winexe /platform:anycpu /optimize+ /win32manifest:assets\app.manifest /win32icon:assets\nabz.ico /resource:assets\catalog.tsv,Nabz.Catalog /resource:assets\Vazirmatn-Regular.ttf,Orbit.Regular /resource:assets\Vazirmatn-Bold.ttf,Orbit.Bold /resource:assets\orbit-cover.jpg,Orbit.Cover /resource:assets\OFL.txt,Orbit.FontLicense /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /out:release\NabzOrbit.exe src\*.cs
set "RC=%ERRORLEVEL%"
if "%RC%"=="0" echo Built release\NabzOrbit.exe
popd
exit /b %RC%
