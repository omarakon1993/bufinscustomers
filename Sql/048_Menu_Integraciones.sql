-- =====================================================================================
-- 048 · Opcion de menu «Integraciones API» (Configuracion)
-- Compatible con SQL Server 2012. MANUAL e idempotente (si la opcion ya existe no hace nada).
--
-- La opcion cuelga del mismo grupo/categoria que «Supuestos del balance del ppto» (o, si no existe, que «Empresas»),
-- es SoloAdminEmpresa (la ven Super Admin y Admin de Empresa; no se puede asignar a un Usuario Normal) y NO es SoloSuperAdmin.
-- Alternativa sin SQL: crearla en Configuracion > Gestor de menus con estos mismos datos (ver el final de este archivo).
-- =====================================================================================

IF NOT EXISTS (SELECT 1 FROM dbo.MenuOpciones WHERE Codigo = 'ADMIN_CONFIG_INTEGRACIONES')
BEGIN
    DECLARE @CodigoHermana VARCHAR(100) =
        (SELECT TOP 1 Codigo FROM dbo.MenuOpciones
          WHERE Codigo IN ('ADMIN_CONFIG_BALANCE_PPTO', 'ADMIN_CONFIG_EMPRESAS')
          ORDER BY CASE Codigo WHEN 'ADMIN_CONFIG_BALANCE_PPTO' THEN 0 ELSE 1 END);

    IF @CodigoHermana IS NULL
    BEGIN
        RAISERROR('No se encontro una opcion hermana (ADMIN_CONFIG_BALANCE_PPTO / ADMIN_CONFIG_EMPRESAS). Cree la opcion desde el Gestor de menus.', 16, 1);
    END
    ELSE
    BEGIN
        INSERT INTO dbo.MenuOpciones
            (Codigo, Nombre, NombreEN, Descripcion, Icono, Orden, Controller, [Action], Activo, IdGrupo, IdCategoria, SoloSuperAdmin, SoloAdminEmpresa, EsDestacado)
        SELECT
            'ADMIN_CONFIG_INTEGRACIONES',
            'Integraciones API',
            'API integrations',
            'Claves para que el sistema de la empresa envie sus datos a BUFINS por la API',
            'fas fa-plug',
            ISNULL((SELECT MAX(o.Orden) FROM dbo.MenuOpciones o
                     WHERE ISNULL(o.IdGrupo, -1) = ISNULL(s.IdGrupo, -1) AND ISNULL(o.IdCategoria, -1) = ISNULL(s.IdCategoria, -1)), 0) + 10,
            'Integraciones',
            'Index',
            1,
            s.IdGrupo,
            s.IdCategoria,
            0,   -- SoloSuperAdmin
            1,   -- SoloAdminEmpresa
            0    -- EsDestacado
        FROM dbo.MenuOpciones s
        WHERE s.Codigo = @CodigoHermana;
    END
END
GO

-- -------------------------------------------------------------------------------------
-- OPCIONAL · habilitar la opcion a los Admin de Empresa que ya existen (el Super Admin la ve siempre).
-- Equivale al boton «Habilitar para todos» del Gestor de menus para una opcion SoloAdminEmpresa.
-- Si prefiere asignarla uno a uno, use Permisos > Usuario y no ejecute este bloque.
-- -------------------------------------------------------------------------------------
-- DECLARE @IdOpcion INT = (SELECT Id FROM dbo.MenuOpciones WHERE Codigo = 'ADMIN_CONFIG_INTEGRACIONES');
-- DECLARE @IdAsigno INT = (SELECT TOP 1 Id FROM dbo.Usuarios WHERE Admin = 2 ORDER BY Id);
-- INSERT INTO dbo.UsuarioMenuPermisos (IdUsuario, IdMenuOpcion, UsuarioAsigno)
-- SELECT u.Id, @IdOpcion, @IdAsigno
-- FROM dbo.Usuarios u
-- WHERE u.Admin = 1
--   AND NOT EXISTS (SELECT 1 FROM dbo.UsuarioMenuPermisos p WHERE p.IdUsuario = u.Id AND p.IdMenuOpcion = @IdOpcion);

-- -------------------------------------------------------------------------------------
-- Datos para crearla a mano en Configuracion > Gestor de menus (si el INSERT de arriba no aplica):
--   Codigo: ADMIN_CONFIG_INTEGRACIONES        Nombre: Integraciones API       Nombre EN: API integrations
--   Controlador: Integraciones                Accion: Index                   Icono: fas fa-plug
--   Mismo grupo/categoria que «Supuestos del balance del ppto»                Solo Admin de Empresa: SI    Solo Super Admin: NO
-- -------------------------------------------------------------------------------------
