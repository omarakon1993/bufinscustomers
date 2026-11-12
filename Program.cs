using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using bufinscustomers;
using bufinscustomers.Filters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Server.IIS;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

const long LimiteSubida = 104857600;   // 100 MB: igual que maxRequestLength / maxAllowedContentLength del sitio anterior

var builder = WebApplication.CreateBuilder(args);

// Credenciales locales (cadena de conexion, clave de maquina): secretsSettings.json esta en .gitignore. En produccion se usan variables de entorno o este mismo archivo en el servidor.
builder.Configuration.AddJsonFile("secretsSettings.json", optional: true, reloadOnChange: true);

AppPaths.Iniciar(builder.Environment.ContentRootPath);
bufinscustomers.Compat.ConfigurationManager.Iniciar(builder.Configuration);
bufinscustomers.Compat.MachineKey.Iniciar(builder.Configuration);
Bundles.Configurar(builder.Environment);

builder.WebHost.ConfigureKestrel(o => { o.AddServerHeader = false; o.Limits.MaxRequestBodySize = LimiteSubida; o.AllowSynchronousIO = true; });
builder.Services.Configure<IISServerOptions>(o => { o.MaxRequestBodySize = LimiteSubida; o.AllowSynchronousIO = true; });
builder.Services.Configure<FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = LimiteSubida;
    o.ValueLengthLimit = int.MaxValue;        // campos con JSON grande (p. ej. historial del chat de IA, cambios masivos de supuestos)
    o.ValueCountLimit = 100000;                // formularios con muchas filas (permisos, columnas)
    o.KeyLengthLimit = int.MaxValue;
});

builder.Services.AddHttpContextAccessor();

// Cookies: el sitio anterior marcaba todas como seguras (httpCookies requireSSL="true"). SameAsRequest las marca Secure en toda peticion
// HTTPS (la unica que llega a produccion) y no lanza error si alguna peticion de prueba llega en claro (con Always, el antiforgery fallaria).
var cookieSegura = CookieSecurePolicy.SameAsRequest;
builder.Services.AddAntiforgery(o => { o.Cookie.SecurePolicy = cookieSegura; });
builder.Services.Configure<Microsoft.AspNetCore.Mvc.CookieTempDataProviderOptions>(o => { o.Cookie.SecurePolicy = cookieSegura; });

// Las llaves de proteccion de datos (antiforgery, TempData) se guardan en disco: equivalen a la <machineKey> fija del sitio anterior
// y evitan que un reciclaje del sitio invalide los formularios abiertos. App_Data\dpkeys NO viaja con la publicacion.
var dp = builder.Services.AddDataProtection().SetApplicationName("bufinscustomers");
// Solo se guardan las llaves en disco si la carpeta es realmente escribible (en el hosting de Conexcol la identidad del sitio puede no
// tener permiso para crear archivos en App_Data\dpkeys). Si no lo es, las llaves viven en memoria: un reinicio del sitio invalida los
// formularios abiertos (la sesion tambien se pierde), pero la aplicacion no cae.
// Orden de preferencia: 1) SQL Server (dbo.DataProtectionKeys, script Sql/045; llaves cifradas con la MachineKey), 2) disco, 3) memoria.
var cadenaBd = bufinscustomers.Compat.ConfigurationManager.ConnectionStrings["DefaultConnection"]?.ConnectionString;
if (!string.IsNullOrEmpty(cadenaBd) && bufinscustomers.Infra.DataProtectionSqlRepository.Disponible(cadenaBd))
{
    dp.AddKeyManagementOptions(o =>
    {
        o.XmlRepository = new bufinscustomers.Infra.DataProtectionSqlRepository(cadenaBd);
        o.XmlEncryptor = new bufinscustomers.Infra.MachineKeyXmlEncryptor();
    });
    bufinscustomers.Helpers.AppLogger.Info("DataProtection: llaves en SQL Server (dbo.DataProtectionKeys).");
}
else
{
    var carpetaLlaves = AppPaths.Map("~/App_Data/dpkeys");
    try
    {
        Directory.CreateDirectory(carpetaLlaves);
        var prueba = Path.Combine(carpetaLlaves, Guid.NewGuid().ToString("N") + ".prueba");
        File.WriteAllText(prueba, "ok");
        File.Delete(prueba);
        dp.PersistKeysToFileSystem(new DirectoryInfo(carpetaLlaves));
    }
    catch (Exception ex)
    {
        bufinscustomers.Helpers.AppLogger.Warn("DataProtection: la carpeta de llaves no es escribible (" + ex.GetType().Name + "); se usan llaves en memoria.");
    }
}

builder.Services.AddControllersWithViews(o =>
{
    // Equivale a FilterConfig.RegisterGlobalFilters del sitio anterior (mismo orden)
    o.Filters.Add(new LoggingHandleErrorAttribute());
    o.Filters.Add(new EmpresasViewBagFilter());
    o.Filters.Add(new EscenariosViewBagFilter());
    o.Filters.Add(new CambioClaveObligatorioFilter());
    o.Filters.Add(new RegistroNavegacionFilter());
    o.Filters.Add(new AntiforgeryFallidoFilter());   // token anti-falsificación inválido -> login/página de origen, no «500»
    o.MaxModelBindingCollectionSize = 100000;
})
.AddNewtonsoftJson(o =>
{
    // Mismo formato que JavaScriptSerializer (MVC 5): nombres de propiedad tal cual (sin camelCase) y fechas "/Date(ms)/"
    o.SerializerSettings.ContractResolver = new DefaultContractResolver();
    o.SerializerSettings.DateFormatHandling = DateFormatHandling.MicrosoftDateFormat;
    o.SerializerSettings.DateTimeZoneHandling = DateTimeZoneHandling.Utc;
    o.SerializerSettings.StringEscapeHandling = StringEscapeHandling.EscapeHtml;   // como JavaScriptSerializer: escapa < > & '
});

builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<BrotliCompressionProvider>();
    o.Providers.Add<GzipCompressionProvider>();
});

var app = builder.Build();

AppHttp.Iniciar(app.Services.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>());

// Reemplaza a Global.asax Session_End: al expirar o abandonarse una sesion se descartan los cargues que dejo en revision.
// Además se borra su respaldo en dbo.SesionesActivas (la sesión ya no debe poder reconstruirse tras un reinicio).
SesionStore.SesionTerminada = datos =>
{
    bufinscustomers.Helpers.CargueLoteSesionHelper.DescartarAbiertos(datos);
    new bufinscustomers.Services.SesionPersistenteService().Eliminar(datos.Id);
};

if (app.Environment.IsDevelopment()) app.UseDeveloperExceptionPage();
else app.UseExceptionHandler("/Error");

// Red de seguridad (antes Global.asax Application_Error): registra cualquier excepcion que llegue hasta aqui.
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (Exception ex) { bufinscustomers.Helpers.AppLogger.Error(ex, "Application_Error"); throw; }
});

// Paginas de error de la marca para 403 y 404 (antes customErrors + httpErrors)
app.UseStatusCodePagesWithReExecute("/Error/Status", "?code={0}");

app.UseResponseCompression();
app.UseMiddleware<CabecerasSeguridad>();
app.UsarBundles();

// Estaticos: viven en /Assets (no en wwwroot). Como en IIS (clientCache): cache del navegador de 1 ano; las URL llevan ?v= (AssetVersion).
var assets = Path.Combine(app.Environment.ContentRootPath, "Assets");
if (Directory.Exists(assets))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(assets),
        RequestPath = "/Assets",
        OnPrepareResponse = ctx => ctx.Context.Response.Headers["Cache-Control"] = "public, max-age=31536000"
    });
}
var favicon = Path.Combine(app.Environment.ContentRootPath, "favicon.ico");
app.MapGet("/favicon.ico", (HttpContext ctx) =>
{
    ctx.Response.Headers["Cache-Control"] = "public, max-age=31536000";
    return Results.File(favicon, "image/x-icon");
});

// Idioma: cookie "lang" con "es-CO" o "en-US" (la escribe Acceso/SetLanguage). Por defecto es-CO; no se usa Accept-Language.
var idiomas = new[] { new CultureInfo("es-CO"), new CultureInfo("en-US") };
var localizacion = new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("es-CO"),
    SupportedCultures = idiomas,
    SupportedUICultures = idiomas
};
localizacion.RequestCultureProviders.Clear();
localizacion.RequestCultureProviders.Add(new CustomRequestCultureProvider(ctx =>
{
    var v = ctx.Request.Cookies["lang"];
    return Task.FromResult(v == "es-CO" || v == "en-US" ? new ProviderCultureResult(v) : null);
}));
app.UseRequestLocalization(localizacion);

app.UseRouting();

app.MapControllerRoute("Default", "{controller=Home}/{action=Index}/{id?}");

app.Run();
