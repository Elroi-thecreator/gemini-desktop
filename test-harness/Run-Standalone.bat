@echo off
title Gemini Desktop - Extension Standalone Test Runner
cd /d "%~dp0"
echo Starting Gemini Desktop Standalone Test Window...
powershell -NoProfile -ExecutionPolicy Bypass -File "Run-Standalone.ps1"
pause
