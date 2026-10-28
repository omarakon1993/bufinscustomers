<!-- Detalle técnico. Si cambias este informe, actualiza este archivo (y la documentación HTML si algún día se le crea). -->

# Informe mensual de gerencia

Informe nuevo (2026-10): el cierre de UN mes en un solo documento (el «management pack» que el equipo manda cada mes). Opción de menú **global** `INFORMES_GERENCIA_MENSUAL` (script manual `Sql/041`); `[RequierePermiso]` en el controlador. Cápsula «Información modelo Bufins».

- **Piezas**: `Models/InformeGerenciaMensualModels.cs`, `Services/InformeGerenciaMensualService.cs`, `Controllers/InformeGerenciaMensualController.cs`, `Views/Informes/InformeGerenciaMensual.cshtml`, `Assets/css/informe-gerencia-mensual.css`. Textos `GM_*`.
- **No calcula nada propio**: `InformeGerenciaMensualService.Construir` llama a los servicios de cada informe con el rango de UN mes (así las cifras son idénticas a las de las pantallas originales) y cada sección es independiente (`try/catch` + banderas `HayResultados`, `HayBalance`, `HayCaja`, `HayIndicadores`, `HayLineas`): si una falla o no tiene datos las demás se muestran; solo si todas faltan → `SinDatos`.
  - **Resultados** (`InformePYGService`, comparar = presupuesto): Ingresos, EBITDA y Utilidad neta del mes (`IngresosFila`/`EbitdaFila`/`UtilidadNetaFila`) con variación vs presupuesto, vs año anterior y acumulado del año.
  - **Balance** (`InformeBalanceService`, comparar = cierre del año anterior): activo total, capital de trabajo, razón corriente, endeudamiento y chequeo de cuadre.
  - **Caja** (`InformeFlujoService(Caja)`, medida real): caja inicial, flujo neto (vs presupuesto), flujo de caja libre operativo, caja final (la del balance) y su conciliación — las mismas reglas de «Caja inicial → caja final» de `Flujos.md`.
  - **Indicadores** (`InformeIndicadoresService`): conteo del semáforo y los rojos/naranja que más requieren atención (hasta 6; respeta los umbrales propios de la empresa).
  - **Líneas de negocio** (`InformeLineasNegocioService`): las 6 primeras del mes.
- **Mes de cierre**: lista de los meses con ingresos en el PYG (`ObtenerMesesDisponibles`), el más reciente primero. No hay slider: es un solo mes.
- **Salida**: encabezado con chips y unidad, **resumen con IA arriba** («Resultado:» / «Caja y balance:» / «Foco del mes:»; en inglés «Result:» / «Cash and balance:» / «Focus of the month:»; función `IAFuncion.InsightsGerenciaMensual`, prompt `RESUMEN_GERENCIA_MENSUAL`), tarjetas de resultados, bloques de balance y caja, indicadores en atención, líneas y comentarios.
- **Comentarios de gestión**: la tarjeta compartida en modo `todos:true` (lista los de TODOS los informes del cierre; los nuevos quedan como «Informe mensual de gerencia»). Entran al PDF (pág. 2) y a la hoja «Comentarios» del Excel.
- **Exportar**: Excel (Resumen, Atención y líneas, Comentarios), PDF A4 horizontal de 2 páginas sin gráficos (texto y tablas), imagen y correo (KPIs del mes + tabla de indicadores en atención; auditado, entidad `InformeGerenciaMensual`).
- **Cuidado al mantenerlo**: como depende de otros servicios, un cambio de firma o de comportamiento en PYG/Balance/Flujos/Indicadores/Líneas puede afectarlo — revisar `Construir`. El correo de indicadores usa los nombres `Ind_{Codigo}_Nombre`.
- **Fuera de alcance**: gráficos en el PDF, comparación entre escenarios, varios meses a la vez.
