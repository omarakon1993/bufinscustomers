using System.Net.Http.Headers;
using System.Text.Json;

namespace AgenteBufins;

public enum Categoria
{
    /// <summary>2xx: aceptado.</summary>
    Ok,
    /// <summary>Falla transitoria (red, 5xx, 429, cargue activo de otra persona…): se reintenta más tarde y el archivo se deja donde está.</summary>
    Reintentar,
    /// <summary>BUFINS rechazó el archivo (400/422…): reintentar igual no lo arregla; el archivo pasa a «errores».</summary>
    Rechazado,
    /// <summary>401/403: la clave no sirve (o la IP no está permitida). Se pausa todo; los archivos no se tocan.</summary>
    Credencial
}

public sealed record RespuestaApi(Categoria Categoria, int Estado, string? Codigo, string? Mensaje, JsonElement? Cuerpo, string CuerpoTexto);

/// <summary>Cliente mínimo de la API de ingesta (https, Authorization: Bearer). Sin dependencias fuera de .NET.</summary>
public sealed class BufinsClient : IDisposable
{
    private readonly HttpClient _http;

    public BufinsClient(string urlBase, string clave)
    {
        _http = new HttpClient { BaseAddress = new Uri(urlBase.TrimEnd('/') + "/"), Timeout = TimeSpan.FromMinutes(15) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", clave);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("AgenteBufins/1.0");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public Task<RespuestaApi> PingAsync(CancellationToken ct) => EnviarAsync(() => new HttpRequestMessage(HttpMethod.Get, "ping"), ct);

    public Task<RespuestaApi> EstadoEjecucionAsync(long id, CancellationToken ct) =>
        EnviarAsync(() => new HttpRequestMessage(HttpMethod.Get, $"ejecuciones/{id}"), ct);

    public Task<RespuestaApi> EjecutarModelosAsync(int idEscenario, CancellationToken ct) =>
        EnviarAsync(() => new HttpRequestMessage(HttpMethod.Post, "modelos/ejecutar")
        { Content = new StringContent(JsonSerializer.Serialize(new { idEscenario }), System.Text.Encoding.UTF8, "application/json") }, ct);

    public Task<RespuestaApi> SubirArchivoAsync(string ruta, int anio, string modo, int idEscenario, bool confirmar, string idempotencyKey, CancellationToken ct) =>
        EnviarAsync(() =>
        {
            var form = new MultipartFormDataContent
            {
                { new StringContent(anio.ToString()), "anio" },
                { new StringContent(modo), "modo" },
                { new StringContent(idEscenario.ToString()), "idEscenario" },
                { new StringContent(confirmar ? "true" : "false"), "confirmar" }
            };
            var archivo = new StreamContent(File.OpenRead(ruta));
            archivo.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(archivo, "archivo", Path.GetFileName(ruta));
            var req = new HttpRequestMessage(HttpMethod.Post, "cargues/archivo") { Content = form };
            req.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
            return req;
        }, ct);

    private async Task<RespuestaApi> EnviarAsync(Func<HttpRequestMessage> crear, CancellationToken ct)
    {
        try
        {
            using var req = crear();
            using var resp = await _http.SendAsync(req, ct);
            string texto = await resp.Content.ReadAsStringAsync(ct);
            int estado = (int)resp.StatusCode;

            JsonElement? json = null;
            string? codigo = null, mensaje = null;
            try
            {
                using var doc = JsonDocument.Parse(texto);
                json = doc.RootElement.Clone();
                if (json.Value.ValueKind == JsonValueKind.Object && json.Value.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
                {
                    codigo = err.TryGetProperty("codigo", out var c) ? c.GetString() : null;
                    mensaje = err.TryGetProperty("mensaje", out var m) ? m.GetString() : null;
                }
            }
            catch (JsonException) { /* la respuesta no era JSON (p. ej. una página de un proxy): se trata por su código HTTP */ }

            return new RespuestaApi(Clasificar(estado, codigo), estado, codigo, mensaje, json, texto);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            string causa = ex.InnerException != null ? $"{ex.Message} → {ex.InnerException.GetType().Name}: {ex.InnerException.Message}" : $"{ex.GetType().Name}: {ex.Message}";
            return new RespuestaApi(Categoria.Reintentar, 0, "sin_conexion", $"No se pudo comunicar con BUFINS ({causa}).", null, "");
        }
    }

    private static Categoria Clasificar(int estado, string? codigo)
    {
        if (estado is >= 200 and < 300) return Categoria.Ok;
        if (estado is 401 or 403) return Categoria.Credencial;
        if (estado is 408 or 429 or >= 500) return Categoria.Reintentar;
        if (estado == 409) return Categoria.Reintentar;                  // cargue pendiente de otra persona / ejecución en curso: se reintenta luego
        return Categoria.Rechazado;                                       // 400, 404, 422…
    }

    public void Dispose() => _http.Dispose();
}
