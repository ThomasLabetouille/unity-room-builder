@echo off
REM Lance les tests de proprietes de la triangulation, hors Unity.
REM   run.bat                       5000 tirages
REM   run.bat --iterations 50000    passe longue
REM   run.bat --seed 12345          rejoue une execution a l'identique
REM   run.bat --deep                verifie aussi les T-jonctions sur tous les tirages
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo.
  echo   dotnet introuvable.
  echo   Installe le SDK .NET ^(https://dotnet.microsoft.com/download^),
  echo   ou ouvre GeometryTests.csproj directement dans Rider / Visual Studio.
  echo.
  exit /b 2
)

dotnet run -c Release --project GeometryTests.csproj -- %*
exit /b %errorlevel%
