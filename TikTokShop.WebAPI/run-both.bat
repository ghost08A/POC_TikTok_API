@echo off
echo Building project first...
dotnet build
if %ERRORLEVEL% neq 0 (
    echo Build failed! Aborting...
    pause
    exit /b
)

echo.
echo Build successful! Starting Sandbox and RealShop Environments...

:: รัน Sandbox ในหน้าต่างใหม่ (ใส่ --no-build)
start "TikTok API - Sandbox (5101)" cmd /k "cd /d %~dp0 && dotnet run --no-build --launch-profile LYT-Sandbox"

:: รัน RealShop ในหน้าต่างใหม่ (ใส่ --no-build)
start "TikTok API - RealShop (5201)" cmd /k "cd /d %~dp0 && dotnet run --no-build --launch-profile LYT-RealShop"

echo Both environments have been started in new windows!
pause
