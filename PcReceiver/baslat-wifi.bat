@echo off
dotnet run --project "%~dp0PcReceiver.csproj" -c Release
if errorlevel 1 pause
