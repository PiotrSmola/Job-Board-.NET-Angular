# Buduje i uruchamia aplikację w Dockerze.
# Jeśli preferowany port jest zajęty, wybierany jest kolejny wolny (nic nie jest zwalniane).
param(
    [int]$FrontendPort = 4200,
    [int]$BackendPort = 5285,
    [int]$DbPort = 3306
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

function Test-PortFree([int]$Port) {
    if (Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue) {
        return $false
    }
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Any, $Port)
    try {
        $listener.Start()
        return $true
    } catch {
        return $false
    } finally {
        $listener.Stop()
    }
}

$taken = @()
function Get-FreePort([int]$Preferred) {
    $port = $Preferred
    while ($taken -contains $port -or -not (Test-PortFree $port)) {
        $port++
    }
    $script:taken += $port
    return $port
}

# hasło do bazy zachowujemy z poprzedniego .env
$dbPassword = 'jobboard'
if (Test-Path .env) {
    $line = Get-Content .env | Where-Object { $_ -match '^DB_PASSWORD=' } | Select-Object -First 1
    if ($line) { $dbPassword = $line.Substring('DB_PASSWORD='.Length) }
}

# zatrzymanie wcześniej uruchomionych kontenerów tego projektu, żeby nie blokowały własnych portów
docker compose down
if ($LASTEXITCODE -ne 0) { throw 'docker compose down nie powiodło się' }

$ports = [ordered]@{
    FRONTEND_PORT = Get-FreePort $FrontendPort
    BACKEND_PORT  = Get-FreePort $BackendPort
    DB_PORT       = Get-FreePort $DbPort
}

$envLines = @($ports.Keys | ForEach-Object { "$_=$($ports[$_])" }) + "DB_PASSWORD=$dbPassword"
[System.IO.File]::WriteAllLines((Join-Path $PSScriptRoot '.env'), $envLines, [System.Text.UTF8Encoding]::new($false))

docker compose up -d --build
if ($LASTEXITCODE -ne 0) { throw 'docker compose up nie powiodło się' }

Write-Host ''
Write-Host "Frontend: http://localhost:$($ports.FRONTEND_PORT)"
Write-Host "Swagger:  http://localhost:$($ports.BACKEND_PORT)/swagger"
Write-Host "MySQL:    localhost:$($ports.DB_PORT) (root / $dbPassword)"
