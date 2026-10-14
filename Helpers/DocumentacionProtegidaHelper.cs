using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using bufinscustomers.Models;
using bufinscustomers.Services;

namespace bufinscustomers.Helpers
{
    /// <summary>
    /// Sirve la documentación de un informe como página de SOLO LECTURA para el visor (iframe con sandbox): el documento vive en
    /// App_Data (no público), exige sesión, no se guarda en caché y se rechaza (403) al abrirlo directo en una pestaña. Al HTML se le
    /// inyecta, en el momento, una marca de agua con la leyenda de propiedad y quién la consulta (nombre, empresa y fecha), un aviso de
    /// confidencialidad y el bloqueo de imprimir/guardar/copiar/arrastrar/menú contextual; cada consulta queda en la auditoría.
    /// Desalienta la descarga casual; no puede impedir una captura de pantalla ni las herramientas del navegador.
    /// (Misma lógica que InformeHistPreciosController.Documentacion, parametrizada para los informes nuevos.)
    /// </summary>
    public static class DocumentacionProtegidaHelper
    {
        public static ActionResult Servir(Controller c, string rutaVirtual, string leyenda, string aviso,
            string entidadAuditoria, string nombreEntidad, string descripcion)
        {
            if (string.Equals(c.Request.Headers["Sec-Fetch-Dest"], "document", StringComparison.OrdinalIgnoreCase))
                return new HttpStatusCodeResult(403);   // abrir la URL directamente (pestaña nueva / "guardar como")

            string ruta = c.Server.MapPath(rutaVirtual);
            if (!System.IO.File.Exists(ruta)) return new HttpNotFoundResult();

            var usuario = UsuarioSesionHelper.UsuarioActual;
            string quien = ((usuario?.Nombre ?? "") + " " + (usuario?.Apellidos ?? "")).Trim();
            if (string.IsNullOrEmpty(quien)) quien = usuario?.Usuario ?? "";
            string empresaUsuario = EmpresaCacheHelper.ObtenerEmpresasCacheadas().FirstOrDefault(e => e.Id == usuario?.IdEmpresa)?.Nombre ?? "";
            string fecha = DateTime.Now.ToString("yyyy-MM-dd");

            // Marca de agua: leyenda de propiedad + nombre, empresa y fecha (sin hora, IP ni códigos).
            var partes = new List<string> { quien, empresaUsuario, fecha };
            string linea2 = string.Join("  ·  ", partes.Where(x => !string.IsNullOrWhiteSpace(x)));
            string html = InyectarProteccion(System.IO.File.ReadAllText(ruta, System.Text.Encoding.UTF8), leyenda, linea2, aviso);

            // Cada consulta queda en la auditoría (usuario, fecha/hora e IP los completa AuditoriaService).
            new AuditoriaService().RegistrarCambio(AuditoriaTipo.Informes, AuditoriaAccion.Ver, entidadAuditoria,
                (usuario?.Id ?? 0).ToString(), descripcion, null, null, usuario?.IdEmpresa, entidadNombre: nombreEntidad);

            c.Response.Cache.SetCacheability(System.Web.HttpCacheability.NoCache);
            c.Response.Cache.SetNoStore();
            c.Response.AddHeader("Content-Disposition", "inline");
            c.Response.AddHeader("X-Content-Type-Options", "nosniff");
            c.Response.AddHeader("Content-Security-Policy",
                "default-src 'none'; img-src data:; style-src 'unsafe-inline'; script-src 'unsafe-inline'; frame-ancestors 'self'; sandbox allow-scripts");
            return new ContentResult { Content = html, ContentType = "text/html", ContentEncoding = System.Text.Encoding.UTF8 };
        }

        /// <summary>Marca de agua repetida (leyenda de propiedad + datos de quien consulta), aviso fijo al pie y sin
        /// imprimir/guardar/copiar/arrastrar ni menú contextual dentro del documento.</summary>
        private static string InyectarProteccion(string html, string leyenda, string linea2, string aviso)
        {
            string marca = leyenda + "\n" + linea2;
            string bloque =
                "<style>" +
                "body{-webkit-user-select:none;user-select:none;padding-bottom:46px}img{-webkit-user-drag:none;user-drag:none}" +
                "#marca{position:fixed;inset:0;pointer-events:none;z-index:9998;overflow:hidden}" +
                "#marca span{position:absolute;white-space:pre;font:700 12px/1.45 'Segoe UI',Arial,sans-serif;color:rgba(88,58,255,.11);transform:rotate(-24deg)}" +
                "#aviso{position:fixed;left:0;right:0;bottom:0;z-index:9999;pointer-events:none;text-align:center;padding:9px 14px;font:700 11.5px 'Segoe UI',Arial,sans-serif;color:#fff;background:rgba(22,9,51,.88)}" +
                "@media print{html{display:none!important}}" +
                "</style>" +
                "<script>(function(){" +
                "var t='" + System.Web.HttpUtility.JavaScriptStringEncode(marca) + "';" +
                "var av='" + System.Web.HttpUtility.JavaScriptStringEncode(aviso) + "';" +
                "document.addEventListener('contextmenu',function(e){e.preventDefault();});" +
                "document.addEventListener('dragstart',function(e){e.preventDefault();});" +
                "document.addEventListener('copy',function(e){e.preventDefault();});" +
                "document.addEventListener('keydown',function(e){var k=(e.key||'').toLowerCase();if((e.ctrlKey||e.metaKey)&&(k==='s'||k==='p'||k==='c'||k==='u'||k==='a'))e.preventDefault();});" +
                "document.addEventListener('DOMContentLoaded',function(){var c=document.createElement('div');c.id='marca';" +
                "for(var y=-60;y<1800;y+=170){for(var x=-200;x<2000;x+=560){var s=document.createElement('span');s.textContent=t;s.style.left=(x+((y+60)/170%2?280:0))+'px';s.style.top=y+'px';c.appendChild(s);}}" +
                "document.body.appendChild(c);" +
                "var a=document.createElement('div');a.id='aviso';a.textContent=av;document.body.appendChild(a);});" +
                "})();</script>";
            return html.Replace("</head>", bloque + "</head>");
        }
    }
}
