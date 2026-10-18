param([ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet run --project Tests/TimeSync.Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw '校时逻辑验证失败。' }
    dotnet publish App/TimeSyncAssistant.csproj -c Release -r $Runtime --self-contained true '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' '-p:DebugType=None' -o "dist/$Runtime"
    if ($LASTEXITCODE -ne 0) { throw '构建失败。' }
    Copy-Item README.md "dist/$Runtime/使用说明.md"
    Compress-Archive -Path "dist/$Runtime/*" -DestinationPath "dist/TimeSyncAssistant-$Runtime.zip" -Force
    Write-Host "完成：dist/TimeSyncAssistant-$Runtime.zip"
} finally { Pop-Location }
