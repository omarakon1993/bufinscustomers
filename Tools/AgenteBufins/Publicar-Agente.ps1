<#
  Publica el agente de carpeta como un ejecutable unico y autocontenido (no requiere instalar .NET en el servidor del cliente).
  Uso (desde la raiz del repositorio):   powershell -File Tools\AgenteBufins\Publicar-Agente.ps1
  Resultado: Tools\AgenteBufins\publicado\  ->  AgenteBufins.exe + agente.ejemplo.json
  Ademas copia AgenteBufins.exe a App_Data\Agente\ (fuera de git): el sitio lo ofrece en Integraciones > Asistente de conexion
  y el paquete de publicacion del sitio lo incluye (<Content> del .csproj).

  Firma digital (recomendada: evita el aviso "editor desconocido" de Windows SmartScreen). Opcional:
    -Certificado C:\ruta\certificado.pfx   (la contrasena se pide por consola; nunca se pasa por parametro ni se guarda)
#>
param([string]$Certificado = '')
$ErrorActionPreference = 'Stop'
$proyecto = Join-Path $PSScriptRoot 'AgenteBufins.csproj'
$salida   = Join-Path $PSScriptRoot 'publicado'
if (Test-Path $salida) { Remove-Item $salida -Recurse -Force }
dotnet publish $proyecto -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $salida
if ($LASTEXITCODE -ne 0) { throw 'La publicacion fallo.' }

$exe = Join-Path $salida 'AgenteBufins.exe'
if ($Certificado) {
    if (-not (Test-Path $Certificado)) { throw "No existe el certificado: $Certificado" }
    $clave = Read-Host 'Contrasena del certificado' -AsSecureString
    $cert  = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($Certificado, $clave)
    $r = Set-AuthenticodeSignature -FilePath $exe -Certificate $cert -TimestampServer 'http://timestamp.digicert.com' -HashAlgorithm SHA256
    if ($r.Status -ne 'Valid') { throw "La firma fallo: $($r.Status) $($r.StatusMessage)" }
    Write-Host 'Ejecutable firmado.'
} else {
    Write-Host 'AVISO: sin firma digital (use -Certificado). Windows podria mostrar "editor desconocido" al ejecutarlo.'
}

# Copia descargable del sitio (App_Data\Agente\, ignorada por git)
$raiz   = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$destino = Join-Path $raiz 'App_Data\Agente'
New-Item -ItemType Directory -Force $destino | Out-Null
Copy-Item $exe (Join-Path $destino 'AgenteBufins.exe') -Force
$hash = (Get-FileHash (Join-Path $destino 'AgenteBufins.exe') -Algorithm SHA256).Hash
Write-Host "Copiado a $destino"
Write-Host "SHA-256: $hash"

Get-ChildItem $salida | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}}
