# Guía del Menú Lateral Moderno - Bufins

## 🎯 Descripción General

El nuevo menú lateral de Bufins es un sistema **híbrido y adaptativo** diseñado para funcionar perfectamente en todos los dispositivos, desde pantallas grandes de 27" hasta móviles pequeños.

## ✨ Características Principales

### 1. **Diseño Responsive Inteligente**
- **Desktop (>992px)**: Menú colapsable con tooltips
- **Tablet (768px-991px)**: Menú overlay con backdrop
- **Móvil (<768px)**: Menú fullscreen deslizante

### 2. **Búsqueda en Tiempo Real**
- Campo de búsqueda en la parte superior del menú
- Filtra categorías, menús y submenús
- Resalta automáticamente los resultados
- Expande submenús que coinciden con la búsqueda

### 3. **Tooltips Inteligentes**
- Se muestran automáticamente cuando el menú está colapsado
- Posicionamiento inteligente a la derecha del ícono
- Animación suave fade-in/fade-out
- Solo visibles en modo desktop

### 4. **Badges/Indicadores**
- Contadores visuales para notificaciones
- Tres variantes: info (azul), success (verde), danger (rojo)
- Adaptativo: en modo colapsado se muestran como puntos pequeños
- Animación de "pulso" para alertas importantes

### 5. **Animaciones Fluidas**
- Transiciones suaves al expandir/colapsar
- Efectos hover modernos con elevación
- Micro-interacciones en clicks
- Animaciones CSS3 optimizadas por GPU

### 6. **Persistencia de Estado**
- El estado del menú (expandido/colapsado) se guarda en localStorage
- Se restaura automáticamente al recargar la página
- Solo aplica en desktop (en móvil siempre inicia oculto)

## 🎨 Paleta de Colores

```css
--sidebar-bg-primary: #160933    /* Morado oscuro */
--sidebar-accent-1: #583AFF       /* Morado vibrante */
--sidebar-accent-2: #01E1D7       /* Cyan */
--sidebar-gradient: linear-gradient(90deg, #6366F1, #3B82F6, #06B6D4, #01E1D7)
```

## 📱 Comportamiento por Dispositivo

### Desktop (>992px)
- **Ancho expandido**: 240px
- **Ancho colapsado**: 70px
- **Toggle**: Botón con ícono de flecha en el header
- **Tooltips**: Activos en modo colapsado
- **Submenús**: Accordion tradicional

### Tablet (768px - 991px)
- **Modo**: Overlay con backdrop
- **Activación**: Botón hamburguesa en esquina superior izquierda
- **Cierre**: Click en backdrop o botón hamburguesa
- **Ancho**: 280px
- **Backdrop**: Oscuro con blur

### Móvil (<768px)
- **Modo**: Fullscreen overlay
- **Activación**: Botón hamburguesa
- **Cierre**: Click en backdrop, botón hamburguesa o tecla ESC
- **Ancho**: 100% (máx 320px)
- **Backdrop**: Oscuro con blur intenso

## 🔧 Funciones JavaScript Disponibles

El menú expone una API pública que puedes usar:

```javascript
// Alternar estado (expandir/colapsar)
window.ModernSidebar.toggle();

// Colapsar menú
window.ModernSidebar.collapse();

// Expandir menú
window.ModernSidebar.expand();

// Verificar si está colapsado
if (window.ModernSidebar.isCollapsed()) {
    console.log('Menú está colapsado');
}

// Buscar en el menú programáticamente
window.ModernSidebar.search('reportes');

// Refrescar item activo (útil después de navegación AJAX)
window.ModernSidebar.refresh();
```

### Con jQuery:

```javascript
// Colapsar
$('.modern-sidebar').modernSidebar('collapse');

// Expandir
$('.modern-sidebar').modernSidebar('expand');

// Toggle
$('.modern-sidebar').modernSidebar('toggle');
```

## 📋 Estructura HTML

### Categoría con Submenús:

```html
<div class="sidebar-category">
    <div class="sidebar-category-header">
        <i class="fas fa-database sidebar-category-icon"></i>
        <span class="sidebar-category-title">CATEGORÍA</span>
    </div>

    <!-- Item con submenú -->
    <div class="sidebar-nav-item">
        <a class="sidebar-nav-link" href="#" data-has-submenu="true">
            <i class="fas fa-file-excel sidebar-nav-icon"></i>
            <span class="sidebar-nav-text">Menú Principal</span>
            <span class="sidebar-badge badge-success">2</span>
            <i class="fas fa-chevron-right sidebar-nav-arrow"></i>
            <span class="sidebar-tooltip">Menú Principal</span>
        </a>
        <div class="sidebar-submenu">
            <div class="sidebar-submenu-item">
                <a class="sidebar-submenu-link" href="/url/destino">
                    Submenú 1
                </a>
            </div>
        </div>
    </div>
</div>
```

### Item sin Submenú (Link Directo):

```html
<div class="sidebar-nav-item">
    <a class="sidebar-nav-link" href="/url/destino">
        <i class="fas fa-home sidebar-nav-icon"></i>
        <span class="sidebar-nav-text">Dashboard</span>
        <span class="sidebar-tooltip">Dashboard</span>
    </a>
</div>
```

## 🎨 Badges/Indicadores

### Tipos de Badges:

```html
<!-- Badge azul (info) - por defecto -->
<span class="sidebar-badge">5</span>

<!-- Badge verde (success) -->
<span class="sidebar-badge badge-success">3</span>

<!-- Badge rojo (danger) - con animación de pulso -->
<span class="sidebar-badge badge-danger">!</span>

<!-- Badge amarillo (warning) -->
<span class="sidebar-badge badge-warning">2</span>
```

## 🔍 Búsqueda

La búsqueda funciona automáticamente:

1. **Filtra por**:
   - Nombres de categorías
   - Nombres de menús principales
   - Nombres de submenús

2. **Comportamiento**:
   - Oculta items que no coinciden
   - Expande automáticamente submenús con resultados
   - Tiempo de respuesta: 300ms (debounce)

3. **Limpiar búsqueda**:
   - Borrar el texto del input
   - Muestra todos los items nuevamente

## ⌨️ Atajos de Teclado

- **ESC**: Cierra el menú en móvil/tablet
- **Tab**: Navegación por teclado entre elementos
- **Enter**: Activa el link/menú seleccionado

## 🎯 Eventos Personalizados

El menú dispara eventos que puedes escuchar:

```javascript
// Cuando el menú se expande/colapsa
window.addEventListener('sidebarToggled', function(e) {
    console.log('Menú colapsado:', e.detail.collapsed);
    // Aquí puedes ajustar el tamaño de gráficos, tablas, etc.
});
```

## 💡 Mejores Prácticas

### 1. **Nombres Cortos**
- Usa nombres descriptivos pero concisos
- Máximo 2-3 palabras por item
- Ejemplo: ✅ "Cargue Excel" vs ❌ "Cargar archivo de Excel desde computadora"

### 2. **Iconos Apropiados**
- Usa iconos de Font Awesome que representen claramente la función
- Mantén consistencia en el estilo de iconos
- Ejemplo: `fa-file-excel` para archivos Excel, `fa-chart-bar` para reportes

### 3. **Organización Lógica**
- Agrupa funciones relacionadas en la misma categoría
- Máximo 4-6 items por categoría
- Orden de importancia: más usado arriba

### 4. **Uso de Badges**
- Solo para información importante
- No abuses de ellos (pueden distraer)
- Actualiza los números dinámicamente con JavaScript si es necesario

## 🐛 Solución de Problemas

### El menú no se muestra:
1. Verifica que los archivos CSS y JS estén cargados
2. Abre la consola del navegador y busca errores
3. Verifica que jQuery esté cargado antes de modern-sidebar.js

### Los tooltips no aparecen:
- Solo funcionan en modo desktop (>992px)
- Solo se muestran cuando el menú está colapsado
- Verifica que el elemento tenga la clase `sidebar-tooltip`

### La búsqueda no funciona:
- Verifica que el input tenga la clase `sidebar-search-input`
- Verifica que no haya errores de JavaScript en la consola

### El menú no se colapsa en móvil:
- El comportamiento es correcto: en móvil el menú se oculta/muestra, no se colapsa
- Usa el botón hamburguesa para abrir/cerrar

## 📝 Personalización

### Cambiar Colores:

Edita las variables CSS en `modern-sidebar.css`:

```css
:root {
    --sidebar-width-expanded: 240px;  /* Ancho expandido */
    --sidebar-width-collapsed: 70px;  /* Ancho colapsado */
    --sidebar-bg-primary: #160933;    /* Color de fondo */
    --sidebar-accent-1: #583AFF;      /* Color acento 1 */
    --sidebar-accent-2: #01E1D7;      /* Color acento 2 */
}
```

### Cambiar Breakpoints:

Edita las constantes en `modern-sidebar.js`:

```javascript
const CONFIG = {
    MOBILE_BREAKPOINT: 992,   /* Cambiar punto de corte móvil */
    TABLET_BREAKPOINT: 768,   /* Cambiar punto de corte tablet */
};
```

## 🚀 Optimización de Rendimiento

El menú está optimizado para:
- **Animaciones GPU**: Usa `transform` y `opacity` para mejor performance
- **Debouncing**: La búsqueda espera 300ms antes de filtrar
- **Lazy Loading**: Los submenús solo se procesan cuando se expanden
- **CSS Puro**: Las animaciones son CSS puro, no JavaScript

## 📞 Soporte

Para problemas o mejoras:
1. Revisa esta documentación
2. Verifica la consola del navegador
3. Comprueba que todos los archivos estén cargados correctamente

## 🎉 ¡Disfruta tu Nuevo Menú Moderno!

El menú está listo para usar en todos los dispositivos. Recarga la página y prueba:
- Colapsar/expandir en desktop
- Buscar opciones
- Navegar en móvil
- Todas las animaciones

---

**Versión**: 1.0.0
**Fecha**: Octubre 2025
**Desarrollado para**: Bufins Customers
