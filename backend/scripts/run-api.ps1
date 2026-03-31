# Stops a previous Api instance (locks bin\ DLLs on Windows) then builds and runs.
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root "src\TravelDisruptionAgent.Api\TravelDisruptionAgent.Api.csproj"

Get-Process -Name "TravelDisruptionAgent.Api" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

Set-Location $root
dotnet run --project $project @args
