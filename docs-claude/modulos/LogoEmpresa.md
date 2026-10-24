# Logo de la empresa en los informes

Resumen y reglas obligatorias: `CLAUDE.md` → «Logo de la empresa en los informes — ESTÁNDAR». Aquí va el detalle de mantenimiento.

## Piezas
| Pieza | Archivo |
|---|---|
| Tabla (script manual) | `Sql/036_EmpresaLogos.sql` → `dbo.EmpresaLogos(IdEmpresa PK/FK ON DELETE CASCADE, Imagen, TipoImagen, Ancho, Alto, Bytes, Version, NombreOriginal, FechaActualizacion, IdUsuarioActualizo)` |
| Reglas del archivo | `Helpers/EmpresaLogoHelper.cs` (`Procesar` → `ResultadoLogo`; errores = claves `EmpLogo_Err_*`) |
| Persistencia | `Services/EmpresaLogoService.cs` (`Disponible`, `Obtener`, `ObtenerVersiones`, `Guardar`, `Eliminar`; tolera tabla inexistente, error 208) |
| Endpoints | `Controllers/EmpresaLogoController.cs` (`Ver`, `Datos`, `Subir`, `Eliminar`) |
| Gestor | `Views/Configuracion/Empresas.cshtml` (columna «Logo», zona de carga en el modal de edición; el modal de creación también lo pide (opcional): el archivo viaja en el mismo envío del formulario y `EmpresaController.CrearEmpresa` lo valida ANTES de crear la empresa; si es inválido no se crea nada) |
| Pantalla | `Assets/css/empresa-logo.css`, `Assets/js/empresa-logo.js` |
| Excel | `Helpers/ExcelLogoHelper.cs` |
| Correo | `Services/EmailService.EnviarInforme(..., byte[] logoEmpresa)` |

## Reglas del archivo (para soporte)
PNG o JPG · ≤ 2 MB · mínimo 200×50 px · máximo 4000 px por lado (12 MP) · proporción ancho/alto 0,9–6 · archivo completo · no en blanco. Se guarda PNG ≤ 600×200 px. Si el cliente pregunta por SVG/GIF/WebP: no se aceptan (SVG puede llevar scripts; los otros dos no se procesan de forma fiable).

## Dónde queda el logo
- **Pantalla**: placa blanca a la izquierda del nombre de la empresa (`.pyg2-head`, `.lt-head-row`) o al inicio del encabezado de la tabla (Informe de modelos).
- **PDF**: placa blanca a la derecha de la franja oscura de la primera página (Línea de tiempo: derecha de la franja azul, el título se corre a su izquierda; Histórico de precios: a la izquierda de la etiqueta de la derecha).
- **Excel**: imagen flotante de 48 px de alto junto al título (B1; Línea de tiempo: columna ≥ 5). PYG, Balance e Informe de modelos reservan 3 filas arriba solo si hay logo.
- **Correo**: placa blanca bajo la marca Bufins (imagen incrustada `cid:logo-empresa`).
- **Imagen**: la captura de pantalla ya incluye la placa.

## Cómo probar (manual)
1. Ejecutar `Sql/036_EmpresaLogos.sql`.
2. Empresas → editar → subir un PNG/JPG válido → ver la miniatura en la tabla.
3. Probar rechazos: GIF, imagen de 100×30, imagen 10:1, imagen en blanco, archivo de texto renombrado `.png`.
4. En cada informe (los 9 de la lista del CLAUDE.md): consultar → logo junto al nombre → PDF → Excel → imagen → correo.
5. Empresa SIN logo: los informes y exportaciones quedan exactamente como antes.

## Pendiente / decisiones
- Las documentaciones HTML de Indicadores e Histórico de precios (`App_Data/Documentacion`) **no** mencionan aún el logo ni traen capturas nuevas; regenerarlas cuando se decida (capturas con una empresa que tenga logo).
- Sin notificaciones internas (editar un logo es una edición: no aplica según la regla del proyecto). La auditoría sí: `AuditoriaTipo.Empresas` / `Editar` («Logo de la empresa agregado/reemplazado/eliminado»).
