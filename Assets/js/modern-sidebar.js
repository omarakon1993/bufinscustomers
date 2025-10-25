/**
 * BUFINS MODERN SIDEBAR
 * Sistema de menú lateral moderno, responsive e híbrido
 * @version 1.0.0
 */

(function() {
    'use strict';

    // ===== CONFIGURACIÓN =====
    const CONFIG = {
        STORAGE_KEY: 'bufins_sidebar_state',
        DEBOUNCE_DELAY: 300,
        ANIMATION_DURATION: 300,
        MOBILE_BREAKPOINT: 992,
        TABLET_BREAKPOINT: 768
    };

    // ===== VARIABLES GLOBALES =====
    let sidebar = null;
    let sidebarToggleBtn = null;
    let sidebarHamburger = null;
    let sidebarBackdrop = null;
    let sidebarSearchInput = null;
    let mainContent = null;
    let isCollapsed = false;
    let isMobileOpen = false;
    let searchDebounceTimer = null;

    // ===== INICIALIZACIÓN =====
    function init() {
        // Esperar a que el DOM esté listo
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', setupSidebar);
        } else {
            setupSidebar();
        }
    }

    function setupSidebar() {
        // Obtener elementos del DOM
        sidebar = document.querySelector('.modern-sidebar');
        sidebarToggleBtn = document.querySelector('.sidebar-toggle-btn');
        sidebarHamburger = document.querySelector('.sidebar-hamburger');
        sidebarBackdrop = document.querySelector('.sidebar-backdrop');
        sidebarSearchInput = document.querySelector('.sidebar-search-input');
        mainContent = document.querySelector('.main-content');

        if (!sidebar) {
            console.warn('Modern Sidebar: Elemento .modern-sidebar no encontrado');
            return;
        }

        // Cargar estado guardado
        loadSidebarState();

        // Configurar event listeners
        setupEventListeners();

        // Configurar búsqueda
        setupSearch();

        // Configurar tooltips
        setupTooltips();

        // Configurar responsive
        setupResponsive();

        // Marcar item activo basado en URL
        markActiveMenuItem();

        console.log('Modern Sidebar inicializado correctamente');
    }

    // ===== EVENT LISTENERS =====
    function setupEventListeners() {
        // Toggle sidebar (desktop)
        if (sidebarToggleBtn) {
            sidebarToggleBtn.addEventListener('click', toggleSidebar);
        }

        // Hamburger menu (mobile/tablet)
        if (sidebarHamburger) {
            sidebarHamburger.addEventListener('click', toggleMobileSidebar);
        }

        // Backdrop (cerrar sidebar al hacer click fuera)
        if (sidebarBackdrop) {
            sidebarBackdrop.addEventListener('click', closeMobileSidebar);
        }

        // Items con submenú
        const navLinksWithSubmenu = document.querySelectorAll('.sidebar-nav-link[data-has-submenu="true"]');
        navLinksWithSubmenu.forEach(link => {
            link.addEventListener('click', handleSubmenuToggle);
        });

        // Prevenir cierre al hacer click dentro del sidebar
        if (sidebar) {
            sidebar.addEventListener('click', function(e) {
                e.stopPropagation();
            });
        }

        // Responsive: detectar cambios de tamaño de ventana
        window.addEventListener('resize', handleResize);

        // Cerrar sidebar con tecla ESC
        document.addEventListener('keydown', function(e) {
            if (e.key === 'Escape' && isMobileOpen) {
                closeMobileSidebar();
            }
        });
    }

    // ===== TOGGLE SIDEBAR (DESKTOP) =====
    function toggleSidebar() {
        if (window.innerWidth < CONFIG.MOBILE_BREAKPOINT) {
            return; // En móvil/tablet no usar este toggle
        }

        isCollapsed = !isCollapsed;

        if (isCollapsed) {
            sidebar.classList.add('collapsed');
        } else {
            sidebar.classList.remove('collapsed');
        }

        // Guardar estado
        saveSidebarState();

        // Dispatch evento personalizado
        window.dispatchEvent(new CustomEvent('sidebarToggled', {
            detail: { collapsed: isCollapsed }
        }));
    }

    // ===== TOGGLE MOBILE SIDEBAR =====
    function toggleMobileSidebar() {
        if (window.innerWidth >= CONFIG.MOBILE_BREAKPOINT) {
            return; // Solo funciona en móvil/tablet
        }

        isMobileOpen = !isMobileOpen;

        if (isMobileOpen) {
            openMobileSidebar();
        } else {
            closeMobileSidebar();
        }
    }

    function openMobileSidebar() {
        sidebar.classList.add('mobile-open');
        sidebar.classList.add('animate-in');
        sidebarBackdrop.classList.add('active');
        document.body.style.overflow = 'hidden'; // Prevenir scroll del body

        setTimeout(() => {
            sidebar.classList.remove('animate-in');
        }, CONFIG.ANIMATION_DURATION);
    }

    function closeMobileSidebar() {
        sidebar.classList.remove('mobile-open');
        sidebarBackdrop.classList.remove('active');
        document.body.style.overflow = '';
        isMobileOpen = false;
    }

    // ===== MANEJO DE SUBMENÚS =====
    function handleSubmenuToggle(e) {
        e.preventDefault();
        const link = e.currentTarget;
        const navItem = link.closest('.sidebar-nav-item');
        const submenu = navItem.querySelector('.sidebar-submenu');

        if (!submenu) return;

        const isExpanded = link.classList.contains('expanded');

        // En modo colapsado desktop, no expandir submenús (se manejan con tooltips/segundo nivel)
        if (isCollapsed && window.innerWidth >= CONFIG.MOBILE_BREAKPOINT) {
            return;
        }

        if (isExpanded) {
            // Cerrar
            link.classList.remove('expanded');
            submenu.classList.remove('expanded');
        } else {
            // Abrir (cerrar otros primero - accordion behavior)
            const allLinks = document.querySelectorAll('.sidebar-nav-link.expanded');
            allLinks.forEach(otherLink => {
                if (otherLink !== link) {
                    otherLink.classList.remove('expanded');
                    const otherSubmenu = otherLink.closest('.sidebar-nav-item').querySelector('.sidebar-submenu');
                    if (otherSubmenu) {
                        otherSubmenu.classList.remove('expanded');
                    }
                }
            });

            link.classList.add('expanded');
            submenu.classList.add('expanded');
        }
    }

    // ===== BÚSQUEDA EN EL MENÚ =====
    function setupSearch() {
        if (!sidebarSearchInput) return;

        sidebarSearchInput.addEventListener('input', function(e) {
            const query = e.target.value.toLowerCase().trim();

            // Debounce
            clearTimeout(searchDebounceTimer);
            searchDebounceTimer = setTimeout(() => {
                performSearch(query);
            }, CONFIG.DEBOUNCE_DELAY);
        });
    }

    function performSearch(query) {
        const allNavItems = document.querySelectorAll('.sidebar-nav-item');
        const allSubmenuItems = document.querySelectorAll('.sidebar-submenu-item');
        const allCategories = document.querySelectorAll('.sidebar-category');

        if (query === '') {
            // Mostrar todo
            allNavItems.forEach(item => item.style.display = '');
            allSubmenuItems.forEach(item => item.style.display = '');
            allCategories.forEach(cat => cat.style.display = '');
            return;
        }

        let hasResults = false;

        // Buscar en categorías
        allCategories.forEach(category => {
            const categoryTitle = category.querySelector('.sidebar-category-title');
            const categoryText = categoryTitle ? categoryTitle.textContent.toLowerCase() : '';
            const navItems = category.querySelectorAll('.sidebar-nav-item');

            let categoryHasMatch = false;

            // Buscar en items de esta categoría
            navItems.forEach(item => {
                const link = item.querySelector('.sidebar-nav-link');
                const text = link ? link.textContent.toLowerCase() : '';
                const submenuLinks = item.querySelectorAll('.sidebar-submenu-link');

                let itemMatches = text.includes(query);
                let submenuMatches = false;

                // Buscar en submenú
                submenuLinks.forEach(subLink => {
                    const subText = subLink.textContent.toLowerCase();
                    if (subText.includes(query)) {
                        subLink.closest('.sidebar-submenu-item').style.display = '';
                        submenuMatches = true;
                        hasResults = true;
                    } else {
                        subLink.closest('.sidebar-submenu-item').style.display = 'none';
                    }
                });

                if (itemMatches || submenuMatches || categoryText.includes(query)) {
                    item.style.display = '';
                    categoryHasMatch = true;
                    hasResults = true;

                    // Si hay match, expandir submenú si existe
                    if (submenuMatches) {
                        const submenu = item.querySelector('.sidebar-submenu');
                        if (submenu) {
                            submenu.classList.add('expanded');
                            link.classList.add('expanded');
                        }
                    }
                } else {
                    item.style.display = 'none';
                }
            });

            // Mostrar/ocultar categoría
            category.style.display = categoryHasMatch ? '' : 'none';
        });

        // Si no hay resultados, mostrar mensaje (opcional)
        if (!hasResults) {
            console.log('No se encontraron resultados para:', query);
        }
    }

    // ===== TOOLTIPS =====
    function setupTooltips() {
        // Los tooltips se muestran automáticamente con CSS
        // Aquí podríamos agregar lógica adicional si es necesario
    }

    // ===== MARCAR ITEM ACTIVO =====
    function markActiveMenuItem() {
        const currentPath = window.location.pathname;
        const allLinks = document.querySelectorAll('.sidebar-nav-link, .sidebar-submenu-link');

        allLinks.forEach(link => {
            const href = link.getAttribute('href');
            if (href && currentPath.includes(href) && href !== '#') {
                link.classList.add('active');

                // Si es un submenú, expandir el padre
                if (link.classList.contains('sidebar-submenu-link')) {
                    const parentItem = link.closest('.sidebar-nav-item');
                    const parentLink = parentItem.querySelector('.sidebar-nav-link');
                    const parentSubmenu = parentItem.querySelector('.sidebar-submenu');

                    if (parentLink && parentSubmenu) {
                        parentLink.classList.add('expanded');
                        parentSubmenu.classList.add('expanded');
                    }
                }
            }
        });
    }

    // ===== RESPONSIVE =====
    function setupResponsive() {
        handleResize(); // Ejecutar al inicio
    }

    let resizeTimer = null;
    function handleResize() {
        clearTimeout(resizeTimer);
        resizeTimer = setTimeout(() => {
            const width = window.innerWidth;

            // Desktop
            if (width >= CONFIG.MOBILE_BREAKPOINT) {
                closeMobileSidebar();
                sidebar.classList.remove('mobile-open');

                // Restaurar estado colapsado si estaba guardado
                if (isCollapsed) {
                    sidebar.classList.add('collapsed');
                }
            } else {
                // Móvil/Tablet: quitar estado colapsado
                sidebar.classList.remove('collapsed');
            }
        }, 100);
    }

    // ===== PERSISTENCIA DE ESTADO =====
    function saveSidebarState() {
        try {
            localStorage.setItem(CONFIG.STORAGE_KEY, JSON.stringify({
                collapsed: isCollapsed,
                timestamp: Date.now()
            }));
        } catch (e) {
            console.warn('No se pudo guardar el estado del sidebar:', e);
        }
    }

    function loadSidebarState() {
        try {
            const savedState = localStorage.getItem(CONFIG.STORAGE_KEY);
            if (savedState) {
                const state = JSON.parse(savedState);

                // Solo aplicar en desktop
                if (window.innerWidth >= CONFIG.MOBILE_BREAKPOINT) {
                    isCollapsed = state.collapsed || false;
                    if (isCollapsed) {
                        sidebar.classList.add('collapsed');
                    }
                }
            }
        } catch (e) {
            console.warn('No se pudo cargar el estado del sidebar:', e);
        }
    }

    // ===== API PÚBLICA =====
    window.ModernSidebar = {
        toggle: toggleSidebar,
        collapse: function() {
            if (!isCollapsed) toggleSidebar();
        },
        expand: function() {
            if (isCollapsed) toggleSidebar();
        },
        isCollapsed: function() {
            return isCollapsed;
        },
        search: performSearch,
        refresh: markActiveMenuItem
    };

    // ===== INICIAR =====
    init();

})();

// ===== INTEGRACIÓN CON JQUERY (SI ESTÁ DISPONIBLE) =====
if (typeof jQuery !== 'undefined') {
    (function($) {
        $.fn.modernSidebar = function(action) {
            if (window.ModernSidebar && window.ModernSidebar[action]) {
                return window.ModernSidebar[action]();
            }
            return this;
        };
    })(jQuery);
}
