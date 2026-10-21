@echo off
rem Builds TaskbarNetSpeed for each architecture into the bin folder, using the
rem C# compiler that ships with Windows (.NET Framework 4.x).
rem The version number comes from the VERSION file.
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
set OPTS=/nologo /codepage:65001 /target:winexe /optimize

set /p VERSION=<VERSION
if "%VERSION%"=="" (echo VERSION file is empty & exit /b 1)

if not exist bin mkdir bin

rem Stamp the version into the exe (visible in file Properties and the right-click menu).
set GEN=bin\Version.g.cs
> %GEN% echo [assembly: System.Reflection.AssemblyTitle("TaskbarNetSpeed")]
>>%GEN% echo [assembly: System.Reflection.AssemblyProduct("TaskbarNetSpeed")]
>>%GEN% echo [assembly: System.Reflection.AssemblyVersion("%VERSION%.0")]
>>%GEN% echo [assembly: System.Reflection.AssemblyFileVersion("%VERSION%.0")]
>>%GEN% echo [assembly: System.Reflection.AssemblyInformationalVersion("%VERSION%")]

set SRC=TaskbarNetSpeed.cs %GEN%
"%CSC%" %OPTS% /platform:x64 /out:bin\TaskbarNetSpeed-x64.exe %SRC% || exit /b 1
"%CSC%" %OPTS% /platform:x86 /out:bin\TaskbarNetSpeed-x86.exe %SRC% || exit /b 1
rem This compiler can't target ARM64 directly. An AnyCPU build runs natively on
rem ARM64 Windows 11, which ships .NET Framework 4.8.1 with ARM64 support.
"%CSC%" %OPTS% /platform:anycpu /out:bin\TaskbarNetSpeed-arm64.exe %SRC% || exit /b 1

echo Built v%VERSION%: bin\TaskbarNetSpeed-x64.exe, bin\TaskbarNetSpeed-x86.exe, bin\TaskbarNetSpeed-arm64.exe
