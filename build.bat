@echo off
rem PixelForge: builds build\PixelForge.exe (keep the data_PixelForge_windows_x86_64 folder next to it).
cd /d "%~dp0"
if not exist build mkdir build
"C:\Tools\Godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe" --headless --path "%~dp0." --export-release "Windows Desktop" build\PixelForge.exe || pause
