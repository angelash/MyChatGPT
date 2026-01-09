@echo off
chcp 65001 >nul
setlocal

echo ========================================
echo   AudioBridge Windows 清理脚本
echo ========================================
echo.

echo [清理] 正在清理解决方案...
dotnet clean AudioBridge.sln
if errorlevel 1 (
    echo [警告] dotnet clean 执行出错
)

echo.
echo [清理] 正在删除 bin 和 obj 目录...

:: 删除所有 bin 目录
for /d /r %%i in (bin) do (
    if exist "%%i" (
        echo   删除: %%i
        rd /s /q "%%i"
    )
)

:: 删除所有 obj 目录
for /d /r %%i in (obj) do (
    if exist "%%i" (
        echo   删除: %%i
        rd /s /q "%%i"
    )
)

echo.
echo ========================================
echo   清理完成！
echo ========================================
echo.

endlocal
