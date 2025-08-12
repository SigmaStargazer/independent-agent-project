@echo off
echo Compiling Protocol Buffers files...
"protoc-3.2.0-win32/bin/protogen" --proto_path=../Src/Lib/proto --csharp_out=../Src/Lib/Protocol message.proto
if %errorlevel% neq 0 goto error

echo Copying generated files...
xcopy "../Src/Lib/Protocol/bin/Debug/*" "../Src/Client/Assets/References" /E /Y /I
if %errorlevel% neq 0 goto error

echo Done.
goto end

:error
echo An error occurred.

:end