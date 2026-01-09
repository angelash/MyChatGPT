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

:: 检查是否已经在运行
tasklist /fi "imagename eq AudioBridge.Agent.Tray.exe" 2>nul | find /i "AudioBridge.Agent.Tray.exe" >nul
if not errorlevel 1 (
    echo [提示] AudioBridge 已在运行中！
    echo        请查看任务栏右下角的托盘图标。
    echo.
    pause
    exit /b 0
)

echo [启动] %EXE_PATH%
echo.

:: 启动程序
start "" "%EXE_PATH%"

echo ========================================
echo   AudioBridge 已启动！
echo ========================================
echo.
echo 这是一个托盘应用，没有主窗口。
echo 请查看任务栏右下角的托盘图标。
echo.
echo 右键点击图标可以:
echo   ^> Start/Stop  启动/停止服务
echo   ^> Show Status 查看状态
echo   ^> Show Devices 查看音频设备
echo   ^> Open Log File 打开日志文件
echo   ^> Exit 退出程序
echo.

endlocal
