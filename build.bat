@echo off
setlocal
rem Compile en un seul .exe autonome (aucun .NET a installer sur le PC cible), puis cree l'installateur si Inno Setup est present.
cd /d "%~dp0"

rem Version lue dans le csproj : une seule source de verite pour l'executable et l'installateur.
set "APPVER="
for /f "usebackq delims=" %%v in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "$x=[xml](Get-Content -LiteralPath 'DeskNest.csproj'); $x.Project.PropertyGroup.Version.Trim()"`) do set "APPVER=%%v"
if not defined APPVER (
  echo Impossible de lire la version dans DeskNest.csproj.
  exit /b 1
)

echo Version : %APPVER%
set "SETUPNAME=DeskNest-Setup-%APPVER%.exe"
set "PORTABLENAME=DeskNest-%APPVER%-Portable.zip"
echo.
echo Nettoyage de publish...
if exist publish rmdir /s /q publish

echo Compilation...
dotnet publish DeskNest.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:DebugType=None ^
  -p:DebugSymbols=false ^
  -p:PublishReadyToRun=true ^
  -o publish
if errorlevel 1 exit /b 1

if not exist "publish\DeskNest.exe" (
  echo Echec : publish\DeskNest.exe n'a pas ete cree.
  exit /b 1
)

rem Signature Authenticode facultative : DESKNEST_PFX pointe vers le certificat .pfx,
rem DESKNEST_PFX_PASSWORD contient son mot de passe. Sans certificat, le build reste non signe.
set "SIGNTOOL="
if not defined DESKNEST_PFX goto :no_sign
if not exist "%DESKNEST_PFX%" (
  echo ATTENTION : DESKNEST_PFX introuvable, build non signe.
  goto :no_sign
)
for /f "delims=" %%s in ('where signtool 2^>nul') do if not defined SIGNTOOL set "SIGNTOOL=%%s"
if not defined SIGNTOOL (
  for /f "usebackq delims=" %%s in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -Path '%ProgramFiles(x86)%\Windows Kits\10\bin' -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName"`) do set "SIGNTOOL=%%s"
)
if not defined SIGNTOOL (
  echo ATTENTION : signtool.exe introuvable, build non signe.
  goto :no_sign
)

echo.
echo Signature de publish\DeskNest.exe...
call :sign "publish\DeskNest.exe"
if errorlevel 1 exit /b 1
goto :signed

:no_sign
echo.
echo Signature : ignoree - build non signe.
:signed

rem Archive portable : l'executable autonome et la licence, prets a etre dezippes.
echo.
echo Creation de l'archive portable (%PORTABLENAME%)...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -LiteralPath 'publish\DeskNest.exe','LICENSE' -DestinationPath 'publish\%PORTABLENAME%' -Force"
if errorlevel 1 exit /b 1
if not exist "publish\%PORTABLENAME%" (
  echo Echec : publish\%PORTABLENAME% n'a pas ete cree.
  exit /b 1
)

rem Inno Setup peut etre installe pour tous les utilisateurs ou seulement l'utilisateur courant.
set "ISCC="
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%LocalAppData%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LocalAppData%\Programs\Inno Setup 6\ISCC.exe"

if not defined ISCC (
  echo Inno Setup 6 non trouve : publish\DeskNest.exe et publish\%PORTABLENAME% ont ete crees.
  exit /b 0
)

echo.
echo Creation de l'installateur (%APPVER%)...
"%ISCC%" /DAppVersion=%APPVER% installer.iss
if errorlevel 1 exit /b 1

if defined SIGNTOOL (
  echo.
  echo Signature de installer\%SETUPNAME%...
  call :sign "installer\%SETUPNAME%"
  if errorlevel 1 exit /b 1
)

echo.
echo Termine : publish\DeskNest.exe, publish\%PORTABLENAME% et installer\%SETUPNAME%
exit /b 0

:sign
if defined DESKNEST_PFX_PASSWORD (
  "%SIGNTOOL%" sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /f "%DESKNEST_PFX%" /p "%DESKNEST_PFX_PASSWORD%" "%~1"
) else (
  "%SIGNTOOL%" sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /f "%DESKNEST_PFX%" "%~1"
)
if errorlevel 1 (
  echo Echec de la signature de %~1
  exit /b 1
)
exit /b 0
