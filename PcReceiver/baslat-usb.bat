@echo off
adb reverse tcp:28555 tcp:28555 || echo adb bulunamadi - Android platform-tools kurup PATH'e ekleyin
dotnet run --project "%~dp0PcReceiver.csproj" -c Release
if errorlevel 1 pause
