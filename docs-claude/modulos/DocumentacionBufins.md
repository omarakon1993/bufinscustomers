# Biblioteca «Documentación BUFINS»

Pantalla de **Configuración** solo para **Super Admin** que reúne la documentación del sistema.

## Piezas
- `Controllers/DocumentacionBufinsController.cs` — `Index` (lista) y `Ver(clave)` (sirve el documento). `[ValidarSesion][SoloSuperAdmin]`.
- `Views/Configuracion/DocumentacionBufins.cshtml` — filtros (texto, categoría, alcance general/de menú), tarjetas agrupadas por categoría, botón «Abrir».
- `Helpers/DocumentacionBibliotecaCatalogo.cs` — catálogo único (documentos y categorías).
- `Assets/js/doc-visor.js` — `DocVisor.abrirUrl(url, titulo, $foco)` abre el mismo modal que los informes con el documento elegido (vacía el iframe al cerrar).
- `Sql/032_Menu_DocumentacionBufins.sql` — opción de menú `ADMIN_DOCUMENTACION_BUFINS` (SoloSuperAdmin), junto a «Configuraciones generales BUFINS».
- Archivos: `App_Data/Documentacion/Biblioteca/*.html` (no públicos) y, para los informes, `App_Data/Documentacion/Informe_*.html`.

## Documentos actuales (solo lo oficial)
| Clave | Categoría | Alcance |
|---|---|---|
| `seguridad-blindaje` | Seguridad | General |
| `informe-indicadores` | Informes | Menú: Informes → Informe de indicadores |
| `informe-hist-precios` | Informes | Menú: Informes → Histórico de precios |

**Regla:** la biblioteca solo tiene la documentación oficial. Al crear documentación nueva dentro de una opción hay que **preguntar** si se agrega aquí; por defecto no se agrega, y nada más entra sin que el responsable lo pida explícitamente.

## Agregar un documento
1. HTML autocontenido (sin recursos externos: la CSP del visor solo permite estilos/scripts en línea e imágenes `data:`) en `App_Data/Documentacion/Biblioteca/`.
2. `<Content Include="App_Data\Documentacion\Biblioteca\...html" />` en el `.csproj`.
3. Una línea en `DocumentacionBibliotecaCatalogo.Todos` (y la categoría en `Categorias` + recurso `DocBuf_Cat_<id>` si es nueva).

## Estilo de los documentos
Todos los documentos de la biblioteca usan el estilo único de la documentación (cabecera con degradado de marca, navegación fija con píldoras, secciones numeradas, tarjetas y tablas con encabezado degradado, modo oscuro): ver «ESTILO VISUAL ÚNICO» en `CLAUDE.md` → «Documentación de informes — ESTÁNDAR». Modelo: `Informe_Indicadores.html`.

## Protección y auditoría
Mismo mecanismo que la documentación de los informes (`DocumentacionProtegidaHelper`): marca de agua con nombre, empresa y fecha, aviso de confidencialidad, bloqueo de imprimir/copiar/guardar, sin caché, rechazo de apertura directa en pestaña y auditoría de cada consulta (`AuditoriaTipo.Informes` + `Ver`, entidad `DocumentacionBufins.<clave>`).

## Nota
Los PDF/Word no se pueden mostrar en el visor: si alguno se pide para la biblioteca, hay que convertirlo antes a HTML autocontenido.
