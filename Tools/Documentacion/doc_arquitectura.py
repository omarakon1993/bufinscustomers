# -*- coding: utf-8 -*-
"""Guía de arquitectura y código de BUFINS (cómo está construido el sitio y cómo aprender C#/.NET con él). Solo español."""
import html
from doc_lib import *

FECHA = '2026-11-16'


def codigo(texto, leyenda=''):
    """Bloque de código con la leyenda del archivo de origen."""
    pie = '<figcaption>%s</figcaption>' % leyenda if leyenda else ''
    return '<figure><pre><code>%s</code></pre>%s</figure>' % (html.escape(texto.strip('\n')), pie)


def nodos(*items):
    """Diagrama de flujo horizontal (clase .flow / .node de la documentación técnica)."""
    return '<div class="flow">' + ''.join('<div class="node">%s</div>' % i for i in items) + '</div>'


def pasos(*items):
    return '<ol class="steps">' + ''.join('<li>%s</li>' % i for i in items) + '</ol>'


def ficha(titulo, etiqueta, *parrafos):
    return '<div class="ficha"><h4>%s<span class="fam">%s</span></h4>%s</div>' % (titulo, etiqueta, ''.join('<p>%s</p>' % p for p in parrafos))


def doc_arquitectura():
    s = []

    # 1 ----------------------------------------------------------------------------------------------------------------
    s.append(('intro', 'Cómo usar esta guía', seccion('intro', 1, 'Cómo usar esta guía',
        '<p class="lead">Esta guía explica <b>cómo está construido BUFINS por dentro</b>: qué carpeta hace qué, por dónde pasa una petición, cómo se guarda un dato y por qué el código está escrito así. '
        'Está pensada para leerla con el proyecto abierto: cada explicación señala un archivo real y muchos fragmentos están copiados tal cual del código.</p>'
        + dos_tarjetas('Si quieres entender el sitio', [
            'Lee las secciones 2 a 5 (mapa, carpetas, recorrido de una petición y capas).',
            'Sigue con 6 a 9 (datos, dependencias, seguridad y cargue) cuando necesites saber cómo se protege o se guarda algo.'],
            'Si quieres aprender C# y .NET', [
            'Ve directo a la sección 14 (glosario con ejemplos del propio código) y a la 15 (ruta de lectura de archivos).',
            'La sección 13 es una receta para practicar: agregar una pantalla nueva de principio a fin.'])
        + '<p class="note">Fecha de esta guía: <b>%s</b>. Describe el sitio después de la reingeniería posterior a la migración a .NET 10 (fases 0 a 9 de <code>.claude/notas/refactor-plan.md</code>). '
          'Si cambia la estructura, se actualiza aquí y en <code>Tools/Documentacion/doc_arquitectura.py</code>.</p>' % FECHA)))

    # 2 ----------------------------------------------------------------------------------------------------------------
    s.append(('mapa', 'Mapa del sistema', seccion('mapa', 2, 'Mapa del sistema',
        '<p class="lead">BUFINS Customers es una aplicación <b>ASP.NET Core MVC</b> sobre <b>.NET 10</b>. MVC significa que el trabajo se reparte en tres piezas: '
        '<b>M</b>odelo (los datos), <b>V</b>ista (la pantalla HTML) y <b>C</b>ontrolador (quien recibe la petición y decide qué hacer).</p>'
        + nodos('<b>Navegador</b><br>el usuario hace clic', '<b>IIS</b><br>aloja el sitio', '<b>Program.cs</b><br>middlewares y filtros',
                '<b>Controlador</b><br>recibe y valida', '<b>Servicio</b><br>reglas de negocio', '<b>SQL Server</b><br>procedimientos y tablas')
        + '<p>La respuesta vuelve por el mismo camino: el servicio devuelve datos, el controlador los entrega a una <b>vista Razor</b> (que genera el HTML) o los convierte en JSON para que el JavaScript de la página los pinte.</p>'
        + '<h3>Las capas y quién puede hablar con quién</h3>'
        + tabla(['Capa', 'Carpeta', 'Responsabilidad', 'No debe hacer'], [
            ['Presentación', '<code>Views/</code>, <code>Assets/</code>', 'HTML (Razor), CSS y JavaScript de cada pantalla.', 'Consultar la base de datos ni decidir permisos.'],
            ['Entrada HTTP', '<code>Controllers/</code>, <code>Api/</code>', 'Recibir la petición, validar el acceso, llamar al servicio y elegir la respuesta.', 'Contener reglas de negocio largas ni SQL.'],
            ['Negocio', '<code>Services/</code>', 'Reglas, cálculos, acceso a datos y llamadas a SQL Server, correo e IA.', 'Conocer la sesión HTTP o las vistas (el orquestador del cargue no usa HTTP).'],
            ['Datos compartidos', '<code>Models/</code>', 'Clases simples que viajan entre capas (Empresa, Usuario, ReporteViewModel…).', 'Tener lógica pesada.'],
            ['Utilidades', '<code>Helpers/</code>', 'Funciones reutilizables sin pantalla: <code>Bd</code>, sesión, permisos de empresa, correo, Excel.', 'Depender de un controlador concreto.'],
            ['Transversal', '<code>Filters/</code>, <code>Permisos/</code>, <code>Infra/</code>', 'Reglas que se aplican a muchas acciones: sesión, permisos, cabeceras de seguridad, paquetes de CSS/JS.', 'Contener lógica de una pantalla.'],
            ['Compatibilidad', '<code>Compat/</code>', 'Puente con APIs de la versión anterior (sesión, caché, configuración).', 'Crecer: el código nuevo usa las APIs de ASP.NET Core directamente.']])
        + '<p class="note">Regla de oro de la arquitectura: <b>el controlador es delgado y el servicio es el que sabe</b>. Si te encuentras escribiendo una regla de negocio dentro de un controlador, probablemente va en un servicio.</p>')))

    # 3 ----------------------------------------------------------------------------------------------------------------
    s.append(('carpetas', 'Carpetas', seccion('carpetas', 3, 'Estructura de carpetas',
        '<p class="lead">Cifras reales a la fecha de la guía (archivos de código del sitio, sin <code>bin/</code> ni <code>obj/</code>).</p>'
        + tabla(['Carpeta', 'Qué contiene', 'Tamaño aproximado', 'Un archivo para empezar'], [
            ['<code>Program.cs</code>', 'Arranque: configuración, servicios, orden de los middlewares y rutas.', '190 líneas', '<code>Program.cs</code>'],
            ['<code>Controllers/</code>', 'Un controlador por pantalla o módulo (60 clases, 66 archivos por las partes).', '17.700 líneas', '<code>GestorEscenariosController.cs</code> (el más simple y completo)'],
            ['<code>Services/</code>', 'Negocio y datos (unos 70 servicios).', '22.600 líneas', '<code>EscenarioService.cs</code>'],
            ['<code>Models/</code>', 'Clases de datos y modelos de vista.', '3.500 líneas', '<code>Escenario.cs</code>'],
            ['<code>Helpers/</code>', 'Utilidades: acceso a BD (<code>Bd</code>), sesión, correo, Excel, seguridad.', '3.700 líneas', '<code>Bd.cs</code>'],
            ['<code>Filters/</code> y <code>Permisos/</code>', 'Filtros globales y atributos <code>[ValidarSesion]</code>, <code>[RequierePermiso]</code>, <code>[SoloSuperAdmin]</code>.', '410 líneas', '<code>ValidarSesionAttribute.cs</code>'],
            ['<code>Infra/</code>', 'Cabeceras de seguridad (CSP), paquetes CSS/JS, llaves de protección de datos, registro de servicios.', '780 líneas', '<code>ServiciosExtensions.cs</code>'],
            ['<code>Compat/</code>', 'Sesión en memoria, caché, configuración y cookies al estilo de la versión anterior.', '710 líneas', '<code>Sesion.cs</code>'],
            ['<code>Api/</code>', 'API de ingesta <code>/api/v1</code> para que otros sistemas envíen datos.', '800 líneas', '<code>ApiV1.cs</code>'],
            ['<code>Views/</code>', 'Pantallas Razor agrupadas por área funcional (Acceso, Configuracion, Datos, Informes, Seguridad…) y plantillas parciales en <code>Shared/</code>.', '89 vistas, 42.000 líneas', '<code>Views/Shared/_Layout.cshtml</code>'],
            ['<code>Assets/</code>', 'CSS, JavaScript, fuentes, plantillas de Excel. Se sirve en la ruta <code>/Assets</code> (no en wwwroot).', '25 JS, 39 CSS', '<code>Assets/css/bufins-components.css</code>'],
            ['<code>App_GlobalResources/</code>', 'Textos de la interfaz en español e inglés (<code>Strings.resx</code>, <code>Strings.en-US.resx</code>).', '3.800 textos por idioma', '<code>Strings.resx</code>'],
            ['<code>App_Data/Documentacion/</code>', 'La documentación oficial (HTML de solo lectura).', '—', 'Esta guía'],
            ['<code>Tests/</code>', 'Pruebas automáticas (xUnit), 77 en total.', '7 archivos', '<code>TotpHelperTests.cs</code>'],
            ['<code>Tools/</code>', 'Utilidades de despliegue y generadores de documentación (no forman parte del sitio).', '—', '<code>Tools/Despliegue/</code>'],
            ['<code>.claude/notas/</code>', 'Notas internas por módulo para quien mantiene el código con Claude.', '—', '<code>arquitectura-acceso.md</code>']])
        + '<p>Las vistas se agrupan por <b>área funcional</b> y no por controlador: un controlador devuelve su vista con una ruta explícita, por ejemplo '
          '<code>View("~/Views/Configuracion/GestorEscenarios.cshtml", escenarios)</code>.</p>')))

    # 4 ----------------------------------------------------------------------------------------------------------------
    s.append(('peticion', 'Una petición', seccion('peticion', 4, 'El recorrido de una petición',
        '<p class="lead">Cuando alguien abre <code>/Auditoria</code> ocurre esto, en este orden. Entender este recorrido explica casi todo el comportamiento del sitio.</p>'
        + pasos(
            '<b>Entrada.</b> IIS entrega la petición a la aplicación .NET 10 (alojamiento <i>in-process</i>).',
            '<b>Manejo de errores</b> (<code>UseExceptionHandler</code>) y la red de seguridad que registra en el log cualquier excepción que llegue hasta ahí.',
            '<b>Páginas de error de la marca</b> para 403 y 404 (no se aplican a <code>/api</code>, que responde siempre JSON).',
            '<b>Compresión</b> Brotli/Gzip y el middleware <code>CabecerasSeguridad</code>, que agrega la política de seguridad de contenido (CSP) con un <i>nonce</i> distinto por petición.',
            '<b>Archivos estáticos</b>: lo que empieza por <code>/Assets</code> se sirve desde disco con caché de un año (las URL llevan <code>?v=</code> para renovarla).',
            '<b>Idioma</b>: la cookie <code>lang</code> decide es-CO o en-US.',
            '<b>Enrutamiento</b>: <code>/Auditoria</code> se resuelve a <code>AuditoriaController.Index</code> con la ruta <code>{controller}/{action}/{id?}</code>. Si empieza por <code>/api/v1</code> lo toma antes el middleware de la API.',
            '<b>Filtros globales</b>, en este orden: registro de errores, carga de empresas y escenarios para la vista, cambio de clave obligatorio, registro de navegación y manejo del token anti-falsificación.',
            '<b>Filtros de la acción</b>: <code>[ValidarSesion]</code> (¿hay usuario?) y <code>[RequierePermiso("…")]</code> (¿tiene el permiso?). Si fallan, devuelven login o 403 y la acción ni se ejecuta.',
            '<b>El controlador</b> ejecuta su método, llama a uno o varios <b>servicios</b>, y estos usan <code>Bd</code> para llamar a SQL Server.',
            '<b>La respuesta</b>: una vista Razor (HTML), un <code>Json(...)</code> o un archivo (Excel, PDF).')
        + '<h3>Dónde se decide cada cosa</h3>'
        + tabla(['Pregunta', 'Quién la responde', 'Archivo'], [
            ['¿Hay sesión iniciada?', 'Atributo <code>[ValidarSesion]</code>', '<code>Permisos/ValidarSesionAttribute.cs</code>'],
            ['¿Puede ver esta opción del menú?', 'Atributo <code>[RequierePermiso("CODIGO")]</code>', '<code>Permisos/RequierePermisoAttribute.cs</code>'],
            ['¿Puede ver los datos de esta empresa?', '<code>EmpresaAccesoHelper.TieneAcceso(...)</code> (la propia empresa o su grupo)', '<code>Helpers/EmpresaAccesoHelper.cs</code>'],
            ['¿Qué idioma?', 'Cookie <code>lang</code> + <code>R("Clave")</code> en el controlador', '<code>Controllers/BaseController.cs</code>'],
            ['¿Qué cabeceras de seguridad lleva la respuesta?', 'Middleware <code>CabecerasSeguridad</code>', '<code>Infra/CabecerasSeguridad.cs</code>']]))))

    # 5 ----------------------------------------------------------------------------------------------------------------
    s.append(('capas', 'Las capas', seccion('capas', 5, 'Las capas con un ejemplo real',
        '<p class="lead">El módulo más pequeño y completo es el de <b>Escenarios</b>. Sirve para ver las tres capas juntas.</p>'
        + '<h3>Modelo: la forma de los datos</h3>'
        + codigo('''
public class Escenario
{
    public int Id { get; set; }
    public string Nombre { get; set; }
    public int Orden { get; set; }
    public bool Activo { get; set; }
}''', 'Models/Escenario.cs: una clase con propiedades; no tiene lógica.')
        + '<h3>Servicio: lo que sabe el negocio y habla con la base de datos</h3>'
        + codigo('''
public bool Desactivar(int id)
{
    return Bd.Ejecutar(CadenaConexion,
        "UPDATE dbo.Escenarios SET Activo = 0 WHERE Id = @Id", new { Id = id }) > 0;
}''', 'Services/EscenarioService.cs: «desactivar» nunca borra, porque otras tablas dependen del escenario.')
        + '<h3>Controlador: recibe, valida, llama y responde</h3>'
        + codigo('''
[ValidarSesion]
[SoloSuperAdmin]
public class GestorEscenariosController(
    EscenarioService escenarioService,
    AuditoriaService auditoriaService) : BaseController
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    public ActionResult Crear(string nombre)
    {
        ...
        bool ok = escenarioService.Crear(nombre);
        if (ok)
        {
            SetSuccessMessage(string.Format(R("GestorEscenarios_Creado"), nombre.Trim()));
            auditoriaService.RegistrarCambio(AuditoriaTipo.Escenarios, AuditoriaAccion.Crear,
                "Escenarios", null, $"Escenario creado: {nombre.Trim()}", null, new { nombre = nombre.Trim() });
        }
        ...
        return RedirectToAction("Index");
    }
}''', 'Controllers/GestorEscenariosController.cs (abreviado). Observa el patrón: atributos de seguridad, servicio inyectado, mensaje traducido con R(...), auditoría tras el éxito.')
        + '<h3>Vista: lo que ve el usuario</h3>'
        + '<p>El controlador termina con <code>View("~/Views/Configuracion/GestorEscenarios.cshtml", escenarios)</code>. Una vista Razor es HTML con partes de C# que empiezan por <code>@</code>. '
          'Los textos nunca van escritos a mano: se leen del archivo de recursos con <code>@Resources.Strings.Clave</code>, de modo que existen en español e inglés.</p>'
        + '<div class="g2">'
        + '<div class="card"><b>Reglas de cada capa</b><ul>'
          '<li>Un controlador nuevo hereda de <code>BaseController</code> y lleva <code>[ValidarSesion]</code> si requiere usuario.</li>'
          '<li>Un servicio nuevo hereda de <code>BaseService</code>, que ya trae <code>CadenaConexion</code>.</li>'
          '<li>Los errores al navegador salen con <code>MensajeError(ex)</code>, que registra el detalle y muestra un código de referencia.</li>'
          '<li>Los textos van siempre en los dos archivos <code>.resx</code>.</li></ul></div>'
        + '<div class="card"><b>Reglas que protegen los datos</b><ul>'
          '<li>Toda acción acotada por empresa usa <code>EmpresaAccesoHelper</code>, nunca una comparación directa de ids.</li>'
          '<li>Toda acción que cambia datos se audita con <code>RegistrarCambio</code>.</li>'
          '<li>Las consultas son siempre parametrizadas (nada de concatenar valores del usuario en el SQL).</li>'
          '<li>El SQL debe funcionar en SQL Server 2012.</li></ul></div></div>')))

    # 6 ----------------------------------------------------------------------------------------------------------------
    s.append(('datos', 'Datos', seccion('datos', 6, 'Acceso a datos',
        '<p class="lead">BUFINS usa <b>ADO.NET</b> directo con <b>procedimientos almacenados</b> en SQL Server; no usa un ORM como Entity Framework. Es más verboso pero predecible y muy rápido.</p>'
        + '<h3>El ayudante <code>Bd</code></h3>'
        + '<p>Hasta la reingeniería, cada consulta repetía unas ocho líneas (abrir conexión, crear comando, agregar parámetros, ejecutar). Ahora <code>Helpers/Bd.cs</code> lo hace una vez:</p>'
        + codigo('''
// Antes (8 líneas)
using (var cn = new SqlConnection(CadenaConexion))
{
    var cmd = new SqlCommand("DELETE FROM ConfiguracionSistema WHERE Clave = @Clave", cn);
    cmd.Parameters.AddWithValue("@Clave", clave.Trim());
    cn.Open();
    return cmd.ExecuteNonQuery() > 0;
}

// Ahora (1 línea)
return Bd.Ejecutar(CadenaConexion, "DELETE FROM ConfiguracionSistema WHERE Clave = @Clave", new { Clave = clave.Trim() }) > 0;''',
                 'Services/ConfiguracionSistemaService.cs: el mismo comportamiento, sin la repetición.')
        + tabla(['Método de <code>Bd</code>', 'Para qué', 'Ejemplo de uso'], [
            ['<code>Ejecutar</code>', 'INSERT, UPDATE, DELETE o un procedimiento; devuelve las filas afectadas.', '<code>Bd.Ejecutar(cadena, sql, new { Id = id })</code>'],
            ['<code>Escalar&lt;T&gt;</code>', 'Un solo valor (un COUNT, un id nuevo…).', '<code>Bd.Escalar&lt;int&gt;(cadena, "SELECT COUNT(*) FROM …")</code>'],
            ['<code>Lista&lt;T&gt;</code>', 'Todas las filas convertidas a objetos.', '<code>Bd.Lista(cadena, sql, r =&gt; new Escenario { … })</code>'],
            ['<code>Leer</code> y <code>LeerPrimera</code>', 'Recorrer las filas (o solo la primera) con tu propio código.', '<code>Bd.Leer(cadena, sql, r =&gt; ids.Add(Convert.ToInt32(r["Id"])))</code>'],
            ['<code>Primero&lt;T&gt;</code>', 'La primera fila convertida, o nulo.', '<code>Bd.Primero(cadena, sql, parametros, r =&gt; …)</code>']])
        + '<p class="note">El acceso manual (<code>SqlConnection</code> + <code>SqlCommand</code>) se sigue usando donde <code>Bd</code> no llega: transacciones, parámetros OUTPUT, varios conjuntos de resultados y carga masiva (<code>SqlBulkCopy</code>).</p>'
        + '<h3>Ideas que conviene recordar</h3>'
        + '<ul><li><b>Multiempresa:</b> casi toda tabla de datos tiene <code>IdEmpresa</code>; el código nunca lee datos sin filtrarlo.</li>'
          '<li><b>Escenarios:</b> las tablas de datos cargados tienen <code>IdEscenario</code> (por defecto 1 = principal): una copia paralela de los datos por empresa. Las lecturas usan <code>ISNULL(IdEscenario,1) = @IdEscenario</code>.</li>'
          '<li><b>Columnas nuevas incrementales:</b> si una columna puede no existir todavía en algún entorno, se lee dentro de un <code>try/catch (IndexOutOfRangeException)</code> y el código sigue funcionando sin ella.</li>'
          '<li><b>Scripts SQL manuales:</b> los cambios de base de datos se aplican a mano en cada entorno; no se debe asumir que un script ya corrió.</li>'
          '<li><b>Modelos de cálculo:</b> los informes leen tablas ya calculadas (<code>dbo.Modelo*</code>); nunca ejecutan los procedimientos de cálculo en vivo.</li></ul>')))

    # 7 ----------------------------------------------------------------------------------------------------------------
    s.append(('di', 'Dependencias', seccion('di', 7, 'Inyección de dependencias',
        '<p class="lead">Un controlador necesita servicios para trabajar. En vez de crearlos él mismo con <code>new</code>, los <b>recibe</b> al construirse: eso se llama inyección de dependencias.</p>'
        + codigo('''
// Antes
public class GestorEscenariosController : BaseController
{
    private readonly EscenarioService _svc = new EscenarioService();
    ...
}

// Ahora: constructor primario (C# 12). Las dependencias son visibles en la cabecera de la clase
public class GestorEscenariosController(
    EscenarioService escenarioService,
    AuditoriaService auditoriaService) : BaseController
{
    // dentro de los métodos se usa directamente: escenarioService.Crear(nombre)
}''', 'Controllers/GestorEscenariosController.cs')
        + pasos('Al arrancar, <code>Program.cs</code> llama a <code>AddServiciosBufins()</code> (archivo <code>Infra/ServiciosExtensions.cs</code>).',
                'Esa función busca por reflexión todas las clases públicas que terminan en <code>Service</code> dentro de <code>bufinscustomers.Services</code> y las registra como <b>transitorias</b> (una instancia nueva cada vez que se piden).',
                'ASP.NET Core, al crear un controlador, mira los parámetros de su constructor y entrega cada servicio.',
                'La prueba <code>InyeccionDependenciasTests</code> revisa que <b>todo</b> parámetro de <b>todo</b> controlador sea resoluble; si agregas uno que no está registrado, falla en las pruebas y no en producción.')
        + '<p class="note">Los servicios, ayudantes y filtros estáticos siguen creando otros servicios con <code>new</code>. Convertirlos a inyección sería un cambio mucho mayor sin beneficio inmediato; el código nuevo en controladores sí usa el constructor.</p>')))

    # 8 ----------------------------------------------------------------------------------------------------------------
    s.append(('seguridad', 'Seguridad', seccion('seguridad', 8, 'Sesión, usuarios y permisos',
        '<p class="lead">La seguridad vive en capas, de la más externa a la más interna. Esta sección es un mapa; el detalle de controles está en <b>Ciberseguridad BUFINS</b> y <b>Seguridad: blindaje del inicio de sesión</b> (biblioteca de documentación).</p>'
        + tabla(['Capa', 'Qué hace', 'Dónde'], [
            ['Cabeceras y CSP', 'Política estricta de scripts: solo se ejecutan los que llevan el <i>nonce</i> de la petición. Por eso están prohibidos <code>onclick=""</code> y similares en HTML.', '<code>Infra/CabecerasSeguridad.cs</code>, <code>Infra/CspTagHelpers.cs</code>'],
            ['Inicio de sesión', 'Contraseñas con BCrypt (costo 12), límite de intentos por IP y por usuario, bloqueo de cuenta, doble factor (TOTP) y llaves de acceso (passkeys).', '<code>Controllers/AccesoController.*.cs</code>, <code>Services/RateLimitLoginService.cs</code>'],
            ['Sesión', 'Cookie <code>BUFINS_SessionId</code>, 20 minutos de inactividad, guardada en memoria del servidor (un reinicio la cierra).', '<code>Compat/Sesion.cs</code>, <code>Helpers/UsuarioSesionHelper.cs</code>'],
            ['Roles', 'Usuario normal (0), Admin de empresa (1) y Super Admin (2).', '<code>UsuarioSesionHelper.EsSuperAdmin()</code>…'],
            ['Permisos de menú', 'Cada opción tiene un código; se asigna por usuario. Hay opciones solo para Super Admin y menús personalizados por empresa.', '<code>Permisos/RequierePermisoAttribute.cs</code>, <code>Services/PermisosService.cs</code>'],
            ['Aislamiento por empresa', 'Un usuario solo ve su empresa y las de su grupo empresarial.', '<code>Helpers/EmpresaAccesoHelper.cs</code>'],
            ['Anti-falsificación', 'Todo POST lleva token (<code>[ValidateAntiForgeryToken]</code>).', 'En cada acción que cambia datos'],
            ['Auditoría', 'Quién cambió qué y cuándo, y los intentos de acceso.', '<code>Services/AuditoriaService.cs</code>']])
        + '<h3>Cómo se ve en el código</h3>'
        + codigo('''
// Aislamiento por empresa: si no es Super Admin, solo su empresa o las de su grupo
public static bool TieneAcceso(Usuarios usuario, int idEmpresa)
{
    if (usuario == null) return false;
    if (UsuarioSesionHelper.EsSuperAdmin()) return true;
    if (usuario.IdEmpresa == idEmpresa) return true;
    var idGrupo = ObtenerIdGrupoDeEmpresa(usuario.IdEmpresa);
    if (idGrupo == null) return false;
    var empresaObjetivo = EmpresaCacheHelper.ObtenerEmpresasCacheadas().FirstOrDefault(e => e.Id == idEmpresa);
    return empresaObjetivo != null && empresaObjetivo.IdGrupoEmpresarial == idGrupo;
}''', 'Helpers/EmpresaAccesoHelper.cs (con los comentarios quitados)')
        + codigo('''
[HttpPost]
[ValidateAntiForgeryToken]
public JsonResult ConsultarReporte(FiltrosPYG filtros)
{
    if (filtros == null || !TieneAccesoEmpresa(filtros.IdEmpresa))
        return Json(new { success = false, message = R("Common_SinPermisos") });

    var reporte = service.ConstruirReporte(filtros);
    ...
}''', 'Controllers/InformePYGController.cs (abreviado). Los informes están habilitados para todos los roles (la clase lleva [ValidarSesion]) y el acceso real se acota por empresa.'))))

    # 9 ----------------------------------------------------------------------------------------------------------------
    s.append(('cargue', 'Cargue', seccion('cargue', 9, 'El cargue de datos (Excel y API)',
        '<p class="lead">Es el corazón del sistema: los clientes suben su información contable y ese dato alimenta todos los informes. Por eso tiene las protecciones más estrictas.</p>'
        + nodos('<b>Subir Excel</b><br>o enviar por API', '<b>Staging</b><br>se guarda aparte', '<b>Validación</b><br>reglas del procedimiento', '<b>Revisar</b><br>el usuario ve el impacto', '<b>Confirmar</b><br>una sola transacción')
        + '<ul><li><b>Dos pasos:</b> subir solo escribe en tablas temporales <code>Staging_Ini_*</code> y valida. Nada real cambia hasta confirmar.</li>'
          '<li><b>Todo en una transacción al confirmar:</b> copia de seguridad (snapshot) → borrados → carga definitiva → auditoría. Si algo falla, no queda nada a medias.</li>'
          '<li><b>Regla de oro:</b> el cargue nunca puede dejar un año vacío; hay un umbral que bloquea reducciones fuertes.</li>'
          '<li><b>Un lote activo por llave</b> (empresa, año, modo y escenario) y ningún lote puede quedar «pegado» (abandono, recarga, fin de sesión, limpieza).</li>'
          '<li><b>Historial:</b> se conservan versiones comprimidas para deshacer, con un respaldo automático antes de restaurar.</li></ul>'
        + '<h3>Dónde está el código</h3>'
        + tabla(['Archivo', 'Qué contiene'], [
            ['<code>Services/CargueOrquestadorService.cs</code>', 'El flujo: validar la solicitud, procesar el archivo, confirmar, descartar, abandonar.'],
            ['<code>…Service.Api.cs</code>', 'Lo que usa solo la API: iniciar lote, agregar filas por hoja, validar el lote.'],
            ['<code>…Service.LecturaExcel.cs</code>', 'Leer cada hoja de Excel a una tabla en memoria y sanear los números.'],
            ['<code>…Service.EscrituraSql.cs</code>', 'Escribir en SQL Server (carga masiva) y diagnosticar errores de carga.'],
            ['<code>…Service.Auditoria.cs</code>', 'Detalle de cambios y registro de auditoría del cargue.'],
            ['<code>Helpers/CargueConversion.cs</code>', 'Conversiones puras de cifras, fechas y sí/no (con 42 pruebas).'],
            ['<code>Controllers/DatosController.Cargue.cs</code>', 'Las acciones de la pantalla de Excel: solo traducen el resultado del orquestador.'],
            ['<code>Api/ApiV1.cs</code>', 'Las acciones de la API de ingesta.']])
        + '<p class="note"><b>Paridad Excel ↔ API:</b> el cargue por Excel y el de la API deben tener las mismas validaciones. Por eso toda regla va en el orquestador o en el procedimiento <code>sp_ValidarCargueStaging</code>, que sirven a las dos entradas. '
          'Lo que es propio de cada entrada (sesión y token en el Excel; clave, IP y límites en la API) se cambia en las dos.</p>')))

    # 10 ---------------------------------------------------------------------------------------------------------------
    s.append(('informes', 'Informes', seccion('informes', 10, 'Informes, modelos e inteligencia artificial',
        '<p class="lead">Los informes (PYG, Balance, Tesorería, Flujos, Indicadores…) comparten la misma forma. Aprender uno es aprender todos.</p>'
        + nodos('<b>Ejecutar modelos</b><br>calcula y guarda <code>Modelo*</code>', '<b>Servicio del informe</b><br>lee y arma el reporte', '<b>Controlador</b><br>filtros y JSON', '<b>Vista + JS</b><br>pinta tablas y gráficos', '<b>Exportar</b><br>Excel · PDF · imagen · correo')
        + '<ul><li><b>Los informes no calculan en vivo:</b> leen tablas materializadas (<code>dbo.Modelo*</code>) que llena la ejecución de modelos.</li>'
          '<li><b>Un servicio por informe</b> (<code>InformePYGService</code>, <code>InformeBalanceService</code>…) que devuelve un modelo de vista; el controlador solo valida acceso y lo entrega.</li>'
          '<li><b>Elementos comunes:</b> botón Exportar estándar (<code>_ExportMenu</code>), logo de la empresa, tema de colores, comentarios de gestión y documentación propia, siempre con las mismas piezas compartidas.</li>'
          '<li><b>El correo de un informe se reconstruye en el servidor</b>: nunca se confía en las cifras que manda el navegador.</li>'
          '<li><b>IA:</b> todo uso de inteligencia artificial pasa por <code>IAGateway</code>, que controla el acceso por empresa, mide el consumo y solo envía datos de las tablas <code>Modelo*</code>, nunca datos de detalle.</li></ul>')))

    # 11 ---------------------------------------------------------------------------------------------------------------
    s.append(('frontend', 'Interfaz', seccion('frontend', 11, 'La interfaz: Razor, CSS y JavaScript',
        '<ul><li><b>Razor (<code>.cshtml</code>):</b> HTML con C#. Cada pantalla vive en <code>Views/&lt;Área&gt;/</code>; el marco común está en <code>Views/Shared/_Layout.cshtml</code> y las piezas reutilizables empiezan por guion bajo (<code>_ExportMenu</code>, <code>_LoaderDots</code>…).</li>'
          '<li><b>Librerías:</b> Bootstrap 4.6 con el tema SB Admin 2, jQuery 3, Chart.js, jsPDF y html2canvas (PDF e imagen), SweetAlert2 (diálogos), Select2 y Font Awesome (iconos).</li>'
          '<li><b>Estilo único:</b> <code>bufins-components.css</code> define botones, tarjetas, tablas y modales; antes de crear una clase nueva se busca una existente.</li>'
          '<li><b>Idiomas:</b> ningún texto va escrito en la vista; se usa <code>@Resources.Strings.Clave</code>. Para JavaScript, el texto se inyecta desde Razor a una variable.</li>'
          '<li><b>CSP estricta:</b> no se permiten manejadores en línea (<code>onclick</code>…). Se usan <code>data-*</code> y listeners; <code>inline-handlers.js</code> traduce llamadas simples. Los <code>&lt;script&gt;</code> reciben el <i>nonce</i> automáticamente.</li>'
          '<li><b>Paquetes y versiones:</b> los enlaces a CSS y JS propios usan <code>AssetVersion.Url(...)</code>, que agrega <code>?v=</code> con la fecha del archivo para que el navegador recargue al publicar.</li></ul>'
        + codigo('''
<h1 class="powerbi-header-title">@Resources.Strings.CfgEmp_PageTitle</h1>
<link rel="stylesheet" href="@bufinscustomers.Helpers.AssetVersion.Url("~/Assets/css/tema-informe.css")">''',
                 'Views/Configuracion/ConfiguracionesEmpresas.cshtml: un texto traducido y un CSS con versión.'))))

    # 12 ---------------------------------------------------------------------------------------------------------------
    s.append(('pruebas', 'Pruebas', seccion('pruebas', 12, 'Pruebas y cómo verificar un cambio',
        '<p class="lead">Hay 77 pruebas automáticas en <code>Tests/Bufins.Pruebas</code> (xUnit). Prueban lógica pura y <b>nunca tocan la base de datos</b>.</p>'
        + tabla(['Qué se prueba', 'Archivo'], [
            ['Códigos TOTP (doble factor) con los vectores oficiales del estándar RFC 6238', '<code>TotpHelperTests.cs</code>'],
            ['Política de contraseñas', '<code>PoliticaContrasenaTests.cs</code>'],
            ['Validación y cálculo de colores del tema de informes', '<code>TemaInformeTests.cs</code>'],
            ['Conversión de cifras, fechas y sí/no del cargue de Excel', '<code>CargueConversionTests.cs</code>'],
            ['Conversión de parámetros de <code>Bd</code>', '<code>BdTests.cs</code>'],
            ['Que un error técnico nunca filtre su texto al usuario y una regla de negocio sí conserve el suyo', '<code>MensajeErrorHelperTests.cs</code>'],
            ['Que todo controlador se pueda construir con el contenedor de dependencias', '<code>InyeccionDependenciasTests.cs</code>']])
        + codigo('''
dotnet build bufinscustomers.csproj --no-incremental     # compila y muestra TODOS los avisos
dotnet test Tests/Bufins.Pruebas/Bufins.Pruebas.csproj   # corre las 77 pruebas
dotnet run                                               # https://localhost:44339''', 'Comandos del día a día (desde la carpeta del proyecto).')
        + '<p class="note"><b>Cuidado:</b> el entorno local usa la base de datos de <b>producción</b>. Cualquier POST (guardar, cargar, ejecutar modelos, enviar correos) es real. Para probar a mano se hacen solo lecturas, o se acuerda explícitamente cada prueba que escribe.</p>'
        + '<p>Una buena prueba de caracterización: ejemplo del estilo usado.</p>'
        + codigo('''
[Fact]
public void Codigo_CoincideConVectorRfc6238()
{
    // RFC 6238, T = 59 s -> paso 1 -> 94287082 (8 dígitos); los 6 últimos son 287082.
    Assert.Equal("287082", TotpHelper.Codigo(SecretoRfc, 1));
}''', 'Tests/Bufins.Pruebas/TotpHelperTests.cs: [Fact] marca una prueba; Assert.Equal compara lo esperado con lo real.'))))

    # 13 ---------------------------------------------------------------------------------------------------------------
    s.append(('receta', 'Receta', seccion('receta', 13, 'Receta: agregar una pantalla nueva',
        '<p class="lead">Esta es la lista que se sigue para cualquier pantalla nueva. Sirve también como ejercicio para aprender cómo encajan las piezas.</p>'
        + pasos(
            '<b>Textos:</b> agrega las claves en <code>Strings.resx</code> y <code>Strings.en-US.resx</code> (mismo nombre en ambos), con el formato <code>Area_Elemento</code>.',
            '<b>Modelo:</b> crea la clase de datos en <code>Models/</code> (solo propiedades).',
            '<b>Servicio:</b> crea <code>MiCosaService : BaseService</code> en <code>Services/</code> y usa <code>Bd.*</code> para leer y escribir. Si hay procedimiento almacenado, el script SQL se aplica a mano en cada entorno.',
            '<b>Controlador:</b> hereda de <code>BaseController</code>, lleva <code>[ValidarSesion]</code> y <code>[RequierePermiso("CODIGO")]</code> y recibe sus servicios por constructor primario. El servicio se registra solo.',
            '<b>Empresa:</b> si los datos son de una empresa, verifica con <code>EmpresaAccesoHelper</code> o <code>TieneAccesoEmpresa(id)</code>.',
            '<b>Vista:</b> crea la vista Razor en la carpeta de su área con el esqueleto estándar (cabecera, filtros, tabla) y las clases de <code>bufins-components.css</code>.',
            '<b>Menú:</b> agrega la opción en la tabla <code>MenuOpciones</code> para que aparezca en el menú lateral y en el gestor de permisos.',
            '<b>Auditoría:</b> si la acción cambia datos, llama a <code>RegistrarCambio</code> tras el éxito.',
            '<b>Notificación:</b> si la operación es larga o importante, crea una notificación interna con <code>NotificacionesService</code>.',
            '<b>Documentación:</b> si es una pantalla nueva, crea su documentación oficial y, si se quiere, agrégala a la biblioteca «Documentación BUFINS».',
            '<b>Verificar:</b> compila con <code>--no-incremental</code>, corre las pruebas y revisa la pantalla.')
        + '<p class="note">Si la pantalla es un <b>informe</b>, además lleva: la cápsula «Información modelo Bufins», el botón Exportar estándar, el logo de la empresa, el tema de colores y, si tiene cierre mensual, los comentarios de gestión.</p>')))

    # 14 ---------------------------------------------------------------------------------------------------------------
    gl = []
    def g(titulo, texto, cod, ley):
        gl.append(ficha(titulo, 'C# / .NET', texto) + codigo(cod, ley))
    g('Namespaces y <code>using</code>', 'Un <b>namespace</b> agrupa clases con un nombre que no se repite. <code>using</code> te deja escribir el nombre corto. Aquí cada carpeta tiene su namespace (<code>bufinscustomers.Services</code>, <code>bufinscustomers.Helpers</code>…).',
      'using bufinscustomers.Helpers;\n\nnamespace bufinscustomers.Services\n{\n    public class EscenarioService : BaseService { ... }\n}', 'Services/EscenarioService.cs')
    g('Clases y propiedades', 'Una <b>clase</b> es una plantilla de objetos. Una <b>propiedad</b> (<code>{ get; set; }</code>) es un dato con su lectura y escritura. Los modelos son clases con solo propiedades.',
      'public class Escenario\n{\n    public int Id { get; set; }\n    public string Nombre { get; set; }\n}', 'Models/Escenario.cs')
    g('Herencia', 'Una clase puede <b>heredar</b> de otra y reutilizar lo suyo. Todos los controladores heredan de <code>BaseController</code> (que trae <code>R(...)</code>, <code>MensajeError</code>, <code>TieneAccesoEmpresa</code>…) y todos los servicios de <code>BaseService</code> (que trae <code>CadenaConexion</code>).',
      'public class GestorEscenariosController(...) : BaseController', 'Los dos puntos significan «hereda de».')
    g('Constructor primario (C# 12)', 'Los parámetros de la clase se escriben en su cabecera y están disponibles en todos sus métodos. Reemplaza el campo + constructor + asignación de siempre.',
      'public class GestorEscenariosController(\n    EscenarioService escenarioService,\n    AuditoriaService auditoriaService) : BaseController', 'Controllers/GestorEscenariosController.cs')
    g('Atributos', 'Los corchetes colocan <b>metadatos</b> sobre una clase o método. Aquí controlan seguridad y comportamiento HTTP: el filtro se ejecuta antes de la acción.',
      '[ValidarSesion]\n[SoloSuperAdmin]\npublic class GestorEscenariosController(...) : BaseController\n{\n    [HttpPost]\n    [ValidateAntiForgeryToken]\n    public ActionResult Crear(string nombre) { ... }\n}', 'Controllers/GestorEscenariosController.cs. Un atributo es una clase que termina en «Attribute» (Permisos/RequierePermisoAttribute.cs).')
    g('Clases y métodos estáticos', 'Un miembro <code>static</code> pertenece a la clase y no a un objeto: se llama sin <code>new</code>. Los ayudantes del sitio son clases estáticas.',
      'if (UsuarioSesionHelper.EsSuperAdmin()) return true;', 'Helpers/EmpresaAccesoHelper.cs')
    g('<code>using</code> como bloque y <code>try/catch</code>', 'Un objeto que usa recursos (una conexión) se envuelve en <code>using</code>: al terminar el bloque se cierra solo. <code>try/catch</code> atrapa los errores para decidir qué hacer.',
      'using (var cn = new SqlConnection(cadena))\nusing (var cmd = Crear(cn, texto, parametros, esSp, timeoutSeg))\n{\n    cn.Open();\n    return cmd.ExecuteNonQuery();\n}', 'Helpers/Bd.cs')
    g('Genéricos', 'El tipo entre <code>&lt; &gt;</code> es un parámetro: el mismo código sirve para varios tipos. <code>Escalar&lt;int&gt;</code> devuelve un entero; <code>Escalar&lt;string&gt;</code>, un texto.',
      'public static T? Escalar<T>(string cadena, string texto, object? parametros = null, ...)\n\nint total = Bd.Escalar<int>(cadena, "SELECT COUNT(*) FROM Usuarios");', 'Helpers/Bd.cs')
    g('Lambdas y delegados', 'Una <b>lambda</b> (<code>x =&gt; …</code>) es una función corta que se pasa como dato. <code>Bd.Leer</code> recibe una función que ejecuta por cada fila.',
      'var ids = new List<int>();\nBd.Leer(CadenaConexion, "SELECT Id FROM dbo.Usuarios WHERE Admin = 2",\n    r => ids.Add(Convert.ToInt32(r["Id"])));', 'Services/IntegracionesService.cs')
    g('LINQ', 'Consultas sobre colecciones con métodos encadenados: <code>Where</code> (filtrar), <code>Select</code> (transformar), <code>Any</code> (¿alguno cumple?), <code>ToList</code> (materializar).',
      'CodigosPermiso = (codigoPermiso ?? "")\n    .Split(new[] { \',\', \';\', \' \' }, StringSplitOptions.RemoveEmptyEntries)\n    .Select(c => c.Trim())\n    .ToArray();\n\nbool autorizado = CodigosPermiso.Any(UsuarioSesionHelper.TienePermiso);', 'Permisos/RequierePermisoAttribute.cs')
    g('Parámetros <code>out</code>', 'Un método devuelve un valor y, además, deja otros en variables que le pasas con <code>out</code>. El patrón <code>Try…</code> devuelve <code>true/false</code> y el resultado por <code>out</code>.',
      'if (!TryHex(principal, out p) || !TryHex(secundario, out s)) return "Tema_Err_Hex";', 'Helpers/TemaInforme.cs')
    g('Valores nulos: <code>?</code>, <code>??</code> y <code>?.</code>', '<code>int?</code> es un entero que puede ser nulo. <code>a ?? b</code> usa <code>b</code> si <code>a</code> es nulo. <code>x?.Prop</code> evita el error si <code>x</code> es nulo. <code>string?</code> (con <code>#nullable enable</code>) declara que un texto puede ser nulo.',
      'var idEmpresa = usuario?.IdEmpresa ?? 0;\nstring url = "/" + (rutaVirtual ?? "").TrimStart(\'~\', \'/\');', 'Controllers/DatosController.Cargue.cs y Helpers/AssetVersion.cs')
    g('Objetos anónimos', 'Un objeto sin clase, con las propiedades que escribas. Se usa para parámetros SQL y para respuestas JSON rápidas.',
      'Bd.Ejecutar(cadena, "UPDATE dbo.Escenarios SET Activo = 0 WHERE Id = @Id", new { Id = id });\nreturn Json(new { success = true, eliminados });', 'Services/EscenarioService.cs y Controllers/AuditoriaController.cs')
    g('Coincidencia de patrones', '<code>is</code> comprueba el tipo y, si coincide, ya te entrega la variable tipada. <code>switch</code> elige según valores.',
      'if (cellValue is double d) return d != 0;\nswitch (s)\n{\n    case "1": case "si": case "x": return true;\n    case "0": case "no": return false;\n    default: return null;\n}', 'Helpers/CargueConversion.cs (ConvertirCeldaBit, abreviado)')
    g('Tuplas', 'Devolver varios valores con nombre sin crear una clase.',
      'public List<(int Id, string Nombre, string Usuario)> ListarUsuariosElegibles(int idEmpresa)', 'Services/IntegracionesService.cs')
    g('Métodos de extensión', 'Agregan métodos a un tipo existente. El primer parámetro lleva <code>this</code>. Así <code>builder.Services.AddServiciosBufins()</code> parece un método de <code>IServiceCollection</code>.',
      'public static IServiceCollection AddServiciosBufins(this IServiceCollection servicios)', 'Infra/ServiciosExtensions.cs')
    g('Clases parciales', '<code>partial</code> permite repartir una misma clase en varios archivos. Aquí se usa para que ningún archivo sea enorme: <code>DatosController.cs</code>, <code>.Cargue.cs</code>, <code>.Modelos.cs</code> y <code>.Hallazgos.cs</code> son una sola clase.',
      'public partial class DatosController { ... }   // en cada archivo', 'Controllers/DatosController*.cs')
    g('Interpolación de texto', 'El <code>$</code> antes de las comillas permite insertar valores dentro del texto entre llaves.',
      '$"Escenario creado: {nombre.Trim()}"', 'Controllers/GestorEscenariosController.cs')
    g('Constantes y enumeraciones', '<code>const</code> es un valor fijo con nombre; evita escribir el mismo texto en muchos sitios y equivocarse.',
      'public static class AuditoriaTipo\n{\n    public const string Seguridad = "SEGURIDAD";\n    public const string Usuarios  = "USUARIOS";\n}', 'Models/AuditoriaModels.cs')
    g('<code>async</code> y <code>await</code>', 'Permiten esperar una operación lenta (red, disco) sin bloquear el hilo. En este sitio casi todo es síncrono a propósito (simple y suficiente); hay versiones asíncronas donde se usa la red.',
      'public async Task<string> ObtenerValorAsync(string clave)\n{\n    ...\n    await cn.OpenAsync().ConfigureAwait(false);\n}', 'Services/ConfiguracionSistemaService.cs')
    g('Pruebas con xUnit', '<code>[Fact]</code> marca una prueba; <code>[Theory]</code> con <code>[InlineData(...)]</code> la repite con varios valores.',
      '[Theory]\n[InlineData("15.549", "15549")]\n[InlineData("150.534", "150.534")]\npublic void SanitizarValorNumerico_AplicaLaHeuristicaColombiana(string entrada, string esperado)\n{\n    Assert.Equal(esperado, CargueConversion.SanitizarValorNumerico(entrada));\n}', 'Tests/Bufins.Pruebas/CargueConversionTests.cs')
    s.append(('glosario', 'Glosario C#', seccion('glosario', 14, 'Aprender C# y .NET con el propio código',
        '<p class="lead">Cada concepto va con un ejemplo <b>real</b> de BUFINS. Abre el archivo indicado y búscalo: verlo en su contexto es la mejor forma de aprenderlo.</p>' + ''.join(gl))))

    # 15 ---------------------------------------------------------------------------------------------------------------
    s.append(('ruta', 'Ruta de lectura', seccion('ruta', 15, 'Ruta de lectura sugerida',
        '<p class="lead">Un orden pensado para ir de lo simple a lo complejo. Dedica una sesión a cada paso.</p>'
        + tabla(['Paso', 'Archivo', 'Qué aprendes'], [
            ['1', '<code>Models/Escenario.cs</code>', 'Qué es una clase con propiedades.'],
            ['2', '<code>Services/EscenarioService.cs</code> y <code>Helpers/Bd.cs</code>', 'Cómo se habla con la base de datos.'],
            ['3', '<code>Controllers/GestorEscenariosController.cs</code>', 'Un controlador completo: atributos, servicios, mensajes, auditoría.'],
            ['4', '<code>Views/Configuracion/GestorEscenarios.cshtml</code>', 'Cómo una vista Razor muestra los datos y envía formularios.'],
            ['5', '<code>Program.cs</code>', 'El arranque: configuración, servicios y orden de los middlewares.'],
            ['6', '<code>Permisos/ValidarSesionAttribute.cs</code> y <code>RequierePermisoAttribute.cs</code>', 'Cómo funcionan los filtros.'],
            ['7', '<code>Helpers/EmpresaAccesoHelper.cs</code> y <code>UsuarioSesionHelper.cs</code>', 'Roles, sesión y aislamiento por empresa.'],
            ['8', '<code>Controllers/InformePYGController.cs</code> y <code>Services/InformePYGService.cs</code>', 'Un informe real de punta a punta.'],
            ['9', '<code>Services/CargueOrquestadorService*.cs</code>', 'Un flujo grande con transacciones y reglas.'],
            ['10', '<code>Tests/Bufins.Pruebas/*</code>', 'Cómo se protege el comportamiento con pruebas.']])
        + '<h3>Ejercicios</h3>'
        + '<ul><li>Agrega un campo <b>Descripción</b> al modelo <code>Escenario</code> y síguelo por las cuatro capas (en una copia de prueba, no en producción).</li>'
          '<li>Escribe una prueba nueva para <code>TemaInforme.TryHex</code> con un caso que no esté.</li>'
          '<li>Busca con el buscador todos los usos de <code>Bd.Leer</code> y explica con tus palabras qué hace la lambda en uno de ellos.</li>'
          '<li>Sigue una petición real con el navegador abierto: pestaña Red (F12) y el log de <code>App_Data/logs</code>.</li></ul>')))

    # 16 ---------------------------------------------------------------------------------------------------------------
    s.append(('refactor', 'Refactor y deuda', seccion('refactor', 16, 'Qué cambió en la reingeniería y qué queda pendiente',
        '<p class="lead">Después de migrar a .NET 10 se hizo una limpieza sin cambiar el comportamiento. El detalle por fase está en <code>.claude/notas/refactor-plan.md</code>.</p>'
        + tabla(['Métrica', 'Antes', 'Ahora'], [
            ['Código C# del sitio', '51.476 líneas', '50.524 líneas'],
            ['Archivo C# más grande', '2.199 líneas', '900 líneas'],
            ['Aperturas manuales de conexión SQL', '365', '206'],
            ['Servicios creados con <code>new</code> en controladores', '305', '9'],
            ['CSS (por retirar bootstrap-icons)', '23.550 líneas', '15.210 líneas'],
            ['Pruebas automáticas', '0', '77']])
        + '<h3>Deuda conocida y decisiones</h3>'
        + '<ul><li><b>Mensajes de error:</b> ningún error técnico llega al navegador con su texto original. Los controladores usan <code>MensajeError(ex)</code> y los servicios <code>MensajeErrorHelper.Crear(...)</code>: registran el detalle y muestran un código de referencia (se busca en <code>App_Data/logs</code>). Se conservan a propósito el diagnóstico SMTP del correo de prueba y los diagnósticos del archivo de cargue del cliente.</li>'
          '<li><b>Capa Compat (sesión, cookies, caché, configuración):</b> se conserva a propósito; reemplazarla toca el inicio de sesión y requiere pruebas de integración antes.</li>'
          '<li><b>JavaScript en las vistas de informes:</b> unas 12.000 líneas en línea; el PDF y el slider de rango están copiados con variaciones. Unificarlos requiere probar en navegador informe por informe.</li>'
          '<li><b>Nulabilidad:</b> activada por archivo en 8 ayudantes; el resto del proyecto la tiene desactivada (activarla en todo da unos 2.000 avisos, sobre todo en los modelos).</li>'
          '<li><b><code>async</code>:</b> el código es mayormente síncrono; convertirlo es un cambio en cascada con poco beneficio hoy.</li>'
          '<li><b>Despliegue:</b> la publicación la ejecuta el responsable con su usuario; la contraseña nunca se comparte por chat.</li></ul>')))

    # 17 ---------------------------------------------------------------------------------------------------------------
    s.append(('faq', 'Preguntas', seccion('faq', 17, 'Preguntas frecuentes',
        faq([
            ('¿Dónde empiezo si nunca he programado en C#?', 'En la ruta de lectura (sección 15), pasos 1 a 3, con el glosario (sección 14) al lado. Cada concepto del glosario tiene su ejemplo real.'),
            ('¿Por qué no se usa Entity Framework?', 'El sitio nació con ADO.NET y procedimientos almacenados, y el cálculo financiero vive en la base de datos. Cambiarlo sería reescribir todo el acceso a datos; en su lugar se redujo la repetición con <code>Bd</code>.'),
            ('¿Por qué hay clases partial como <code>DatosController.Cargue.cs</code>?', 'Para repartir un archivo que había crecido a más de 1.400 líneas sin cambiar las URLs (un controlador nuevo cambiaría la ruta de cada acción).'),
            ('¿Cómo sé qué texto va en el .resx?', 'Todo texto visible: títulos, botones, mensajes y errores. Se crea con la misma clave en <code>Strings.resx</code> (español) y <code>Strings.en-US.resx</code> (inglés).'),
            ('¿Por qué no puedo usar <code>onclick</code> en el HTML?', 'Por la política de seguridad de contenido (CSP): solo se ejecutan los scripts con el <i>nonce</i> de la petición. Se usan <code>data-*</code> y listeners; el archivo <code>inline-handlers.js</code> cubre los casos simples.'),
            ('¿Se pierde la sesión al publicar?', 'Sí: la sesión está en la memoria del servidor, así que un reinicio la cierra y los formularios abiertos dejan de ser válidos. Conviene publicar fuera de horas de uso.'),
            ('¿Cómo agrego un permiso nuevo a una pantalla?', 'Crea la opción en la tabla <code>MenuOpciones</code> con su código y coloca <code>[RequierePermiso("CODIGO")]</code> en el controlador; luego se asigna a los usuarios desde el gestor de permisos.'),
            ('¿Dónde está la documentación de seguridad?', 'En la biblioteca «Documentación BUFINS»: «Ciberseguridad BUFINS» y «Seguridad: blindaje del inicio de sesión».')]))))
    return s
