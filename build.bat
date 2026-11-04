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

rem Inno Setup peut etre installe pour tous les utilisateurs ou seulement l'utilisateur courant.
set "ISCC="
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%LocalAppData%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LocalAppData%\Programs\Inno Setup 6\ISCC.exe"

if not defined ISCC (
  echo Inno Setup 6 non trouve : seul publish\DeskNest.exe a ete cree.
  exit /b 0
)

echo.
echo Creation de l'installateur (%APPVER%)...
"%ISCC%" /DAppVersion=%APPVER% installer.iss
if errorlevel 1 exit /b 1

echo.
echo Termine : publish\DeskNest.exe et installer\DeskNest-Setup.exe
exit /b 0
