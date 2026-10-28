<!-- Detalle técnico. Si cambias este módulo, actualiza este archivo. -->

# Comentarios de gestión (tarjeta compartida de los informes)

Notas que los usuarios de una empresa dejan sobre el **cierre** (año/mes) de un informe: causa de una desviación, decisión tomada, responsable. Una sola implementación para todos los informes.

- **Datos**: `dbo.ComentariosGestion` (`Sql/040_ComentariosGestion.sql`, **manual**): `Id, IdEmpresa, Informe (clave corta), Anio, Mes, Clave (opcional: p. ej. la línea de negocio), Texto (≤ 1000), IdUsuario, NombreUsuario, FechaCreacion, FechaEdicion, Activo`. Sin la tabla `Listar` devuelve `disponible=false`, la tarjeta muestra un aviso y oculta el formulario; el informe sigue igual.
- **Quién puede qué**: leer y escribir, **todos los usuarios con acceso a la empresa** (`EmpresaAccesoHelper.TieneAcceso`, que incluye el grupo empresarial); editar y eliminar, solo el autor o un administrador (Admin de Empresa / Super Admin). El borrado es lógico (`Activo = 0`). El nombre del autor se guarda en la fila (así sobrevive aunque el usuario cambie).
- **Seguridad**: la clave de informe viaja de una **lista blanca** (`ComentariosGestionService.InformesValidos`); el texto se limita a 1000 caracteres y se escapa al pintarlo; todos los POST llevan antiforgery.
- **Auditoría**: crear / editar / eliminar → `AuditoriaTipo.Informes` + `Crear|Editar|Eliminar`, entidad `ComentarioGestion` (con empresa).
- **Piezas**: `Services/ComentariosGestionService.cs`, `Controllers/ComentariosGestionController.cs` (`Listar`, `Crear`, `Editar`, `Eliminar`), `Views/Shared/_ComentariosGestion.cshtml`, `Assets/js/comentarios-gestion.js`, `Assets/css/comentarios-gestion.css`. Textos `Com_*` (ambos `.resx`); nombres de informe `Com_Inf_<Clave>`.
- **Cómo agregarla a un informe**: (1) `<link comentarios-gestion.css>` y `<script comentarios-gestion.js>` (con `AssetVersion.Url`), (2) `@Html.Partial("_ComentariosGestion", "xxComentarios")` antes del bloque de IA, (3) `ComentariosGestion.montar({ raiz: '#xxComentarios', informe: '<Clave>' })` en el `ready` y `ComentariosGestion.cargar({ idEmpresa, anio, mes, claves })` al pintar el resultado (el mes es el extremo derecho del rango), (4) en el PDF/Excel, `ComentariosGestion.ultimos()` / `ComentariosGestionService.Listar`. Si el informe es nuevo: agregar su clave a `InformesValidos` y `Com_Inf_<Clave>` a los `.resx`.
- **Hoy la llevan**: Líneas de negocio, PYG, Balance, Tesorería, Flujo de caja y de efectivo, Indicadores e Informe mensual de gerencia (este en modo `todos:true`, con los de todos los informes del cierre).
- **Pendiente de producto**: notificaciones internas cuando alguien comenta (no se agregaron; preguntar), menciones y respuestas en hilo.
