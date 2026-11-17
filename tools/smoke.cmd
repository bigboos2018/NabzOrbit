@echo off
setlocal
pushd "%~dp0.."
set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist release\NabzOrbit.exe exit /b 1
"%CSC%" /nologo /noconfig /codepage:65001 /langversion:5 /target:exe /platform:anycpu /r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll /out:release\SmokeRelease.exe tools\SmokeRelease.cs
if errorlevel 1 exit /b 1
release\SmokeRelease.exe release\NabzOrbit.exe
set "RC=%ERRORLEVEL%"
popd
exit /b %RC%
