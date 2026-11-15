<#
  Publica el agente de carpeta como un ejecutable unico y autocontenido (no requiere instalar .NET en el servidor del cliente).
  Uso (desde la raiz del repositorio):   powershell -File Tools\AgenteBufins\Publicar-Agente.ps1
  Resultado: Tools\AgenteBufins\publicado\  ->  AgenteBufins.exe + agente.ejemplo.json  (listo para comprimir y entregar)
#>
$ErrorActionPreference = 'Stop'
$proyecto = Join-Path $PSScriptRoot 'AgenteBufins.csproj'
$salida   = Join-Path $PSScriptRoot 'publicado'
if (Test-Path $salida) { Remove-Item $salida -Recurse -Force }
dotnet publish $proyecto -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $salida
if ($LASTEXITCODE -ne 0) { throw 'La publicacion fallo.' }
Get-ChildItem $salida | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}}
