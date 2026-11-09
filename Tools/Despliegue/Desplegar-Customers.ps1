<#
.SYNOPSIS
  Respalda, publica y restaura el sitio customers.bufins.com con Web Deploy (msdeploy), el mismo mecanismo que usa Visual Studio.

.DESCRIPTION
  Cuatro acciones:
    Respaldar      Descarga una copia COMPLETA del sitio actual a una carpeta local (solo lee; no cambia nada en el servidor).
    SubirSecretos  Sube secretsSettings.json (cadena de conexion y claves MachineKey) al sitio; solo agrega/actualiza ese archivo.
    Publicar       Sube el paquete .NET 10 al sitio. Sin -Aplicar solo SIMULA y muestra que crearia, cambiaria y borraria.
    Restaurar      Devuelve el sitio al estado de un respaldo (vuelta atras). Sin -Aplicar solo simula.

  La contrasena de Web Deploy se pide por pantalla (no se guarda ni se escribe en ningun archivo).
  Al publicar NO toca App_Data\dpkeys, App_Data\logs, secretsSettings.json ni los secrets*.config del sitio antiguo
  (estos ultimos se conservan para poder volver atras).

  Orden recomendado del corte:
    1. dotnet publish (ver abajo)            -> paquete en C:\Users\omara_\bufins-despliegue\customers\paquete-net10
    2. -Accion Respaldar                      -> copia completa del sitio antiguo
    3. -Accion SubirSecretos -Aplicar         -> deja secretsSettings.json en el servidor ANTES de publicar
    4. -Accion Publicar                       -> simulacion; revisar lo que borraria
    5. -Accion Publicar -Aplicar              -> publica de verdad
    6. Pruebas de humo (login con 2FA, 3 informes). Si algo falla: -Accion Restaurar -Respaldo <carpeta> -Aplicar

.EXAMPLE
  dotnet publish bufinscustomers.csproj -c Release -r win-x64 --self-contained true -o C:\Users\omara_\bufins-despliegue\customers\paquete-net10
  .\Desplegar-Customers.ps1 -Accion Respaldar
  .\Desplegar-Customers.ps1 -Accion SubirSecretos -Aplicar
  .\Desplegar-Customers.ps1 -Accion Publicar            # simulacion
  .\Desplegar-Customers.ps1 -Accion Publicar -Aplicar   # publica de verdad
  .\Desplegar-Customers.ps1 -Accion Restaurar -Respaldo C:\Users\omara_\bufins-despliegue\customers\respaldo-20261108-1530 -Aplicar
#>
param(
    [Parameter(Mandatory = $true)][ValidateSet('Respaldar', 'Publicar', 'SubirSecretos', 'Restaurar', 'BajarLogs', 'Reiniciar')][string]$Accion,
    [string]$Paquete = 'C:\Users\omara_\bufins-despliegue\customers\paquete-net10',
    [string]$Respaldo,
    [string]$Secretos,
    [string]$Servicio = 'https://customers.bufins.com:8172/msdeploy.axd?site=customers.bufins.com',
    [string]$Sitio = 'customers.bufins.com',
    [string]$Usuario = 'oglearni',
    [switch]$Aplicar,
    [switch]$ConservarArchivosExtra
)

$ErrorActionPreference = 'Stop'
if (-not $Secretos) {
    $carpetaScript = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
    $Secretos = Join-Path (Split-Path (Split-Path $carpetaScript -Parent) -Parent) 'secretsSettings.json'
}
$carpetaBase = 'C:\Users\omara_\bufins-despliegue\customers'

$msdeploy = @('C:\Program Files\IIS\Microsoft Web Deploy V3\msdeploy.exe', 'C:\Program Files (x86)\IIS\Microsoft Web Deploy V3\msdeploy.exe') |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $msdeploy) { throw 'No se encontro msdeploy.exe (Web Deploy 3.6). Instale Microsoft Web Deploy.' }

# --- contrasena (solo en memoria) ---
$seg = Read-Host -Prompt "Contrasena de Web Deploy del usuario $Usuario" -AsSecureString
$ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($seg)
try { $clave = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
if ([string]::IsNullOrEmpty($clave)) { throw 'La contrasena no puede estar vacia.' }
if ($clave.Contains('"')) { throw 'La contrasena contiene comillas dobles; cambiela o use otra via.' }

# Conexion al servidor (se usa como origen o destino segun la accion)
$conexion = "computerName=`"$Servicio`",userName=$Usuario,password=`"$clave`",authType=Basic"

# Rutas y archivos del servidor que NUNCA se tocan al publicar
$omitir = @(
    '-skip:objectName=dirPath,absolutePath=".*\\App_Data\\dpkeys$"',               # llaves de antiforgery/TempData: si se borran, caen los formularios abiertos
    '-skip:objectName=dirPath,absolutePath=".*\\App_Data\\logs$"',
    '-skip:objectName=filePath,absolutePath=".*\\secretsSettings\.json$"',
    '-skip:objectName=filePath,absolutePath=".*\\secrets\.config$"',                # secretos del sitio antiguo: se conservan por si hay que volver atras
    '-skip:objectName=filePath,absolutePath=".*\\secrets\.machineKey\.config$"',
    '-skip:objectName=filePath,absolutePath=".*\\\.user\.ini$"'                     # archivo de Plesk: no se toca
)

$sello = Get-Date -Format 'yyyyMMdd-HHmm'
$registro = Join-Path $carpetaBase ("registro-$Accion-$sello.txt")
New-Item -ItemType Directory -Force $carpetaBase | Out-Null

function Ejecutar([string[]]$argumentos, [string]$descripcion) {
    Write-Host ""
    Write-Host "== $descripcion" -ForegroundColor Cyan
    # el registro no lleva la contrasena
    ($argumentos -join ' ') -replace 'password="[^"]*"', 'password="***"' | Out-File $registro -Append -Encoding utf8
    & $msdeploy @argumentos 2>&1 | Tee-Object -FilePath $registro -Append
    if ($LASTEXITCODE -ne 0) { throw "msdeploy termino con codigo $LASTEXITCODE. Revise $registro" }
}

switch ($Accion) {

    'Reiniciar' {
        # Reinicia la aplicacion: sube app_offline.htm (IIS detiene la app) y lo borra (IIS la arranca de nuevo y relee secretsSettings.json).
        $tmp = Join-Path $env:TEMP ("bufins-offline-" + $sello)
        New-Item -ItemType Directory -Force $tmp | Out-Null
        try {
            Set-Content -Path (Join-Path $tmp 'app_offline.htm') -Value '<html><body>Reiniciando...</body></html>' -Encoding utf8
            Ejecutar @('-verb:sync', "-source:contentPath=`"$tmp`"", "-dest:contentPath=`"$Sitio`",$conexion", '-allowUntrusted', '-enableRule:DoNotDeleteRule') 'Deteniendo la aplicacion (app_offline.htm)'
        } finally { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
        Start-Sleep -Seconds 5
        Ejecutar @('-verb:delete', "-dest:contentPath=`"$Sitio/app_offline.htm`",$conexion", '-allowUntrusted') 'Arrancando la aplicacion (se borra app_offline.htm)'
        Write-Host 'Aplicacion reiniciada. Pruebe el login.' -ForegroundColor Green
    }

    'BajarLogs' {
        # Solo lectura: copia App_Data\logs del sitio a una carpeta local para diagnosticar.
        $destino = Join-Path $carpetaBase "logs-$sello"
        New-Item -ItemType Directory -Force $destino | Out-Null
        Ejecutar @('-verb:sync', "-source:contentPath=`"$Sitio\App_Data\logs`",$conexion", "-dest:contentPath=`"$destino`"", '-allowUntrusted') "Bajando App_Data\logs de $Sitio a $destino"
        Write-Host "Logs en: $destino" -ForegroundColor Green
    }

    'Respaldar' {
        $destino = Join-Path $carpetaBase "respaldo-$sello"
        Ejecutar @('-verb:sync', "-source:contentPath=`"$Sitio`",$conexion", "-dest:archiveDir=`"$destino`"", '-allowUntrusted') "Descargando copia completa de $Sitio a $destino"
        $f = Get-ChildItem $destino -Recurse -File -ErrorAction SilentlyContinue
        Write-Host ""
        Write-Host ("Respaldo listo: {0} archivos, {1:N1} MB en {2}" -f $f.Count, (($f | Measure-Object Length -Sum).Sum / 1MB), $destino) -ForegroundColor Green
        Write-Host "Verifique que existan: Web.config, bin\ y los secrets*.config del sitio antiguo."
    }

    'Publicar' {
        if (-not (Test-Path (Join-Path $Paquete 'web.config'))) { throw "El paquete no es valido (falta web.config): $Paquete" }
        if (-not (Test-Path (Join-Path $Paquete 'bufinscustomers.dll'))) { throw "El paquete no es valido (falta bufinscustomers.dll): $Paquete" }
        if (Get-ChildItem $Paquete -Recurse -File | Where-Object { $_.Name -like 'secrets*' -and $_.Name -notlike '*.example' }) {
            throw 'El paquete contiene secrets*: no se publica. Genere el paquete de nuevo.'
        }
        $args2 = @('-verb:sync', "-source:contentPath=`"$Paquete`"", "-dest:contentPath=`"$Sitio`",$conexion", '-allowUntrusted',
                   '-enableRule:AppOffline') + $omitir
        if ($ConservarArchivosExtra) { $args2 += '-enableRule:DoNotDeleteRule' }
        if (-not $Aplicar) {
            $args2 += '-whatif'
            Write-Host "SIMULACION: no se cambia nada en el servidor. Use -Aplicar para publicar." -ForegroundColor Yellow
        } else {
            Write-Host "Se publicara en $Sitio. El sitio mostrara 'app_offline' unos segundos/minutos mientras se copian los archivos." -ForegroundColor Yellow
            if ((Read-Host 'Escriba PUBLICAR para continuar') -ne 'PUBLICAR') { throw 'Cancelado.' }
        }
        Ejecutar $args2 ($(if ($Aplicar) { "Publicando $Paquete en $Sitio" } else { "Simulando publicacion de $Paquete en $Sitio" }))
    }

    'SubirSecretos' {
        # La version nueva lee secretsSettings.json (el sitio antiguo usaba secrets*.config, que se conservan).
        # Sube solo ese archivo (DoNotDeleteRule: no borra nada del servidor).
        if (-not (Test-Path $Secretos)) { throw "No existe $Secretos (copie secretsSettings.json.example y complete los valores)." }
        $tmp = Join-Path $env:TEMP ("bufins-secretos-" + $sello)
        New-Item -ItemType Directory -Force $tmp | Out-Null
        try {
            Copy-Item $Secretos (Join-Path $tmp 'secretsSettings.json') -Force
            $args2 = @('-verb:sync', "-source:contentPath=`"$tmp`"", "-dest:contentPath=`"$Sitio`",$conexion", '-allowUntrusted', '-enableRule:DoNotDeleteRule')
            if (-not $Aplicar) { $args2 += '-whatif'; Write-Host 'SIMULACION: use -Aplicar para subir el archivo.' -ForegroundColor Yellow }
            Ejecutar $args2 'Subiendo secretsSettings.json al sitio (no borra nada)'
        } finally { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
    }

    'Restaurar' {
        if (-not $Respaldo -or -not (Test-Path $Respaldo)) { throw 'Indique -Respaldo con la carpeta del respaldo (la que creo la accion Respaldar).' }
        $args2 = @('-verb:sync', "-source:archiveDir=`"$Respaldo`"", "-dest:contentPath=`"$Sitio`",$conexion", '-allowUntrusted', '-enableRule:AppOffline')
        if (-not $Aplicar) {
            $args2 += '-whatif'
            Write-Host "SIMULACION: no se cambia nada en el servidor. Use -Aplicar para restaurar." -ForegroundColor Yellow
        } else {
            Write-Host "Se RESTAURARA $Sitio al estado del respaldo $Respaldo (reemplaza lo publicado)." -ForegroundColor Yellow
            if ((Read-Host 'Escriba RESTAURAR para continuar') -ne 'RESTAURAR') { throw 'Cancelado.' }
        }
        Ejecutar $args2 ($(if ($Aplicar) { "Restaurando $Sitio desde $Respaldo" } else { "Simulando restauracion desde $Respaldo" }))
    }
}

Write-Host ""
Write-Host "Registro: $registro"
