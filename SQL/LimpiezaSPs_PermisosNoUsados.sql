-- =============================================
-- Script de limpieza: SPs y tablas no utilizados del sistema de permisos
-- Fecha: 2026-02-11
-- Descripcion: Elimina SPs que ya no son llamados desde codigo C# activo
--              y tablas legacy del sistema anterior de permisos.
-- =============================================

USE bufinscustomers;
GO

PRINT '=== Eliminando SPs no utilizados del sistema MenuOpciones ==='

-- SPs del sistema MenuOpciones que ya no se llaman desde codigo
IF OBJECT_ID('sp_ObtenerTodasLasOpcionesMenu', 'P') IS NOT NULL
    DROP PROCEDURE sp_ObtenerTodasLasOpcionesMenu;
GO
PRINT 'Eliminado: sp_ObtenerTodasLasOpcionesMenu (reemplazado por sp_ObtenerMenuUsuario)'

IF OBJECT_ID('sp_ObtenerOpcionesMenuUsuario', 'P') IS NOT NULL
    DROP PROCEDURE sp_ObtenerOpcionesMenuUsuario;
GO
PRINT 'Eliminado: sp_ObtenerOpcionesMenuUsuario (reemplazado por sp_ObtenerCodigosPermisosUsuario)'

IF OBJECT_ID('sp_VerificarAccesoMenuUsuario', 'P') IS NOT NULL
    DROP PROCEDURE sp_VerificarAccesoMenuUsuario;
GO
PRINT 'Eliminado: sp_VerificarAccesoMenuUsuario (reemplazado por cache HashSet en TienePermiso)'

IF OBJECT_ID('sp_AsignarOpcionMenuUsuario', 'P') IS NOT NULL
    DROP PROCEDURE sp_AsignarOpcionMenuUsuario;
GO
PRINT 'Eliminado: sp_AsignarOpcionMenuUsuario (reemplazado por GuardarOpcionesUsuario con SQL directo)'

IF OBJECT_ID('sp_RevocarOpcionMenuUsuario', 'P') IS NOT NULL
    DROP PROCEDURE sp_RevocarOpcionMenuUsuario;
GO
PRINT 'Eliminado: sp_RevocarOpcionMenuUsuario (reemplazado por GuardarOpcionesUsuario con SQL directo)'

IF OBJECT_ID('sp_CrearOpcionMenu', 'P') IS NOT NULL
    DROP PROCEDURE sp_CrearOpcionMenu;
GO
PRINT 'Eliminado: sp_CrearOpcionMenu (nunca referenciado en codigo C#)'

PRINT ''
PRINT '=== Eliminando SPs legacy del sistema viejo de permisos ==='

IF OBJECT_ID('sp_ObtenerTodosLosPermisos', 'P') IS NOT NULL
    DROP PROCEDURE sp_ObtenerTodosLosPermisos;
GO
PRINT 'Eliminado: sp_ObtenerTodosLosPermisos (SP legacy)'

IF OBJECT_ID('sp_ObtenerPermisosUsuario', 'P') IS NOT NULL
    DROP PROCEDURE sp_ObtenerPermisosUsuario;
GO
PRINT 'Eliminado: sp_ObtenerPermisosUsuario (SP legacy)'

IF OBJECT_ID('sp_ObtenerPermisosUsuarioConEstado', 'P') IS NOT NULL
    DROP PROCEDURE sp_ObtenerPermisosUsuarioConEstado;
GO
PRINT 'Eliminado: sp_ObtenerPermisosUsuarioConEstado (SP legacy)'

IF OBJECT_ID('sp_AsignarPermisoUsuario', 'P') IS NOT NULL
    DROP PROCEDURE sp_AsignarPermisoUsuario;
GO
PRINT 'Eliminado: sp_AsignarPermisoUsuario (SP legacy)'

IF OBJECT_ID('sp_RevocarPermisoUsuario', 'P') IS NOT NULL
    DROP PROCEDURE sp_RevocarPermisoUsuario;
GO
PRINT 'Eliminado: sp_RevocarPermisoUsuario (SP legacy)'

IF OBJECT_ID('sp_VerificarPermisoUsuario', 'P') IS NOT NULL
    DROP PROCEDURE sp_VerificarPermisoUsuario;
GO
PRINT 'Eliminado: sp_VerificarPermisoUsuario (SP legacy)'

PRINT ''
PRINT '=== Eliminando tablas legacy (si existen) ==='

IF OBJECT_ID('PermisosModulos', 'U') IS NOT NULL
    DROP TABLE PermisosModulos;
GO
PRINT 'Eliminada: PermisosModulos (tabla vieja, renombrada a MenuOpciones)'

IF OBJECT_ID('UsuarioPermisos', 'U') IS NOT NULL
    DROP TABLE UsuarioPermisos;
GO
PRINT 'Eliminada: UsuarioPermisos (tabla vieja, renombrada a UsuarioMenuPermisos)'

PRINT ''
PRINT '=== Limpieza completada ==='
PRINT 'SPs activos que permanecen:'
PRINT '  - sp_ObtenerMenuUsuario (sidebar dinamico)'
PRINT '  - sp_ObtenerCodigosPermisosUsuario (cache TienePermiso)'
PRINT '  - sp_ObtenerOpcionesMenuUsuarioConEstado (vista Gestionar)'
PRINT 'Tablas activas que permanecen:'
PRINT '  - MenuOpciones'
PRINT '  - UsuarioMenuPermisos'
