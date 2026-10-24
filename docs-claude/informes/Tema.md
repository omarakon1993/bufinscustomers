# Tema de colores de los informes

Resumen y reglas obligatorias: `CLAUDE.md` → «Tema de colores de los informes — ESTÁNDAR». Aquí va el detalle de mantenimiento.

## Piezas
| Pieza | Archivo |
|---|---|
| Modelo, presets, derivación y validación (única fuente de verdad) | `Helpers/TemaInforme.cs` |
| Resolución segura (`bufins` / `corp:id:slot` / `custom`) | `Services/TemaInformeService.cs` |
| Paletas corporativas (tabla manual) | `Sql/037_EmpresaPaletas.sql`, `Services/EmpresaPaletaService.cs` |
| Endpoints | `Controllers/InformeTemaController.cs` (`Opciones`, `Resolver`), `Controllers/EmpresaPaletaController.cs` (`Obtener`, `Guardar`, `Eliminar`) |
| Selector en los informes | `Views/Shared/_TemaInformeSelector.cshtml`, `Assets/js/informe-tema.js`, `Assets/css/tema-informe.css` |
| Modal «Colores corporativos» | `Views/Shared/_ColoresCorporativosModal.cshtml`, `Assets/js/colores-corporativos.js` |
| Excel / correo | `Helpers/ExcelTemaHelper.cs`, `EmailService.EnviarInforme(..., logoEmpresa, tema)` |

## Variables CSS (definidas SOLO en el contenedor de resultado de cada informe)
`--tm-p`, `--tm-p-rgb`, `--tm-strong`, `--tm-strong-rgb`, `--tm-s`, `--tm-s-rgb`, `--tm-c` (tercer color), `--tm-a` (acento), `--tm-grad` (en los temas = color fijo), `--tm-soft`, `--tm-soft2`, `--tm-soft3`, `--tm-t1..t4` (tonos del principal), `--tm-dk`, `--tm-wash` / `--tm-wash2` (fondos suaves decorativos: tinte sólido .12 / .24 del principal).
Con el tema Bufins las variables **se quitan** y aplican los respaldos del CSS (`var(--tm-p, #6366F1)`): por eso el modo por defecto no cambia nada.

## Derivación (TemaInforme.Derivar) — colores FIJOS, sin degradados
- «Degradado» = el principal en las 4 paradas; correo = un solo tono oscuro del principal (strong −35 %); lavados decorativos = tinte sólido del principal.
- Fuerte: P oscurecido (8 % por paso, máx. 12) hasta contraste ≥ 4,5 sobre blanco. Tintes: P con blanco al 92 / 95,5 / 97,5 % (`Soft*`) y 86 / 78 / 60 / 30 % (`T1..T4`).
- Serie de 8 (solo de los 3 colores): P, S, A, P oscuro, S oscuro, A oscuro, P claro, S claro.
- Fichas y vistas previas muestran 3 bloques fijos Principal | Secundario | Acento (`muestra`); Bufins muestra su degradado de siempre.
- Estilos predeterminados: **solo Bufins** (valores exactos de siempre). Las demás paletas son las corporativas de cada empresa (2) y la personalizada.

## Papel de cada color (temas distintos de Bufins)
| Color | Dónde se ve |
|---|---|
| Principal | cabeceras de tabla, filas de total, segmentados/botones activos, títulos y etiquetas, serie principal, barras de KPI |
| Secundario | iconos de tarjetas KPI y de tarjetas de gráficos (`--tm-icon-bg`), línea de las secciones (`--tm-s`, 2 px), serie de **comparación** (`--tm-comp`: línea punteada y leyenda, 2.ª serie del Excel, `ambar` del PDF), etiquetas de familia de Indicadores |
| Acento | serie del escenario comparado (`tercero`/`--tm-c`, 3.ª serie) |
Con Bufins nada de esto cambia: las variables no se definen y aplican los respaldos del CSS.

## Qué NO se tematiza
Marca Bufins del letterhead (`.pyg2-brand`, `.lt-brand-mark`), logo de la empresa, verde/rojo de variaciones, semáforo y cascada positiva/negativa, grises y bordes neutros, cromo de la aplicación (sidebar, banner, filtros).

## Cómo probar (manual)
1. Ejecutar `Sql/037_EmpresaPaletas.sql`.
2. Empresas → botón paleta de una empresa → registrar 1 o 2 paletas (selector de color y código hex; sin nombre personalizable; probar un amarillo claro: debe rechazarse por contraste).
3. Cada informe (sección «Estilos del informe», debajo de «Rango y agrupación»): elegir Bufins, cada corporativa y «Personalizada» → Generar → revisar pantalla, PDF, imagen, Excel (cabeceras, subtotales, gráfico nativo) y correo (cabecera, borde del mensaje).
4. Regresión: con Bufins comparar capturas/Excel antes y después; deben ser idénticos.
5. Limpiar → vuelve a Bufins; cambiar de empresa → cambian las corporativas; una corporativa sin registrar aparece deshabilitada con «Sin registrar».
6. Sin el script 037: las dos corporativas aparecen «Sin registrar» y el modal avisa.

## Pendiente / decisiones
- Informe de modelos (grilla) e Histórico de precios no llevan selector todavía.
- Las documentaciones HTML de Indicadores e Histórico de precios no mencionan el selector (no se regeneraron).
- Sin notificaciones internas (edición de configuración); la auditoría sí: `Empresas`/`Editar` (`EmpresaPaletas`).
