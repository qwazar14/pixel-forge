@echo off
rem PixelForge: builds the C# code, then starts the editor.
cd /d "%~dp0"
dotnet build PixelForge.csproj -nologo -v q || pause
start "" "C:\Tools\Godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path "%~dp0."
