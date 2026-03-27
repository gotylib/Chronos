param(
    [int]$Agents = 1,
    [int]$MasterPort = 5000,
    [int]$AgentBasePort = 5001,
    [string]$DockerComposeExecutable = "docker-compose",
    [string]$DockerExecutable = "docker",
    [string]$MasterApiKey = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location (Resolve-Path (Join-Path $root ".."))

Write-Host "Starting Chronos.Master on port $MasterPort ..."
$masterArgs = @(
    "run",
    "--project", "src/Chronos.Master/Chronos.Master.csproj",
    "--",
    "--urls", "http://0.0.0.0:$MasterPort"
)

$masterProcess = Start-Process -FilePath "dotnet" -ArgumentList $masterArgs -NoNewWindow -PassThru

Start-Sleep -Seconds 2

for ($i = 0; $i -lt $Agents; $i++) {
    $agentPort = $AgentBasePort + $i
    $agentBaseUrl = "http://localhost:$agentPort"
    $masterUrl = "http://localhost:$MasterPort"

    Write-Host "Starting Chronos.Agent on port $agentPort ..."

    $cmd = "set CHRONOS_MASTER_URL=$masterUrl&&" +
           " set CHRONOS_AGENT_BASE_URL=$agentBaseUrl&&" +
           " set CHRONOS_AGENT_APP_PATH=/app&&" +
           " set CHRONOS_AGENT_COMPOSE_FILE=docker-compose.yml&&" +
           " set CHRONOS_AGENT_DOCKER_COMPOSE_EXECUTABLE=$DockerComposeExecutable&&" +
           " set CHRONOS_AGENT_DOCKER_EXECUTABLE=$DockerExecutable"

    if (![string]::IsNullOrWhiteSpace($MasterApiKey)) {
        $cmd += "&& set CHRONOS_MASTER_API_KEY=$MasterApiKey"
    }

    $cmd += "&& dotnet run --project src/Chronos.Agent/Chronos.Agent.csproj -- --urls http://0.0.0.0:$agentPort"

    Start-Process -FilePath "cmd.exe" -ArgumentList "/c", $cmd -NoNewWindow | Out-Null
}

Write-Host ""
Write-Host "Cluster started."
Write-Host "Master UI: http://localhost:$MasterPort/ui"
Write-Host "Master agents list: GET http://localhost:$MasterPort/agents"

Write-Host ""
Write-Host "Press Ctrl+C to stop processes."
$null = $masterProcess.WaitForExit()

