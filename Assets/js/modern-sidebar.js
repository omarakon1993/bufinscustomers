/**
 * BUFINS MODERN SIDEBAR
 * @version 3.0.0
 */

(function() {
    'use strict';

    // ===== CONFIGURACIÓN =====
    const CONFIG = {
        STORAGE_KEY: 'bufins_sidebar_state',
        SCROLL_KEY: 'bufins_sidebar_scroll',
        DEBOUNCE_DELAY: 250,
        ANIMATION_DURATION: 200,
        MOBILE_BREAKPOINT: 992
    };

    // ===== ESTADO =====
    let sidebar = null;
    let sidebarToggleBtn = null;
    let sidebarHamburger = null;
    let sidebarBackdrop = null;
    let sidebarSearchInput = null;
    let sidebarMenu = null;
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
        sidebar        = document.querySelector('.modern-sidebar');
        sidebarToggleBtn  = document.querySelector('.sidebar-toggle-btn');
        sidebarHamburger  = document.querySelector('.sidebar-hamburger');
        sidebarBackdrop   = document.querySelector('.sidebar-backdrop');
        sidebarSearchInput = document.querySelector('.sidebar-search-input');
        sidebarMenu    = document.querySelector('.sidebar-menu');
        mainContent    = document.querySelector('.main-content');

        if (!sidebar) return;

        // Freeze transitions: prevents any animation triggered by JS changes post-paint.
        // Active/expanded state is already in HTML (server-side Razor), so this is mostly
        // a safety net for edge cases where markActiveMenuItem() still has work to do.
        sidebar.classList.add('sidebar-initializing');
        if (mainContent) mainContent.style.transition = 'none';

        loadSidebarState();
        setupEventListeners();
        setupSearch();
        setupTooltips();
        markActiveMenuItem();

        // Two rAFs: let browser settle the current frame, then unfreeze on the next
        requestAnimationFrame(function() {
            requestAnimationFrame(function() {
                sidebar.classList.remove('sidebar-initializing');
                if (mainContent) mainContent.style.transition = '';
                document.documentElement.classList.remove('sidebar-init-collapsed');
                document.documentElement.classList.remove('page-loading');
            });
        });
    }

    // ===== EVENT LISTENERS =====
    function setupEventListeners() {
        if (sidebarToggleBtn) sidebarToggleBtn.addEventListener('click', toggleSidebar);
        if (sidebarHamburger) sidebarHamburger.addEventListener('click', toggleMobileSidebar);
        if (sidebarBackdrop)  sidebarBackdrop.addEventListener('click', closeMobileSidebar);

        document.querySelectorAll('.sidebar-nav-link[data-has-submenu="true"]').forEach(link => {
            link.addEventListener('click', handleSubmenuToggle);
        });

        // Stop sidebar clicks from reaching document (would close flyouts unintentionally)
        sidebar.addEventListener('click', function(e) { e.stopPropagation(); });

        // Save scroll position just before navigation — covers all link types including flyout clones
        window.addEventListener('pagehide', saveScroll);
        window.addEventListener('beforeunload', saveScroll);

        window.addEventListener('resize', handleResize);

        document.addEventListener('keydown', function(e) {
            if (e.key === 'Escape') {
                if (activeFlyout) closeFlyout();
                if (isMobileOpen) closeMobileSidebar();
            }
        });
    }

    function saveScroll() {
        try {
            if (sidebarMenu) sessionStorage.setItem(CONFIG.SCROLL_KEY, sidebarMenu.scrollTop);
        } catch (e) {}
    }

    // ===== TOGGLE SIDEBAR (DESKTOP) =====
    function toggleSidebar() {
        if (window.innerWidth < CONFIG.MOBILE_BREAKPOINT) return;
        closeFlyout();
        hideNavTooltip();

        isCollapsed = !isCollapsed;
        sidebar.classList.toggle('collapsed', isCollapsed);
        saveSidebarState();

        window.dispatchEvent(new CustomEvent('sidebarToggled', { detail: { collapsed: isCollapsed } }));
    }

    // ===== TOGGLE MOBILE SIDEBAR =====
    function toggleMobileSidebar() {
        if (window.innerWidth >= CONFIG.MOBILE_BREAKPOINT) return;
        if (isMobileOpen) {
            closeMobileSidebar();
        } else {
            openMobileSidebar();
            isMobileOpen = true;
        }
    }

    function openMobileSidebar() {
        sidebar.classList.add('mobile-open', 'animate-in');
        sidebarBackdrop.classList.add('active');
        document.body.style.overflow = 'hidden';
        setTimeout(() => sidebar.classList.remove('animate-in'), CONFIG.ANIMATION_DURATION);
    }

    function closeMobileSidebar() {
        sidebar.classList.remove('mobile-open');
        if (sidebarBackdrop) sidebarBackdrop.classList.remove('active');
        document.body.style.overflow = '';
        isMobileOpen = false;
    }

    // ===== SUBMENÚS =====
    function handleSubmenuToggle(e) {
        e.preventDefault();
        const link = e.currentTarget;
        const navItem = link.closest('.sidebar-nav-item');
        const submenu = navItem.querySelector('.sidebar-submenu');
        if (!submenu) return;

        // Modo colapsado → flyout lateral
        if (isCollapsed && window.innerWidth >= CONFIG.MOBILE_BREAKPOINT) {
            activeFlyoutNavItem === navItem ? closeFlyout() : showFlyout(navItem, link);
            return;
        }

        // Modo expandido → accordion
        const isExpanded = link.classList.contains('expanded');
        if (isExpanded) {
            link.classList.remove('expanded');
            submenu.classList.remove('expanded');
        } else {
            // Cerrar otros abiertos
            document.querySelectorAll('.sidebar-nav-link.expanded').forEach(other => {
                if (other !== link) {
                    other.classList.remove('expanded');
                    const otherSub = other.closest('.sidebar-nav-item').querySelector('.sidebar-submenu');
                    if (otherSub) otherSub.classList.remove('expanded');
                }
            });
            link.classList.add('expanded');
            submenu.classList.add('expanded');
        }
    }

    // ===== FLYOUT (sidebar colapsado) =====
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

        const header = document.createElement('div');
        header.className = 'sidebar-flyout-header';
        header.textContent = groupName;
        flyout.appendChild(header);

        const itemsContainer = document.createElement('div');
        itemsContainer.className = 'sidebar-flyout-items';
        submenu.querySelectorAll('.sidebar-submenu-link').forEach(subLink => {
            itemsContainer.appendChild(subLink.cloneNode(true));
        });
        flyout.appendChild(itemsContainer);

        flyout.style.left = sidebarRect.right + 'px';
        flyout.style.top  = navItemRect.top + 'px';
        document.body.appendChild(flyout);

        activeFlyout = flyout;
        activeFlyoutNavItem = navItem;

        // Ajustar si se sale del viewport por abajo
        const flyoutRect = flyout.getBoundingClientRect();
        if (flyoutRect.bottom > window.innerHeight - 8) {
            flyout.style.top = Math.max(8, window.innerHeight - flyoutRect.height - 8) + 'px';
        }

        link.classList.add('flyout-open');

        setTimeout(() => document.addEventListener('click', onDocumentClickCloseFlyout), 0);
    }

    function closeFlyout() {
        if (activeFlyout) { activeFlyout.remove(); activeFlyout = null; }
        if (activeFlyoutNavItem) {
            const link = activeFlyoutNavItem.querySelector('.sidebar-nav-link');
            if (link) link.classList.remove('flyout-open');
            activeFlyoutNavItem = null;
        }
        document.removeEventListener('click', onDocumentClickCloseFlyout);
    }

    function onDocumentClickCloseFlyout(e) {
        if (activeFlyout && !activeFlyout.contains(e.target)) closeFlyout();
    }

    // ===== TOOLTIPS FLOTANTES (sidebar colapsado) =====
    function setupTooltips() {
        document.querySelectorAll('.sidebar-nav-link').forEach(link => {
            link.addEventListener('mouseenter', onNavLinkEnter);
            link.addEventListener('mouseleave', onNavLinkLeave);
        });
        document.querySelectorAll('.sidebar-category-header').forEach(header => {
            header.addEventListener('mouseenter', onCategoryHeaderEnter);
            header.addEventListener('mouseleave', onNavLinkLeave);
        });
    }

    function onNavLinkEnter(e) {
        if (!isCollapsed || window.innerWidth < CONFIG.MOBILE_BREAKPOINT) return;
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
        var text = '';
        if (type === 'link') {
            var navText = el.querySelector('.sidebar-nav-text');
            text = navText ? navText.textContent.trim() : '';
        } else {
            var catTitle = el.querySelector('.sidebar-category-title');
            text = catTitle ? catTitle.textContent.trim() : '';
        }
        if (!text) return;

        const rect = el.getBoundingClientRect();
        const sidebarRect = sidebar.getBoundingClientRect();
        const tooltip = document.createElement('div');
        tooltip.className = 'sidebar-tooltip-float';
        tooltip.textContent = text;
        document.body.appendChild(tooltip);

        const tooltipH = tooltip.offsetHeight;
        tooltip.style.top  = Math.max(8, rect.top + rect.height / 2 - tooltipH / 2) + 'px';
        tooltip.style.left = (sidebarRect.right + 10) + 'px';
        activeTooltip = tooltip;
    }

    function hideNavTooltip() {
        if (activeTooltip) { activeTooltip.remove(); activeTooltip = null; }
    }

    // ===== BÚSQUEDA =====
    function setupSearch() {
        if (!sidebarSearchInput) return;
        sidebarSearchInput.addEventListener('input', function(e) {
            const query = e.target.value.toLowerCase().trim();
            clearTimeout(searchDebounceTimer);
            searchDebounceTimer = setTimeout(() => performSearch(query), CONFIG.DEBOUNCE_DELAY);
        });
    }

    function performSearch(query) {
        const allCategories = document.querySelectorAll('.sidebar-category');
        if (query === '') {
            document.querySelectorAll('.sidebar-nav-item, .sidebar-submenu-item').forEach(el => el.style.display = '');
            allCategories.forEach(cat => cat.style.display = '');
            return;
        }

        allCategories.forEach(category => {
            const catText = (category.querySelector('.sidebar-category-title') || {}).textContent || '';
            const navItems = category.querySelectorAll('.sidebar-nav-item');
            let categoryHasMatch = false;

            navItems.forEach(item => {
                const link = item.querySelector('.sidebar-nav-link');
                const text = link ? link.textContent.toLowerCase() : '';
                const submenuLinks = item.querySelectorAll('.sidebar-submenu-link');
                let submenuMatches = false;

                submenuLinks.forEach(subLink => {
                    const matches = subLink.textContent.toLowerCase().includes(query);
                    subLink.closest('.sidebar-submenu-item').style.display = matches ? '' : 'none';
                    if (matches) submenuMatches = true;
                });

                if (text.includes(query) || submenuMatches || catText.toLowerCase().includes(query)) {
                    item.style.display = '';
                    categoryHasMatch = true;
                    if (submenuMatches) {
                        const submenu = item.querySelector('.sidebar-submenu');
                        if (submenu) { submenu.classList.add('expanded'); if (link) link.classList.add('expanded'); }
                    }
                } else {
                    item.style.display = 'none';
                }
            });

            category.style.display = categoryHasMatch ? '' : 'none';
        });
    }

    // ===== MARCAR ITEM ACTIVO (fallback — Razor ya emite las clases server-side) =====
    function markActiveMenuItem() {
        const currentPath = window.location.pathname;
        document.querySelectorAll('.sidebar-nav-link, .sidebar-submenu-link').forEach(link => {
            const href = link.getAttribute('href');
            if (!href || href === '#' || !pathMatchesHref(currentPath, href)) return;
            link.classList.add('active');
            if (link.classList.contains('sidebar-submenu-link')) {
                const parentItem = link.closest('.sidebar-nav-item');
                if (parentItem) {
                    const parentLink   = parentItem.querySelector('.sidebar-nav-link');
                    const parentSubmenu = parentItem.querySelector('.sidebar-submenu');
                    if (parentLink)   parentLink.classList.add('expanded');
                    if (parentSubmenu) parentSubmenu.classList.add('expanded');
                }
            }
        });
    }

    function pathMatchesHref(currentPath, href) {
        var normalHref = href.replace(/\/$/, '') || '/';
        var normalPath = currentPath.replace(/\/$/, '') || '/';
        return normalPath === normalHref || normalPath.indexOf(normalHref + '/') === 0;
    }

    // ===== RESPONSIVE =====
    let resizeTimer = null;
    function handleResize() {
        clearTimeout(resizeTimer);
        resizeTimer = setTimeout(function() {
            closeFlyout();
            hideNavTooltip();
            if (window.innerWidth >= CONFIG.MOBILE_BREAKPOINT) {
                closeMobileSidebar();
                sidebar.classList.toggle('collapsed', isCollapsed);
            } else {
                sidebar.classList.remove('collapsed');
            }
        }, 50);
    }

    // ===== PERSISTENCIA DE ESTADO =====
    function saveSidebarState() {
        try {
            localStorage.setItem(CONFIG.STORAGE_KEY, JSON.stringify({ collapsed: isCollapsed, timestamp: Date.now() }));
        } catch (e) {}
    }

    function loadSidebarState() {
        try {
            const saved = localStorage.getItem(CONFIG.STORAGE_KEY);
            if (saved && window.innerWidth >= CONFIG.MOBILE_BREAKPOINT) {
                const state = JSON.parse(saved);
                isCollapsed = state.collapsed || false;
                if (isCollapsed) sidebar.classList.add('collapsed');
            }
        } catch (e) {}
    }

    // ===== API PÚBLICA =====
    window.ModernSidebar = {
        toggle:    toggleSidebar,
        collapse:  function() { if (!isCollapsed) toggleSidebar(); },
        expand:    function() { if (isCollapsed)  toggleSidebar(); },
        isCollapsed: function() { return isCollapsed; },
        search:    performSearch,
        refresh:   markActiveMenuItem
    };

    init();

})();

// ===== INTEGRACIÓN JQUERY =====
if (typeof jQuery !== 'undefined') {
    (function($) {
        $.fn.modernSidebar = function(action) {
            if (window.ModernSidebar && window.ModernSidebar[action]) return window.ModernSidebar[action]();
            return this;
        };
    })(jQuery);
}
