using bufinscustomers.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.Caching;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace bufinscustomers.Services
{
    public class IAService : BaseService
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        private const string OpenAIEndpoint = "https://api.openai.com/v1/chat/completions";
        private const string OpenAIModelDefault = "gpt-4o-mini";
        private const int MaxTokensDefault = 1024;
        private static readonly MemoryCache _cache = MemoryCache.Default;
        private const int CacheTtlHoras = 4;

        private readonly string _apiKey;

        public IAService(string apiKey)
        {
            _apiKey = apiKey;
        }

        public async Task<IAConsultaResponse> ConsultarAsync(
            IAConsultaRequest request,
            string instruccionesPersonalizadas = null,
            string guardrailSistema = null,
            string modelo = null,
            int maxTokens = 0)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_apiKey))
                {
                    return new IAConsultaResponse
                    {
                        Exitoso = false,
                        Error = "La clave de API de OpenAI no está configurada. Configúrela en Configuración Global IA."
                    };
                }

                string modeloFinal   = !string.IsNullOrWhiteSpace(modelo) ? modelo : OpenAIModelDefault;
                int    tokensFinal   = maxTokens > 0 ? maxTokens : MaxTokensDefault;

                string cacheKey = GenerarCacheKey(request, instruccionesPersonalizadas, guardrailSistema, modeloFinal, tokensFinal);
                if (_cache.Contains(cacheKey))
                {
                    var cached = (IAConsultaResponse)_cache.Get(cacheKey);
                    cached.DesdeCache = true;
                    return cached;
                }

                string sistemaMsg = string.IsNullOrWhiteSpace(guardrailSistema)
                    ? "Eres un asistente financiero exclusivo de la plataforma Bufins. " +
                      "Solo puedes responder preguntas relacionadas con los datos financieros que se te proporcionan " +
                      "(balances, P&G, EBITDA, flujo de caja, gastos, ingresos y tablas financieras de las empresas). " +
                      "Si el usuario hace una pregunta que no tiene relación con los datos financieros de Bufins, " +
                      "declina cortésmente y explica que tu función es exclusivamente el análisis financiero de Bufins. " +
                      "Responde siempre en español."
                    : guardrailSistema;

                bool   esConversacionNueva   = request.Historial == null || request.Historial.Count == 0;
                string promptContextoInicial = null;

                var messages = new List<object>();
                messages.Add(new { role = "system", content = sistemaMsg });

                if (esConversacionNueva)
                {
                    // Primera llamada: construye contexto completo con los datos
                    promptContextoInicial = ConstruirPrompt(request, instruccionesPersonalizadas);
                    messages.Add(new { role = "user", content = promptContextoInicial });
                }
                else
                {
                    // Conversación en curso: envía historial + nueva pregunta
                    foreach (var msg in request.Historial)
                        messages.Add(new { role = msg.Rol, content = msg.Contenido });

                    if (!string.IsNullOrWhiteSpace(request.Pregunta))
                        messages.Add(new { role = "user", content = request.Pregunta });
                }

                var body = new
                {
                    model       = modeloFinal,
                    messages    = messages.ToArray(),
                    temperature = 0.4,
                    max_tokens  = tokensFinal
                };

                string jsonBody = JsonConvert.SerializeObject(body);
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, OpenAIEndpoint);
                httpRequest.Headers.Add("Authorization", "Bearer " + _apiKey);
                httpRequest.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                HttpResponseMessage httpResponse = await _httpClient.SendAsync(httpRequest);
                string responseText = await httpResponse.Content.ReadAsStringAsync();

                if (!httpResponse.IsSuccessStatusCode)
                {
                    string errorDetail = ExtraerMensajeError(responseText);
                    return new IAConsultaResponse
                    {
                        Exitoso = false,
                        Error = $"Error de API ({(int)httpResponse.StatusCode}): {errorDetail}"
                    };
                }

                string respuesta = ExtraerTextoRespuesta(responseText);

                var response = new IAConsultaResponse
                {
                    Exitoso              = true,
                    Respuesta            = respuesta,
                    PromptContextoInicial = promptContextoInicial
                };

                _cache.Set(cacheKey, response, DateTimeOffset.Now.AddHours(CacheTtlHoras));
                return response;
            }
            catch (TaskCanceledException)
            {
                return new IAConsultaResponse { Exitoso = false, Error = "La solicitud excedió el tiempo de espera (60 segundos)." };
            }
            catch (Exception ex)
            {
                return new IAConsultaResponse { Exitoso = false, Error = $"Error al consultar IA: {ex.Message}" };
            }
        }

        private string GenerarCacheKey(IAConsultaRequest request, string instrucciones, string guardrail, string modelo, int maxTokens)
        {
            // Hash del contenido real de los datos para invalidar caché cuando cambian
            string dataHash = string.Empty;
            if (!string.IsNullOrEmpty(request.DatosJson))
            {
                using (var sha = SHA256.Create())
                {
                    byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(request.DatosJson));
                    dataHash = BitConverter.ToString(h).Replace("-", "").Substring(0, 12).ToLowerInvariant();
                }
            }

            // Hash del historial para diferenciar turnos de la misma conversación
            string historialHash = string.Empty;
            if (request.Historial != null && request.Historial.Count > 0)
            {
                var sbH = new StringBuilder();
                foreach (var msg in request.Historial)
                    sbH.Append(msg.Rol).Append(':').Append(msg.Contenido).Append('|');
                using (var md5h = MD5.Create())
                {
                    byte[] h = md5h.ComputeHash(Encoding.UTF8.GetBytes(sbH.ToString()));
                    historialHash = BitConverter.ToString(h).Replace("-", "").Substring(0, 8).ToLowerInvariant();
                }
            }

            string raw = $"{request.NombreTabla}|{request.FiltrosDescripcion}|{request.Pregunta}|{instrucciones}|{guardrail}|{dataHash}|{historialHash}|{modelo}|{maxTokens}";
            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(raw));
                return "ia_" + BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private string ConstruirPrompt(IAConsultaRequest request, string instruccionesPersonalizadas)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Eres un analista financiero experto en finanzas corporativas colombianas.");
            sb.AppendLine($"Tienes acceso a datos reales de la tabla financiera \"{request.NombreTabla}\".");

            if (!string.IsNullOrWhiteSpace(request.FiltrosDescripcion))
                sb.AppendLine($"Filtros aplicados: {request.FiltrosDescripcion}.");

            sb.AppendLine();
            sb.AppendLine("Los datos financieros disponibles en formato JSON:");
            sb.AppendLine(request.DatosJson);
            sb.AppendLine();

            if (string.IsNullOrWhiteSpace(request.Pregunta))
            {
                if (!string.IsNullOrWhiteSpace(instruccionesPersonalizadas))
                    sb.AppendLine(instruccionesPersonalizadas);
                else
                {
                    sb.AppendLine("Genera un resumen gerencial ejecutivo en 3 a 5 párrafos.");
                    sb.AppendLine("Identifica tendencias importantes, valores destacados y puntos de atención.");
                    sb.AppendLine("Incluye observaciones sobre variaciones entre períodos si los datos lo permiten.");
                }
            }
            else
            {
                sb.AppendLine($"Responde la siguiente pregunta basándote ÚNICAMENTE en los datos proporcionados:");
                sb.AppendLine(request.Pregunta);
                sb.AppendLine("Si la información no es suficiente para responder, indícalo claramente.");
            }

            sb.AppendLine();
            sb.AppendLine("Responde en español. Usa formato markdown con listas y **negrita** donde ayude a la claridad.");

            return sb.ToString();
        }

        private string ExtraerTextoRespuesta(string jsonResponse)
        {
            try
            {
                var obj = JObject.Parse(jsonResponse);
                return obj["choices"]?[0]?["message"]?["content"]?.ToString()
                    ?? "No se pudo extraer la respuesta.";
            }
            catch
            {
                return "Error al procesar la respuesta de OpenAI.";
            }
        }

        private string ExtraerMensajeError(string jsonResponse)
        {
            try
            {
                var obj = JObject.Parse(jsonResponse);
                return obj["error"]?["message"]?.ToString() ?? jsonResponse;
            }
            catch
            {
                return jsonResponse;
            }
        }
    }
}
