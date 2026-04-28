/**
 * BUFINS MODERN SIDEBAR
 * Sistema de menú lateral moderno, responsive e híbrido
 * @version 2.0.0
 */

(function() {
    'use strict';

    // ===== CONFIGURACIÓN =====
    const CONFIG = {
        STORAGE_KEY: 'bufins_sidebar_state',
        DEBOUNCE_DELAY: 250,
        ANIMATION_DURATION: 200,
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

    // Tooltip flotante
    let activeTooltip = null;
    let tooltipHideTimer = null;

    // Flyout submenu (sidebar colapsado)
    let activeFlyout = null;
    let activeFlyoutNavItem = null;

    // ===== INICIALIZACIÓN =====
    function init() {
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', setupSidebar);
        } else {
            setupSidebar();
        }
    }

    function setupSidebar() {
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

        loadSidebarState();
        setupEventListeners();
        setupSearch();
        setupTooltips();
        setupResponsive();
        markActiveMenuItem();
    }

    // ===== EVENT LISTENERS =====
    function setupEventListeners() {
        if (sidebarToggleBtn) {
            sidebarToggleBtn.addEventListener('click', toggleSidebar);
        }

        if (sidebarHamburger) {
            sidebarHamburger.addEventListener('click', toggleMobileSidebar);
        }

        if (sidebarBackdrop) {
            sidebarBackdrop.addEventListener('click', closeMobileSidebar);
        }

        const navLinksWithSubmenu = document.querySelectorAll('.sidebar-nav-link[data-has-submenu="true"]');
        navLinksWithSubmenu.forEach(link => {
            link.addEventListener('click', handleSubmenuToggle);
        });

        if (sidebar) {
            sidebar.addEventListener('click', function(e) {
                e.stopPropagation();
            });
        }

        window.addEventListener('resize', handleResize);

        document.addEventListener('keydown', function(e) {
            if (e.key === 'Escape') {
                if (activeFlyout) closeFlyout();
                if (isMobileOpen) closeMobileSidebar();
            }
        });
    }

    // ===== TOGGLE SIDEBAR (DESKTOP) =====
    function toggleSidebar() {
        if (window.innerWidth < CONFIG.MOBILE_BREAKPOINT) return;

        // Cerrar flyout y tooltip al colapsar/expandir
        closeFlyout();
        hideNavTooltip();

        isCollapsed = !isCollapsed;

        if (isCollapsed) {
            sidebar.classList.add('collapsed');
        } else {
            sidebar.classList.remove('collapsed');
        }

        saveSidebarState();

        window.dispatchEvent(new CustomEvent('sidebarToggled', {
            detail: { collapsed: isCollapsed }
        }));
    }

    // ===== TOGGLE MOBILE SIDEBAR =====
    function toggleMobileSidebar() {
        if (window.innerWidth >= CONFIG.MOBILE_BREAKPOINT) return;

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
        document.body.style.overflow = 'hidden';

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

        // Modo colapsado desktop: mostrar panel flyout lateral
        if (isCollapsed && window.innerWidth >= CONFIG.MOBILE_BREAKPOINT) {
            if (activeFlyoutNavItem === navItem) {
                closeFlyout(); // toggle: ya estaba abierto → cerrar
            } else {
                showFlyout(navItem, link);
            }
            return;
        }

        // Modo expandido: accordion normal
        const isExpanded = link.classList.contains('expanded');

        if (isExpanded) {
            link.classList.remove('expanded');
            submenu.classList.remove('expanded');
        } else {
            // Cerrar otros (accordion)
            document.querySelectorAll('.sidebar-nav-link.expanded').forEach(otherLink => {
                if (otherLink !== link) {
                    otherLink.classList.remove('expanded');
                    const otherSubmenu = otherLink.closest('.sidebar-nav-item').querySelector('.sidebar-submenu');
                    if (otherSubmenu) otherSubmenu.classList.remove('expanded');
                }
            });

            link.classList.add('expanded');
            submenu.classList.add('expanded');
        }
    }

    // ===== FLYOUT SUBMENU (sidebar colapsado) =====
    function showFlyout(navItem, link) {
        closeFlyout();
        hideNavTooltip();

        const submenu = navItem.querySelector('.sidebar-submenu');
        if (!submenu) return;

        var navTextEl = link.querySelector('.sidebar-nav-text');
        const groupName = navTextEl ? navTextEl.textContent.trim() : '';
        const sidebarRect = sidebar.getBoundingClientRect();
        const navItemRect = navItem.getBoundingClientRect();

        const flyout = document.createElement('div');
        flyout.className = 'sidebar-flyout';

        // Cabecera del grupo
        const header = document.createElement('div');
        header.className = 'sidebar-flyout-header';
        header.textContent = groupName;
        flyout.appendChild(header);

        // Items del submenú (clonar los links existentes)
        const itemsContainer = document.createElement('div');
        itemsContainer.className = 'sidebar-flyout-items';

        submenu.querySelectorAll('.sidebar-submenu-link').forEach(subLink => {
            const clone = subLink.cloneNode(true);
            itemsContainer.appendChild(clone);
        });

        flyout.appendChild(itemsContainer);

        // Posición inicial: pegado al borde derecho del sidebar, alineado con el nav-item
        flyout.style.left = sidebarRect.right + 'px';
        flyout.style.top = navItemRect.top + 'px';

        document.body.appendChild(flyout);
        activeFlyout = flyout;
        activeFlyoutNavItem = navItem;

        // Ajustar si el flyout se sale del viewport por abajo
        const flyoutRect = flyout.getBoundingClientRect();
        if (flyoutRect.bottom > window.innerHeight - 8) {
            flyout.style.top = Math.max(8, window.innerHeight - flyoutRect.height - 8) + 'px';
        }

        // Marcar el link como activo con el flyout
        link.classList.add('flyout-open');

        // Cerrar al hacer clic fuera (diferido para no capturar el clic que lo abrió)
        setTimeout(() => {
            document.addEventListener('click', onDocumentClickCloseFlyout);
        }, 0);
    }

    function closeFlyout() {
        if (activeFlyout) {
            activeFlyout.remove();
            activeFlyout = null;
        }
        if (activeFlyoutNavItem) {
            const link = activeFlyoutNavItem.querySelector('.sidebar-nav-link');
            if (link) link.classList.remove('flyout-open');
            activeFlyoutNavItem = null;
        }
        document.removeEventListener('click', onDocumentClickCloseFlyout);
    }

    function onDocumentClickCloseFlyout(e) {
        if (activeFlyout && !activeFlyout.contains(e.target)) {
            closeFlyout();
        }
    }

    // ===== TOOLTIPS FLOTANTES (sidebar colapsado, JS/position:fixed) =====
    function setupTooltips() {
        document.querySelectorAll('.sidebar-nav-link').forEach(link => {
            link.addEventListener('mouseenter', onNavLinkEnter);
            link.addEventListener('mouseleave', onNavLinkLeave);
        });

        // También en category headers
        document.querySelectorAll('.sidebar-category-header').forEach(header => {
            header.addEventListener('mouseenter', onCategoryHeaderEnter);
            header.addEventListener('mouseleave', onNavLinkLeave);
        });
    }

    function onNavLinkEnter(e) {
        if (!isCollapsed || window.innerWidth < CONFIG.MOBILE_BREAKPOINT) return;
        // No mostrar tooltip si ya hay un flyout abierto para este item
        const navItem = e.currentTarget.closest('.sidebar-nav-item');
        if (navItem && navItem === activeFlyoutNavItem) return;

        clearTimeout(tooltipHideTimer);
        showNavTooltip(e.currentTarget, 'link');
    }

    function onCategoryHeaderEnter(e) {
        if (!isCollapsed || window.innerWidth < CONFIG.MOBILE_BREAKPOINT) return;
        clearTimeout(tooltipHideTimer);
        showNavTooltip(e.currentTarget, 'category');
    }

    function onNavLinkLeave() {
        clearTimeout(tooltipHideTimer);
        tooltipHideTimer = setTimeout(hideNavTooltip, 80);
    }

    function showNavTooltip(el, type) {
        hideNavTooltip();

        let text = '';
        if (type === 'link') {
            var elNavText = el.querySelector('.sidebar-nav-text');
            text = elNavText ? elNavText.textContent.trim() : '';
        } else if (type === 'category') {
            var elCatTitle = el.querySelector('.sidebar-category-title');
            text = elCatTitle ? elCatTitle.textContent.trim() : '';
        }

        if (!text) return;

        const rect = el.getBoundingClientRect();
        const sidebarRect = sidebar.getBoundingClientRect();

        const tooltip = document.createElement('div');
        tooltip.className = 'sidebar-tooltip-float';
        tooltip.textContent = text;
        document.body.appendChild(tooltip);

        // Centrar verticalmente respecto al elemento
        const tooltipH = tooltip.offsetHeight;
        const top = rect.top + rect.height / 2 - tooltipH / 2;
        tooltip.style.top = Math.max(8, top) + 'px';
        tooltip.style.left = (sidebarRect.right + 10) + 'px';

        activeTooltip = tooltip;
    }

    function hideNavTooltip() {
        if (activeTooltip) {
            activeTooltip.remove();
            activeTooltip = null;
        }
    }

    // ===== BÚSQUEDA EN EL MENÚ =====
    function setupSearch() {
        if (!sidebarSearchInput) return;

        sidebarSearchInput.addEventListener('input', function(e) {
            const query = e.target.value.toLowerCase().trim();

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
            allNavItems.forEach(item => item.style.display = '');
            allSubmenuItems.forEach(item => item.style.display = '');
            allCategories.forEach(cat => cat.style.display = '');
            return;
        }

        let hasResults = false;

        allCategories.forEach(category => {
            const categoryTitle = category.querySelector('.sidebar-category-title');
            const categoryText = categoryTitle ? categoryTitle.textContent.toLowerCase() : '';
            const navItems = category.querySelectorAll('.sidebar-nav-item');

            let categoryHasMatch = false;

            navItems.forEach(item => {
                const link = item.querySelector('.sidebar-nav-link');
                const text = link ? link.textContent.toLowerCase() : '';
                const submenuLinks = item.querySelectorAll('.sidebar-submenu-link');

                let itemMatches = text.includes(query);
                let submenuMatches = false;

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

            category.style.display = categoryHasMatch ? '' : 'none';
        });
    }

    // ===== MARCAR ITEM ACTIVO =====
    function markActiveMenuItem() {
        const currentPath = window.location.pathname;
        const allLinks = document.querySelectorAll('.sidebar-nav-link, .sidebar-submenu-link');

        allLinks.forEach(link => {
            const href = link.getAttribute('href');
            if (href && currentPath.includes(href) && href !== '#') {
                link.classList.add('active');

                if (link.classList.contains('sidebar-submenu-link')) {
                    const parentItem = link.closest('.sidebar-nav-item');
                    if (parentItem) {
                        const parentLink = parentItem.querySelector('.sidebar-nav-link');
                        const parentSubmenu = parentItem.querySelector('.sidebar-submenu');

                        if (parentLink && parentSubmenu) {
                            parentLink.classList.add('expanded');
                            parentSubmenu.classList.add('expanded');
                        }
                    }
                }
            }
        });
    }

    // ===== RESPONSIVE =====
    function setupResponsive() {
        handleResize();
    }

    let resizeTimer = null;
    function handleResize() {
        clearTimeout(resizeTimer);
        resizeTimer = setTimeout(() => {
            const width = window.innerWidth;

            // Cerrar flyout y tooltip al cambiar tamaño
            closeFlyout();
            hideNavTooltip();

            if (width >= CONFIG.MOBILE_BREAKPOINT) {
                closeMobileSidebar();
                sidebar.classList.remove('mobile-open');

                if (isCollapsed) {
                    sidebar.classList.add('collapsed');
                }
            } else {
                sidebar.classList.remove('collapsed');
            }
        }, 50);
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
        collapse: function() { if (!isCollapsed) toggleSidebar(); },
        expand: function() { if (isCollapsed) toggleSidebar(); },
        isCollapsed: function() { return isCollapsed; },
        search: performSearch,
        refresh: markActiveMenuItem
    };

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
