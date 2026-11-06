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

function Get-EnvValue([string]$Name, [string]$Default) {
    if (Test-Path .env) {
        $line = Get-Content .env | Where-Object { $_ -match "^$Name=" } | Select-Object -First 1
        if ($line) {
            # pusta wartość (np. "DB_PASSWORD=") oznacza wartość domyślną
            $value = $line.Substring($Name.Length + 1).Trim()
            if ($value) { return $value }
        }
    }
    return $Default
}

# losowy ciąg alfanumeryczny z kryptograficznie bezpiecznego generatora
function New-RandomSecret([int]$Length) {
    $chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789'
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $buffer = New-Object byte[] 1
    $sb = New-Object System.Text.StringBuilder
    try {
        while ($sb.Length -lt $Length) {
            $rng.GetBytes($buffer)
            # bajty >= 248 (4 * 62) są odrzucane, żeby każdy znak był równie prawdopodobny
            if ($buffer[0] -lt 248) { [void]$sb.Append($chars[$buffer[0] % $chars.Length]) }
        }
    } finally {
        $rng.Dispose()
    }
    return $sb.ToString()
}

# hasło do bazy, sekret JWT, klucze S3, ALLOW_SEED_REFRESH i SEED_DEMO_ACCOUNTS zachowujemy z poprzedniego .env
$dbPassword = Get-EnvValue 'DB_PASSWORD' 'jobboard'
$jwtSecret = Get-EnvValue 'JWT_SECRET' ''
if (-not $jwtSecret) {
    $jwtSecret = New-RandomSecret 64
    Write-Host 'Wygenerowano nowy JWT_SECRET (zapisany w .env).'
}
# klucze magazynu plików CV (SeaweedFS) - zmiana kluczy przy istniejącym wolumenie storage-data jest bezpieczna,
# bo konfiguracja S3 jest generowana przy każdym starcie kontenera
$s3AccessKey = Get-EnvValue 'S3_ACCESS_KEY' ''
$s3SecretKey = Get-EnvValue 'S3_SECRET_KEY' ''
if (-not $s3AccessKey -or -not $s3SecretKey) {
    $s3AccessKey = New-RandomSecret 20
    $s3SecretKey = New-RandomSecret 40
    Write-Host 'Wygenerowano nowe klucze S3_ACCESS_KEY i S3_SECRET_KEY (zapisane w .env).'
}
$allowSeedRefresh = Get-EnvValue 'ALLOW_SEED_REFRESH' ''
$seedDemoAccounts = Get-EnvValue 'SEED_DEMO_ACCOUNTS' ''

# docker compose wymaga JWT_SECRET i kluczy S3 już przy "down" (interpolacja całego pliku), a .env może ich jeszcze nie mieć
$env:JWT_SECRET = $jwtSecret
$env:S3_ACCESS_KEY = $s3AccessKey
$env:S3_SECRET_KEY = $s3SecretKey
try {
    # zatrzymanie wcześniej uruchomionych kontenerów tego projektu, żeby nie blokowały własnych portów
    docker compose down
    if ($LASTEXITCODE -ne 0) { throw 'docker compose down nie powiodło się' }

    $ports = [ordered]@{
        FRONTEND_PORT = Get-FreePort $FrontendPort
        BACKEND_PORT  = Get-FreePort $BackendPort
        DB_PORT       = Get-FreePort $DbPort
    }

    $envLines = @($ports.Keys | ForEach-Object { "$_=$($ports[$_])" }) + "DB_PASSWORD=$dbPassword" + "JWT_SECRET=$jwtSecret" +
        "S3_ACCESS_KEY=$s3AccessKey" + "S3_SECRET_KEY=$s3SecretKey"
    if ($allowSeedRefresh) { $envLines += "ALLOW_SEED_REFRESH=$allowSeedRefresh" }
    if ($seedDemoAccounts) { $envLines += "SEED_DEMO_ACCOUNTS=$seedDemoAccounts" }
    [System.IO.File]::WriteAllLines((Join-Path $PSScriptRoot '.env'), $envLines, [System.Text.UTF8Encoding]::new($false))

    docker compose up -d --build
    if ($LASTEXITCODE -ne 0) { throw 'docker compose up nie powiodło się' }
} finally {
    Remove-Item Env:JWT_SECRET, Env:S3_ACCESS_KEY, Env:S3_SECRET_KEY -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host "Frontend: http://localhost:$($ports.FRONTEND_PORT)"
Write-Host "Swagger:  http://localhost:$($ports.BACKEND_PORT)/swagger"
Write-Host "MySQL:    localhost:$($ports.DB_PORT) (root / $dbPassword)"
