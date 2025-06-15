@echo off
set SRC=C:\Users\Admin\Desktop\Dev\x64DbgMCPServer\bin\x64\Debug
set DST=C:\Users\Admin\Downloads\snapshot_2025-03-15_15-57\release\x64\plugins\x64DbgMCPServer

echo Copying from: %SRC%
echo To: %DST%
dir "%SRC%"
if not exist "%DST%" mkdir "%DST%"
xcopy /Y /I /E "%SRC%\*" "%DST%\"
echo Done.