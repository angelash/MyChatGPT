@echo off
chcp 65001 >nul
setlocal

echo ========================================
echo   AudioBridge Windows 启动脚本
echo ========================================
echo.

:: 设置配置，默认为 Debug
set CONFIG=%1
if "%CONFIG%"=="" set CONFIG=Debug

:: 检查是否已编译
set EXE_PATH=AudioBridge.Agent.Tray\bin\%CONFIG%\net8.0-windows\AudioBridge.Agent.Tray.exe
if not exist "%EXE_PATH%" (
    echo [警告] 未找到可执行文件，正在编译...
    call build.bat %CONFIG%
    if errorlevel 1 (
        echo [错误] 编译失败，无法启动！
        pause
        exit /b 1
    )
)

echo [启动] %EXE_PATH%
echo.

:: 启动程序
start "" "%EXE_PATH%"

echo [完成] 程序已启动
echo.

endlocal
