@echo off
chcp 65001 >nul
setlocal

echo ========================================
echo   AudioBridge Windows 编译脚本
echo ========================================
echo.

:: 设置配置，默认为 Debug
set CONFIG=%1
if "%CONFIG%"=="" set CONFIG=Debug

echo [配置] %CONFIG%
echo.

:: 结束已运行的进程（避免文件锁定）
tasklist /fi "imagename eq AudioBridge.Agent.Tray.exe" 2>nul | find /i "AudioBridge.Agent.Tray.exe" >nul
if not errorlevel 1 (
    echo [清理] 正在结束已运行的 AudioBridge 进程...
    taskkill /f /im AudioBridge.Agent.Tray.exe >nul 2>&1
    timeout /t 1 /nobreak >nul
)

:: 还原 NuGet 包
echo [1/2] 正在还原 NuGet 包...
dotnet restore AudioBridge.sln
if errorlevel 1 (
    echo [错误] NuGet 包还原失败！
    pause
    exit /b 1
)
echo [完成] NuGet 包还原成功
echo.

:: 编译解决方案
echo [2/2] 正在编译解决方案...
dotnet build AudioBridge.sln -c %CONFIG% --no-restore
if errorlevel 1 (
    echo [错误] 编译失败！
    pause
    exit /b 1
)

echo.
echo ========================================
echo   编译成功！
echo ========================================
echo.
echo 输出目录: AudioBridge.Agent.Tray\bin\%CONFIG%\
echo.

endlocal
