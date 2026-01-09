@echo off
chcp 65001 >nul
setlocal

echo ========================================
echo   AudioBridge 编译并启动
echo ========================================
echo.

:: 设置配置，默认为 Debug
set CONFIG=%1
if "%CONFIG%"=="" set CONFIG=Debug

:: 编译
call build.bat %CONFIG%
if errorlevel 1 (
    echo [错误] 编译失败！
    pause
    exit /b 1
)

:: 启动
call run.bat %CONFIG%

endlocal
