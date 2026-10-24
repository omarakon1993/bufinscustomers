# Estilo de modales BUFINS — "Limpio con acento" (1a)

## Cómo usarlo en VS Code con Claude Code

1. Copia esta carpeta `handoff/` a la raíz del repo `bufinscustomers` (por ejemplo `docs-claude/modales/`).
2. Abre Claude Code en VS Code y pega el prompt de abajo.
3. Pídele primero que lo haga **solo en `Views/Configuracion/Empresas.cshtml`**, revisa en el navegador y luego dile que siga con el resto de los gestores.

---

## Prompt para pegar en Claude Code

```
Quiero unificar el estilo de TODOS los modales del proyecto con la propuesta "1a · Limpio con acento".
En docs-claude/modales/ tienes:
- capturas/01-modal-con-logo.png, 02-modal-sin-logo.png, 03-campo-con-foco.png  → resultado esperado
- bufins-modal.css  → hoja de estilos final
- ejemplo-modal-empresa.html → markup de referencia del modal Editar Empresa

Pasos:
1. Copia bufins-modal.css a Assets/css/bufins-modal.css y regístrala en el layout principal
   (Views/Shared/_Layout.cshtml y BundleConfig si aplica) DESPUÉS de layout.css y bufins-components.css.
2. No borres las reglas de modal de layout.css todavía; bufins-modal.css las sobrescribe.
   Excepciones que NO deben cambiar: .modal-confirm (confirmaciones oscuras) y .hc-modal.
3. Empieza por Views/Configuracion/Empresas.cshtml (modales editEmpresaModal y crearEmpresaModal):
   - Cabecera: deja <i class="fas ..."> dentro de .modal-title (el CSS lo vuelve un tile con degradado).
     Agrega <span class="bf-modal-titles"> con el título y un .bf-modal-sub con "Nombre • NIT ..." (rellenarlo en setEditEmpresa).
   - Agrupa campos con <div class="bf-section">Identificación</div> y <div class="bf-section">Contacto y ubicación</div>.
   - Quita los iconos de los <label> y muévelos dentro del input con <div class="bf-input"><i ...></i><input class="form-control"></div>.
   - Quita form-control-sm.
   - Logo: usa .bf-logo-box + .bf-logo-actions (.bf-btn-outline "Cambiar logo" + .bf-btn-icon-danger para quitar).
   - Footer: <span class="bf-footer-note"><b>*</b> Campos obligatorios</span> a la izquierda, luego Cancelar (btn-secondary, sin icono) y Guardar (btn-primary).
   - Elimina el texto "* Campos obligatorios" del inicio del body (ahora vive en el footer).
   - Conserva TODA la lógica existente: ids, name, validaciones, pattern, oninput, AntiForgeryToken, Resources.Strings.
4. Muéstrame el diff y espera mi confirmación.
5. Después, busca todos los .cshtml con class="modal" (grep "modal-dialog") y aplica el mismo patrón
   gestor por gestor (Usuarios, Grupos empresariales, Configuración, etc.). Haz un commit por gestor.
6. No inventes colores: usa solo las variables existentes (--app-btn-gradient, --content-gradient, #583AFF, #01E1D7, #160933).
```

---

## Especificación rápida

| Elemento | Valor |
|---|---|
| Contenedor | radio 16px, sin borde, sombra `0 30px 70px -25px rgba(22,9,51,.55)` |
| Acento superior | 3px con `--content-gradient` |
| Header | fondo blanco, padding 20/24, borde inferior `#eceef4` |
| Icono título | tile 40×40, radio 11, `--app-btn-gradient`, icono blanco 17px |
| Título | 18px / 800 / `#160933` · subtítulo 13px `#6b7280` |
| Cerrar | 34×34, radio 9, borde `#e3e6f0`; hover morado `#583AFF` |
| Sección | 11.5px / 800 / mayúsculas / `#583AFF` + línea `#eceef4` |
| Label | 13px / 700 / `#374151`, asterisco `#ef4444` |
| Input | alto 38, radio 8, borde `#dfe3ec`, icono a la izquierda `#a3a9b6`; foco borde `#583AFF` + halo `rgba(88,58,255,.12)` |
| Logo | caja 150px alto, borde 1.5px dashed `#c9c2f5`, fondo `#f8f7ff` |
| Footer | fondo `#f8f9fc`, nota de obligatorios a la izquierda |
| Botón primario | `--app-btn-gradient`, alto 40, radio 9 |
| Botón cancelar | blanco, borde `#d1d5db`, texto `#374151` |
| Fuente | Nunito (la del proyecto) |

Modales de **eliminar/confirmar** siguen usando `.modal-confirm` (oscuro) — no se tocan.
