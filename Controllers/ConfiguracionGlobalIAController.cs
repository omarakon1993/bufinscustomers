using bufinscustomers.Helpers;
using bufinscustomers.Models;
using bufinscustomers.Permisos;
using bufinscustomers.Services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Mvc;

namespace bufinscustomers.Controllers
{
    [ValidarSesion]
    [SoloSuperAdmin]
    public class ConfiguracionGlobalIAController : BaseController
    {
        private readonly ConfiguracionSistemaService _svc = new ConfiguracionSistemaService();

        public ActionResult Index()
        {
            var items = _svc.ObtenerTodos();
            return View("~/Views/Configuracion/ConfiguracionGlobalIA.cshtml", items);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Guardar(ConfiguracionSistemaItem item)
        {
            if (string.IsNullOrWhiteSpace(item?.Clave))
            {
                SetErrorMessage(R("CfgIA_MsgErrorGuardar") ?? "La clave no puede estar vacía.");
                return RedirectToAction("Index");
            }

            // MaxTokens: validar rango
            if (item.Clave == "OpenAIMaxTokens")
            {
                if (int.TryParse(item.Valor, out int t) && (t < 100 || t > 100000))
                {
                    SetErrorMessage("El valor de MaxTokens debe estar entre 100 y 100000.");
                    return RedirectToAction("Index");
                }
            }

            bool ok = _svc.Guardar(item.Clave, item.Valor ?? "", item.Descripcion ?? "");
            if (ok)
                SetSuccessMessage(R("CfgIA_MsgGuardadoOk") ?? "Configuración guardada correctamente.");
            else
                SetErrorMessage(R("CfgIA_MsgErrorGuardar") ?? "Error al guardar la configuración.");

            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Eliminar(string clave)
        {
            bool ok = _svc.Eliminar(clave ?? "");
            if (ok)
                SetSuccessMessage("Configuración eliminada.");
            else
                SetErrorMessage("No se pudo eliminar la configuración.");

            return RedirectToAction("Index");
        }

        [HttpGet]
        public async Task<JsonResult> ObtenerModelosOpenAI()
        {
            try
            {
                string apiKey = (_svc.ObtenerValor("OpenAIApiKey") ?? "").Trim();
                if (string.IsNullOrEmpty(apiKey))
                    return Json(new { ok = false, mensaje = R("CfgIA_JS_ApiKeyVacia") ?? "Primero guarda una API Key válida." },
                        JsonRequestBehavior.AllowGet);

                using (var http = new HttpClient())
                {
                    http.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", apiKey);
                    http.Timeout = TimeSpan.FromSeconds(12);

                    var resp = await http.GetAsync("https://api.openai.com/v1/models");
                    if (!resp.IsSuccessStatusCode)
                        return Json(new { ok = false, mensaje = "OpenAI respondió: " + (int)resp.StatusCode },
                            JsonRequestBehavior.AllowGet);

                    var body = await resp.Content.ReadAsStringAsync();
                    dynamic json = JsonConvert.DeserializeObject(body);

                    var modelos = new List<string>();
                    foreach (var m in json.data)
                    {
                        string id = m.id?.ToString() ?? "";
                        if (EsModeloChat(id))
                            modelos.Add(id);
                    }
                    modelos.Sort();

                    return Json(new { ok = true, modelos }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, mensaje = "Error: " + ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        private static bool EsModeloChat(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            return id.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("o1", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("o3", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("o4", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("chatgpt", StringComparison.OrdinalIgnoreCase);
        }
    }
}
