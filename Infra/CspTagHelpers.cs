using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Newtonsoft.Json;
using bufinscustomers.Helpers;

namespace bufinscustomers
{
    /// <summary>
    /// Pone el nonce de la CSP en todo &lt;script&gt; en línea de las vistas (sin <c>src</c> y de tipo JavaScript). Sin esto, la política
    /// estricta (<c>script-src 'self' 'nonce-…'</c>) los bloquearía. Los de <c>src</c> no lo necesitan ('self').
    /// </summary>
    [HtmlTargetElement("script")]
    public class NonceScriptTagHelper : TagHelper
    {
        private readonly IHttpContextAccessor _http;
        public NonceScriptTagHelper(IHttpContextAccessor http) { _http = http; }

        public override void Process(TagHelperContext context, TagHelperOutput output)
        {
            // También los <script src>: en un fragmento inyectado con jQuery, el archivo se descarga y se ejecuta como código en línea
            // con el nonce del elemento; sin él la CSP lo bloquea.
            if (output.Attributes.ContainsName("nonce")) return;
            var tipoAttr = output.Attributes["type"];
            if (tipoAttr != null)
            {
                string tipo = (tipoAttr.Value ?? "").ToString().ToLowerInvariant();
                if (tipo.Length > 0 && !tipo.Contains("javascript") && tipo != "module") return;   // json, plantillas… no se ejecutan
            }
            string nonce = CabecerasSeguridad.Nonce(_http.HttpContext);
            if (!string.IsNullOrEmpty(nonce)) output.Attributes.SetAttribute("nonce", nonce);
        }
    }

    /// <summary>
    /// Convierte los atributos de evento en línea (<c>onclick="abrir(3, 'x')"</c>) en <c>data-h-click='{"s":[{"f":"abrir","a":[3,"x"]}]}'</c>:
    /// la CSP estricta no ejecuta código en atributos, y <c>Assets/js/inline-handlers.js</c> atiende esos atributos con un único listener.
    /// Solo se traduce la forma «llamadas a funciones con argumentos simples»; cualquier otra cosa se deja intacta y se registra en el log
    /// como aviso (hay que reescribirla: con la política estricta no se ejecutaría).
    /// </summary>
    /// <summary>Lógica común de las ayudas que traducen eventos en línea.</summary>
    public abstract class InlineHandlerBase : TagHelper
    {
        private static readonly string[] Eventos = { "click", "change", "input", "submit", "keyup", "keydown", "keypress", "mouseover", "mouseout", "dblclick" };

        public override void Process(TagHelperContext context, TagHelperOutput output)
        {
            foreach (var ev in Eventos)
            {
                string nombre = "on" + ev;
                var attr = output.Attributes[nombre];
                if (attr == null) continue;
                string codigo = Texto(attr.Value);
                string json;
                if (InlineHandlerParser.TryTraducir(codigo, out json))
                {
                    output.Attributes.Remove(attr);
                    output.Attributes.SetAttribute("data-h-" + ev, json);
                }
                else
                {
                    AppLogger.Warn("Evento en línea no traducible (" + nombre + "): " + (codigo.Length > 160 ? codigo.Substring(0, 160) + "…" : codigo));
                }
            }
        }

        private static string Texto(object v)
        {
            if (v == null) return "";
            if (v is IHtmlContent c)
            {
                using (var w = new StringWriter())
                {
                    c.WriteTo(w, HtmlEncoder.Default);
                    return WebUtility.HtmlDecode(w.ToString());
                }
            }
            return WebUtility.HtmlDecode(v.ToString());
        }
    }

    /// <summary>Elementos sin etiqueta de cierre (input, img): Razor exige declararlo con <c>TagStructure.WithoutEndTag</c>.</summary>
    [HtmlTargetElement("input", Attributes = "onclick", TagStructure = TagStructure.WithoutEndTag)]
    [HtmlTargetElement("input", Attributes = "onchange", TagStructure = TagStructure.WithoutEndTag)]
    [HtmlTargetElement("input", Attributes = "oninput", TagStructure = TagStructure.WithoutEndTag)]
    [HtmlTargetElement("input", Attributes = "onkeyup", TagStructure = TagStructure.WithoutEndTag)]
    [HtmlTargetElement("input", Attributes = "onkeydown", TagStructure = TagStructure.WithoutEndTag)]
    [HtmlTargetElement("input", Attributes = "onkeypress", TagStructure = TagStructure.WithoutEndTag)]
    [HtmlTargetElement("img", Attributes = "onclick", TagStructure = TagStructure.WithoutEndTag)]
    public class InlineHandlerVoidTagHelper : InlineHandlerBase { }

    [HtmlTargetElement("button", Attributes = "onclick")]
    [HtmlTargetElement("a", Attributes = "onclick")]
    [HtmlTargetElement("div", Attributes = "onclick")]
    [HtmlTargetElement("span", Attributes = "onclick")]
    [HtmlTargetElement("li", Attributes = "onclick")]
    [HtmlTargetElement("tr", Attributes = "onclick")]
    [HtmlTargetElement("td", Attributes = "onclick")]
    [HtmlTargetElement("th", Attributes = "onclick")]
    [HtmlTargetElement("i", Attributes = "onclick")]
    [HtmlTargetElement("label", Attributes = "onclick")]
    [HtmlTargetElement("p", Attributes = "onclick")]
    [HtmlTargetElement("summary", Attributes = "onclick")]
    [HtmlTargetElement("select", Attributes = "onchange")]
    [HtmlTargetElement("select", Attributes = "oninput")]
    [HtmlTargetElement("select", Attributes = "onkeyup")]
    [HtmlTargetElement("select", Attributes = "onkeydown")]
    [HtmlTargetElement("textarea", Attributes = "onchange")]
    [HtmlTargetElement("textarea", Attributes = "oninput")]
    [HtmlTargetElement("textarea", Attributes = "onkeyup")]
    [HtmlTargetElement("textarea", Attributes = "onkeydown")]
    [HtmlTargetElement("form", Attributes = "onsubmit")]
    [HtmlTargetElement("button", Attributes = "ondblclick")]
    [HtmlTargetElement("div", Attributes = "ondblclick")]
    public class InlineHandlerTagHelper : InlineHandlerBase { }

    /// <summary>
    /// Traductor de código de eventos en línea a una descripción JSON. Gramática (la misma que valida Assets/js/inline-handlers.js al ejecutar):
    ///   manejador := sentencia (';' sentencia)* [';']
    ///   sentencia := 'return' 'false' | 'return' llamada | llamada
    ///   llamada   := ruta '(' [arg (',' arg)*] ')'            ruta = identificador('.'identificador)*
    ///   arg       := número | 'texto' | "texto" | true | false | null | undefined | this | event | this.value | this.getAttribute('x')
    /// Salida: {"s":[{"f":"ruta","a":[args],"r":1?}],"p":1?}  («r» = el resultado false cancela el evento; «p» = return false literal).
    /// </summary>
    public static class InlineHandlerParser
    {
        public static bool TryTraducir(string codigo, out string json)
        {
            json = null;
            if (string.IsNullOrWhiteSpace(codigo)) return false;
            try
            {
                var p = new Lector(codigo);
                var sentencias = new List<Dictionary<string, object>>();
                bool prevenir = false;
                while (true)
                {
                    p.Saltar();
                    if (p.Fin) break;
                    if (p.Consumir(";")) continue;
                    bool ret = p.ConsumirPalabra("return");
                    if (ret)
                    {
                        p.Saltar();
                        if (p.ConsumirPalabra("false")) { prevenir = true; continue; }
                    }
                    string ruta = p.Ruta();
                    if (ruta == null) return false;
                    p.Saltar();
                    if (!p.Consumir("(")) return false;
                    var args = new List<object>();
                    p.Saltar();
                    if (!p.Consumir(")"))
                    {
                        while (true)
                        {
                            object a;
                            if (!p.Argumento(out a)) return false;
                            args.Add(a);
                            p.Saltar();
                            if (p.Consumir(",")) continue;
                            if (p.Consumir(")")) break;
                            return false;
                        }
                    }
                    var s = new Dictionary<string, object> { { "f", ruta }, { "a", args } };
                    if (ret) s["r"] = 1;
                    sentencias.Add(s);
                    p.Saltar();
                    if (!p.Fin && !p.Consumir(";")) return false;
                }
                if (sentencias.Count == 0 && !prevenir) return false;
                var res = new Dictionary<string, object> { { "s", sentencias } };
                if (prevenir) res["p"] = 1;
                json = JsonConvert.SerializeObject(res);
                return true;
            }
            catch { return false; }
        }

        private class Lector
        {
            private readonly string _t; private int _i;
            public Lector(string t) { _t = t; }
            public bool Fin { get { return _i >= _t.Length; } }
            public void Saltar() { while (_i < _t.Length && char.IsWhiteSpace(_t[_i])) _i++; }
            public bool Consumir(string s)
            {
                Saltar();
                if (string.CompareOrdinal(_t, _i, s, 0, s.Length) == 0) { _i += s.Length; return true; }
                return false;
            }
            public bool ConsumirPalabra(string w)
            {
                Saltar();
                if (string.CompareOrdinal(_t, _i, w, 0, w.Length) != 0) return false;
                int fin = _i + w.Length;
                if (fin < _t.Length && (char.IsLetterOrDigit(_t[fin]) || _t[fin] == '_' || _t[fin] == '$' || _t[fin] == '.')) return false;
                _i = fin; return true;
            }
            private static bool InicioId(char c) { return char.IsLetter(c) || c == '_' || c == '$'; }
            private static bool ParteId(char c) { return char.IsLetterOrDigit(c) || c == '_' || c == '$'; }
            public string Ruta()
            {
                Saltar();
                int ini = _i;
                while (true)
                {
                    if (_i >= _t.Length || !InicioId(_t[_i])) return null;
                    while (_i < _t.Length && ParteId(_t[_i])) _i++;
                    if (_i < _t.Length && _t[_i] == '.') { _i++; continue; }
                    break;
                }
                return _t.Substring(ini, _i - ini);
            }
            public bool Argumento(out object valor)
            {
                valor = null; Saltar();
                if (_i >= _t.Length) return false;
                char c = _t[_i];
                if (c == '\'' || c == '"') { string s; if (!Texto(out s)) return false; valor = s; return true; }
                if (char.IsDigit(c) || (c == '-' && _i + 1 < _t.Length && char.IsDigit(_t[_i + 1])))
                {
                    int ini = _i; _i++;
                    while (_i < _t.Length && (char.IsDigit(_t[_i]) || _t[_i] == '.')) _i++;
                    string num = _t.Substring(ini, _i - ini);
                    if (num.Contains(".")) { double d; if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return false; valor = d; }
                    else { long l; if (!long.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) return false; valor = l; }
                    return true;
                }
                if (ConsumirPalabra("true")) { valor = true; return true; }
                if (ConsumirPalabra("false")) { valor = false; return true; }
                if (ConsumirPalabra("null") || ConsumirPalabra("undefined")) { valor = null; return true; }
                if (ConsumirPalabra("event")) { valor = new Dictionary<string, object> { { "$", "event" } }; return true; }
                // this, this.value, this.getAttribute('x')
                if (string.CompareOrdinal(_t, _i, "this", 0, 4) == 0)
                {
                    int p = _i + 4;
                    if (p >= _t.Length || !(ParteId(_t[p]) || _t[p] == '.')) { _i = p; valor = new Dictionary<string, object> { { "$", "this" } }; return true; }
                    if (string.CompareOrdinal(_t, p, ".value", 0, 6) == 0 && (p + 6 >= _t.Length || !ParteId(_t[p + 6]))) { _i = p + 6; valor = new Dictionary<string, object> { { "$", "value" } }; return true; }
                    if (string.CompareOrdinal(_t, p, ".getAttribute", 0, 13) == 0)
                    {
                        _i = p + 13;
                        if (!Consumir("(")) return false;
                        Saltar();
                        string nom; if (_i >= _t.Length || !Texto(out nom)) return false;
                        if (!Consumir(")")) return false;
                        valor = new Dictionary<string, object> { { "$attr", nom } };
                        return true;
                    }
                }
                return false;
            }
            private bool Texto(out string s)
            {
                s = null; char q = _t[_i++]; var sb = new StringBuilder();
                while (_i < _t.Length)
                {
                    char c = _t[_i++];
                    if (c == q) { s = sb.ToString(); return true; }
                    if (c != '\\') { sb.Append(c); continue; }
                    if (_i >= _t.Length) return false;
                    char e = _t[_i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'v': sb.Append('\v'); break;
                        case '0': sb.Append('\0'); break;
                        case 'u':
                            if (_i + 4 > _t.Length) return false;
                            int u; if (!int.TryParse(_t.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out u)) return false;
                            sb.Append((char)u); _i += 4; break;
                        case 'x':
                            if (_i + 2 > _t.Length) return false;
                            int x; if (!int.TryParse(_t.Substring(_i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out x)) return false;
                            sb.Append((char)x); _i += 2; break;
                        default: sb.Append(e); break;   // \\ \' \" \/ y cualquier otro carácter escapado
                    }
                }
                return false;   // cadena sin cerrar
            }
        }
    }
}
