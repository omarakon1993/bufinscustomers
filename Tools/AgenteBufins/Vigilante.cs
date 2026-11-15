using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Hosting;

namespace AgenteBufins;

/// <summary>
/// Vigila la carpeta y envía cada plantilla Excel a la API de BUFINS. Reglas:
///  · Solo se toca un archivo que lleva <c>estableSegundos</c> sin modificarse y que se puede abrir en exclusiva (no se sube uno que aún se copia).
///  · «Idempotency-Key» = huella del contenido + año + modo + escenario: reenviar el mismo archivo no duplica el cargue en BUFINS.
///  · Aceptado → <c>procesados/</c> (con el resultado en .json). Rechazado por BUFINS o con errores de validación → <c>errores/</c> (con el motivo).
///    Falla transitoria (red, 5xx, 429, cargue pendiente de otra persona) → el archivo se deja en su sitio y se reintenta con espera creciente (hasta 30 min).
///    Clave no válida / IP no permitida → se pausa todo 10 min y no se mueve nada.
/// </summary>
public sealed class Vigilante : BackgroundService
{
    private readonly Configuracion _cfg;
    private readonly BufinsClient _cliente;
    private readonly Dictionary<string, (int intentos, DateTime proximo)> _reintentos = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _pausaHasta = DateTime.MinValue;

    public Vigilante(Configuracion cfg, BufinsClient cliente) { _cfg = cfg; _cliente = cliente; }

    private string Procesados => Path.Combine(_cfg.Carpeta, "procesados");
    private string Errores => Path.Combine(_cfg.Carpeta, "errores");

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        Registro.Info($"Agente iniciado. Vigilando «{_cfg.Carpeta}» cada {_cfg.IntervaloSegundos} s.");
        PrepararCarpetas();
        await VerificarConexionAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            try { await CicloAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { Registro.Error("Error inesperado en el ciclo (el agente continúa)", ex); }

            try { await Task.Delay(TimeSpan.FromSeconds(_cfg.IntervaloSegundos), ct); }
            catch (OperationCanceledException) { break; }
        }
        Registro.Info("Agente detenido.");
    }

    public void PrepararCarpetas()
    {
        Directory.CreateDirectory(_cfg.Carpeta);
        Directory.CreateDirectory(Procesados);
        Directory.CreateDirectory(Errores);
    }

    /// <summary>Comprueba la clave y muestra a qué integración/empresa pertenece. Devuelve false si no sirve.</summary>
    public async Task<bool> VerificarConexionAsync(CancellationToken ct)
    {
        var r = await _cliente.PingAsync(ct);
        if (r.Categoria == Categoria.Ok && r.Cuerpo is { } j)
        {
            string entorno = j.TryGetProperty("entorno", out var e) ? e.GetString() ?? "?" : "?";
            Registro.Info($"Conexión correcta. Integración «{Texto(j, "integracion")}» · empresa «{Texto(j, "empresa")}» · entorno {entorno}"
                + (Bool(j, "permiteConfirmar") ? " · puede confirmar y ejecutar modelos." : " · confirma una persona."));
            if (entorno == "pruebas") Registro.Aviso("Es una integración de PRUEBAS (clave bft_): nada de lo que se envíe modifica datos reales.");
            return true;
        }
        Registro.Error($"No se pudo verificar la conexión: {r.Mensaje ?? $"HTTP {r.Estado}"}");
        if (r.Categoria == Categoria.Credencial) _pausaHasta = DateTime.Now.AddMinutes(10);
        return false;
    }

    /// <summary>Una pasada por la carpeta. Devuelve true si todo lo que había salió bien (sin rechazos ni reintentos pendientes).</summary>
    public async Task<bool> CicloAsync(CancellationToken ct)
    {
        if (DateTime.Now < _pausaHasta) return false;
        bool todoBien = true;

        var archivos = Directory.EnumerateFiles(_cfg.Carpeta, "*.*", SearchOption.TopDirectoryOnly)
            .Where(f => (f.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase))
                        && !Path.GetFileName(f).StartsWith("~$", StringComparison.Ordinal))   // «~$…» = archivo temporal de Excel abierto
            .OrderBy(f => File.GetLastWriteTimeUtc(f))
            .ToList();

        foreach (var ruta in _reintentos.Keys.Where(k => !File.Exists(k)).ToList()) _reintentos.Remove(ruta);

        foreach (var ruta in archivos)
        {
            ct.ThrowIfCancellationRequested();
            if (_reintentos.TryGetValue(ruta, out var est) && est.proximo > DateTime.Now) { todoBien = false; continue; }
            if ((DateTime.UtcNow - File.GetLastWriteTimeUtc(ruta)).TotalSeconds < _cfg.EstableSegundos) continue;   // aún se está copiando/guardando
            if (!PuedeAbrirEnExclusiva(ruta)) continue;                                                                // otro programa lo tiene abierto

            bool ok = await ProcesarAsync(ruta, ct);
            todoBien &= ok;
            if (DateTime.Now < _pausaHasta) return false;   // credencial inválida: no se sigue
        }
        return todoBien;
    }

    private async Task<bool> ProcesarAsync(string ruta, CancellationToken ct)
    {
        string nombre = Path.GetFileName(ruta);
        var (anio, modo) = _cfg.InterpretarNombre(nombre);
        string huella = Huella(ruta);
        string clave = $"agente-{huella[..32]}-{anio}-{modo}-{_cfg.IdEscenario}";

        Registro.Info($"Enviando «{nombre}» (año {anio}, {modo}, escenario {_cfg.IdEscenario}, {new FileInfo(ruta).Length / 1024:N0} KB)…");
        var r = await _cliente.SubirArchivoAsync(ruta, anio, modo, _cfg.IdEscenario, _cfg.Confirmar, clave, ct);

        switch (r.Categoria)
        {
            case Categoria.Ok:
                _reintentos.Remove(ruta);
                return await AceptadoAsync(ruta, nombre, r, ct);

            case Categoria.Credencial:
                Registro.Error($"BUFINS rechazó la clave o la dirección IP ({r.Codigo}): {r.Mensaje}. Se pausa 10 minutos; no se movió ningún archivo.");
                _pausaHasta = DateTime.Now.AddMinutes(10);
                return false;

            case Categoria.Rechazado:
                _reintentos.Remove(ruta);
                Registro.Error($"BUFINS rechazó «{nombre}» ({r.Codigo ?? "HTTP " + r.Estado}): {r.Mensaje}");
                MoverA(Errores, ruta, $"HTTP {r.Estado} · {r.Codigo}\r\n{r.Mensaje}\r\n\r\n{r.CuerpoTexto}", "error.txt");
                return false;

            default: // Reintentar
                int intentos = _reintentos.TryGetValue(ruta, out var e) ? e.intentos + 1 : 1;
                var espera = TimeSpan.FromMinutes(Math.Min(Math.Pow(2, intentos - 1), 30));
                _reintentos[ruta] = (intentos, DateTime.Now + espera);
                Registro.Aviso($"No se pudo enviar «{nombre}» ({r.Codigo ?? "HTTP " + r.Estado}): {r.Mensaje} Se reintenta en {espera.TotalMinutes:0} min (intento {intentos}).");
                return false;
        }
    }

    private async Task<bool> AceptadoAsync(string ruta, string nombre, RespuestaApi r, CancellationToken ct)
    {
        var j = r.Cuerpo!.Value;
        bool repetido = Bool(j, "repetido");
        string confirmacion = Texto(j, "confirmacion");
        string estado = "?"; long idLote = 0; int filas = 0, errores = 0, advertencias = 0;
        if (j.TryGetProperty("lote", out var lote) && lote.ValueKind == JsonValueKind.Object)
        {
            estado = Texto(lote, "estado");
            idLote = lote.TryGetProperty("idLote", out var il) && il.TryGetInt64(out var v) ? v : 0;
            filas = Entero(lote, "totalFilas"); errores = Entero(lote, "totalErrores"); advertencias = Entero(lote, "totalAdvertencias");
        }

        if (repetido) Registro.Info($"BUFINS ya tenía este mismo envío (cargue {idLote}): no se duplicó.");
        Registro.Info($"«{nombre}» recibido: cargue {idLote}, estado {estado}, {filas:N0} filas, {errores} error(es), {advertencias} advertencia(s).");

        // Con errores de validación el archivo hay que corregirlo: va a «errores» con la referencia del cargue.
        if (estado == "ConErrores")
        {
            Registro.Error($"El cargue {idLote} quedó con {errores} error(es) de validación y no se puede confirmar. Corrija el archivo y vuelva a copiarlo (detalle: «Revisar cargue» en BUFINS o GET /cargues/{idLote}/hallazgos).");
            MoverA(Errores, ruta, r.CuerpoTexto, "resultado.json");
            return false;
        }

        if (confirmacion == "confirmado") Registro.Info($"Cargue {idLote} CONFIRMADO.");
        else if (confirmacion == "simulado") Registro.Info($"Cargue {idLote}: confirmación SIMULADA (integración de pruebas).");
        else Registro.Info($"Cargue {idLote} queda PENDIENTE DE REVISIÓN: una persona lo confirma en «Revisar cargue» de BUFINS.");

        MoverA(Procesados, ruta, r.CuerpoTexto, "resultado.json");

        if (_cfg.EjecutarModelos && confirmacion == "confirmado") await EjecutarModelosAsync(ct);
        return true;
    }

    private async Task EjecutarModelosAsync(CancellationToken ct)
    {
        var r = await _cliente.EjecutarModelosAsync(_cfg.IdEscenario, ct);
        if (r.Categoria != Categoria.Ok || r.Cuerpo is not { } j)
        {
            Registro.Aviso($"No se pudo pedir la ejecución de modelos ({r.Codigo ?? "HTTP " + r.Estado}): {r.Mensaje}");
            return;
        }
        long id = j.TryGetProperty("idEjecucion", out var ide) && ide.TryGetInt64(out var v) ? v : 0;
        Registro.Info($"Ejecución de modelos {id} iniciada (corre en segundo plano en BUFINS)…");

        var limite = DateTime.Now.AddMinutes(40);
        while (DateTime.Now < limite)
        {
            await Task.Delay(TimeSpan.FromSeconds(15), ct);
            var e = await _cliente.EstadoEjecucionAsync(id, ct);
            if (e.Categoria != Categoria.Ok || e.Cuerpo is not { } ej) continue;
            string estado = Texto(ej, "estado");
            if (estado == "EnCurso") continue;

            int ok = 0, err = 0;
            if (ej.TryGetProperty("modelos", out var ms) && ms.ValueKind == JsonValueKind.Array)
                foreach (var m in ms.EnumerateArray()) { var s = Texto(m, "estado"); if (s == "Completado") ok++; else if (s == "Error") err++; }
            if (estado == "Completada") Registro.Info($"Modelos ejecutados: {ok} completado(s). Los informes ya reflejan el cargue.");
            else Registro.Aviso($"La ejecución de modelos terminó como {estado}: {ok} completado(s), {err} con error. Revise «Ejecución de Modelos» en BUFINS.");
            return;
        }
        Registro.Aviso($"La ejecución de modelos {id} sigue en curso tras 40 minutos; el agente deja de esperarla (BUFINS la termina por su cuenta).");
    }

    // ── Utilidades ─────────────────────────────────────────────────────────────────

    private void MoverA(string carpetaDestino, string ruta, string detalle, string extensionDetalle)
    {
        try
        {
            string baseNombre = $"{Path.GetFileNameWithoutExtension(ruta)}__{DateTime.Now:yyyyMMdd_HHmmss}";
            string destino = Path.Combine(carpetaDestino, baseNombre + Path.GetExtension(ruta));
            File.Move(ruta, destino);
            File.WriteAllText(Path.Combine(carpetaDestino, $"{baseNombre}.{extensionDetalle}"), detalle);
        }
        catch (Exception ex) { Registro.Error($"No se pudo mover «{Path.GetFileName(ruta)}» a «{carpetaDestino}»", ex); }
    }

    private static bool PuedeAbrirEnExclusiva(string ruta)
    {
        try { using var fs = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.None); return true; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static string Huella(string ruta)
    {
        using var fs = File.OpenRead(ruta);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }

    private static string Texto(JsonElement e, string campo) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static bool Bool(JsonElement e, string campo) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.True;
    private static int Entero(JsonElement e, string campo) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(campo, out var v) && v.TryGetInt32(out var n) ? n : 0;
}
