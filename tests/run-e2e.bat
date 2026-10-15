@echo off
title Gemini Desktop - End-to-End (E2E) Test Suite
cd /d "%~dp0\.."
echo ==============================================================================
echo Running Gemini Desktop Automated End-to-End (E2E) Test Suite...
echo ==============================================================================
powershell -NoProfile -ExecutionPolicy Bypass -File "tests\e2e-test-suite.ps1"
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] End-to-End tests failed with exit code %ERRORLEVEL%.
    pause
    exit /b %ERRORLEVEL%
)
echo.
echo [SUCCESS] All End-to-End tests completed successfully.
pause
