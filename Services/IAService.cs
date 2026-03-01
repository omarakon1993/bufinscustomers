using bufinscustomers.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace bufinscustomers.Services
{
    public class IAService : BaseService
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        private const string OpenAIEndpoint = "https://api.openai.com/v1/chat/completions";
        private const string OpenAIModel = "gpt-4o-mini";

        private readonly string _apiKey;

        public IAService(string apiKey)
        {
            _apiKey = apiKey;
        }

        public async Task<IAConsultaResponse> ConsultarAsync(IAConsultaRequest request, string instruccionesPersonalizadas = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_apiKey))
                {
                    return new IAConsultaResponse
                    {
                        Exitoso = false,
                        Error = "La clave de API de OpenAI no está configurada. Agregue 'OpenAIApiKey' en Web.config."
                    };
                }

                string prompt = ConstruirPrompt(request, instruccionesPersonalizadas);

                var body = new
                {
                    model = OpenAIModel,
                    messages = new[]
                    {
                        new { role = "user", content = prompt }
                    },
                    temperature = 0.4,
                    max_tokens = 1024
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

                return new IAConsultaResponse
                {
                    Exitoso = true,
                    Respuesta = respuesta
                };
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

        private string ConstruirPrompt(IAConsultaRequest request, string instruccionesPersonalizadas)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Eres un analista financiero experto en finanzas corporativas colombianas.");
            sb.AppendLine($"Tienes acceso a datos reales de la tabla financiera \"{request.NombreTabla}\".");

            if (!string.IsNullOrWhiteSpace(request.FiltrosDescripcion))
            {
                sb.AppendLine($"Filtros aplicados: {request.FiltrosDescripcion}.");
            }

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
