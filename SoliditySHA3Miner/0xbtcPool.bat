@echo off
pushd %~dp0

for %%X in (dotnet.exe) do (set FOUND=%%~$PATH:X)
if defined FOUND (goto dotNetFound) else (goto dotNetNotFound)

:dotNetNotFound
echo .NET Core is not found or not installed,
echo download and install from https://www.microsoft.com/net/download/windows/run
goto end

:dotNetFound
:startMiner
DEL SoliditySHA3Miner.conf
dotnet SoliditySHA3Miner.dll abiFile=ERC-541.abi contract=0xBC2AFc039d2BFa67d582aC181daB5BE17EC91f82 overrideMaxTarget=27606985387162255149739023449108101809804435888681546220650096895197184 pool=http://mike.rs:8080 address=0x1056270360EF979DbC3Cf5FE940F88640D08861D
if %errorlevel% EQU 22 (
  goto startMiner
)
pause
