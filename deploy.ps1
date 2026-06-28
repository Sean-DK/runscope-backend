$ErrorActionPreference = "Stop"

$SERVER_USER = "seang"
$SERVER_HOST = "192.168.1.101"
$SERVER_DIR = "/opt/runscope"
$FRONTEND_DIR = "..\runscope-frontend"
$STATIC_DIR = "/var/www/runscope"

Write-Host "RunScope Deploy" -ForegroundColor Cyan

# Version: days since 1996-08-22 + time of day (24h, seconds precision)
$epoch = [datetime]"1996-08-22"
$now = Get-Date
$daysSinceEpoch = [math]::Floor(($now - $epoch).TotalDays)
$timeOfDay = $now.ToString("HHmmss")
$appVersion = "$daysSinceEpoch.$timeOfDay"

Write-Host ">> Version: $appVersion" -ForegroundColor Cyan

Write-Host "Building React app..." -ForegroundColor Yellow
Push-Location $FRONTEND_DIR
$mapboxToken = (Get-Content .env | Select-String "VITE_MAPBOX_TOKEN").ToString().Replace("VITE_MAPBOX_TOKEN=", "")
$envContent = "VITE_MAPBOX_TOKEN=$mapboxToken`nVITE_API_BASE_URL=https://runscope.stablesea.net`nVITE_SIGNALR_HUB_URL=https://runscope.stablesea.net`nVITE_APP_VERSION=$appVersion"
Set-Content .env.production $envContent
npm run build
Pop-Location

Write-Host "Uploading React build..." -ForegroundColor Yellow
ssh "${SERVER_USER}@${SERVER_HOST}" "rm -rf ${STATIC_DIR}/* && mkdir -p ${STATIC_DIR}"
scp -r "${FRONTEND_DIR}\dist" "${SERVER_USER}@${SERVER_HOST}:/tmp/runscope-dist"
ssh "${SERVER_USER}@${SERVER_HOST}" "cp -r /tmp/runscope-dist/. ${STATIC_DIR}/ && rm -rf /tmp/runscope-dist"

Write-Host "Building API Docker image..." -ForegroundColor Yellow
docker build -f RunScope.Api\Dockerfile -t runscope-api:latest .

Write-Host "Transferring Docker image to server..." -ForegroundColor Yellow
docker save runscope-api:latest -o runscope-api.tar
scp runscope-api.tar "${SERVER_USER}@${SERVER_HOST}:/tmp/runscope-api.tar"
ssh "${SERVER_USER}@${SERVER_HOST}" "docker load -i /tmp/runscope-api.tar && rm /tmp/runscope-api.tar"
Remove-Item runscope-api.tar

Write-Host "Uploading docker-compose.prod.yml..." -ForegroundColor Yellow
ssh "${SERVER_USER}@${SERVER_HOST}" "mkdir -p $SERVER_DIR"
scp docker-compose.prod.yml "${SERVER_USER}@${SERVER_HOST}:${SERVER_DIR}/"

Write-Host "Restarting API container..." -ForegroundColor Yellow
ssh "${SERVER_USER}@${SERVER_HOST}" "cd $SERVER_DIR && docker compose -f docker-compose.prod.yml --env-file .env.prod up -d --force-recreate api"

Write-Host "Waiting for API to start..." -ForegroundColor Yellow
Start-Sleep -Seconds 5

Write-Host "Running database migrations..." -ForegroundColor Yellow
try {
    ssh "${SERVER_USER}@${SERVER_HOST}" "cd $SERVER_DIR && docker compose -f docker-compose.prod.yml --env-file .env.prod exec api dotnet RunScope.Api.dll migrate"
} catch {
    Write-Host "Migration step skipped." -ForegroundColor Yellow
}

Write-Host "Deploy complete!" -ForegroundColor Green
