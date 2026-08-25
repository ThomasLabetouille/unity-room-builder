@echo off
REM Teste les tests : injecte des defauts dans le code de geometrie et verifie
REM que la batterie de proprietes les rattrape.
REM
REM ATTENTION : les fichiers source sont modifies le temps de chaque essai puis
REM restaures. Ne pas lancer sur un arbre de travail non commite.
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo.
  echo   dotnet introuvable. Installe le SDK .NET, ou ouvre MutationTests.csproj dans Rider.
  echo.
  exit /b 2
)

dotnet run -c Release --project MutationTests.csproj -- %*
exit /b %errorlevel%
