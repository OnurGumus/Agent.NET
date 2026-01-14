@echo off
echo ========================================
echo  Agent.NET NuGet Package Publisher
echo ========================================
echo.

:: Create .build directory if it doesn't exist
if not exist .build mkdir .build

:: Clean previous builds
echo Cleaning previous builds...
dotnet clean -c Release --verbosity quiet src\AgentNet\AgentNet.fsproj
dotnet clean -c Release --verbosity quiet src\AgentNet.Durable\AgentNet.Durable.fsproj

:: Build and pack AgentNet
echo.
echo Building and packing AgentNet...
dotnet pack -c Release src\AgentNet\AgentNet.fsproj --output .build

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ERROR: AgentNet build failed!
    exit /b %ERRORLEVEL%
)

:: Build and pack AgentNet.Durable
echo.
echo Building and packing AgentNet.Durable...
dotnet pack -c Release src\AgentNet.Durable\AgentNet.Durable.fsproj --output .build

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ERROR: AgentNet.Durable build failed!
    exit /b %ERRORLEVEL%
)

:: Show the output
echo.
echo ========================================
echo  Packages created successfully!
echo ========================================
echo.
echo Packages:
dir /b .build\*.nupkg
echo.
echo Location: %CD%\.build\
echo.
echo To publish to NuGet.org:
echo   dotnet nuget push .build\AgentNet.*.nupkg --api-key YOUR_API_KEY --source https://api.nuget.org/v3/index.json
echo   dotnet nuget push .build\AgentNet.Durable.*.nupkg --api-key YOUR_API_KEY --source https://api.nuget.org/v3/index.json
echo.
