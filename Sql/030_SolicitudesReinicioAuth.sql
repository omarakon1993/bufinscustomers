/* ============================================================================
   030 - SOLICITUDES DE REINICIO DE METODOS DE AUTENTICACION
   ----------------------------------------------------------------------------
   Script MANUAL. SQL Server 2012+. Se puede ejecutar varias veces.
   Cuando un usuario pierde su app autenticadora y sus passkeys, envia una solicitud desde
   la pantalla de verificacion del 2FA; los Super Admin la revisan y, tras verificar la
   identidad del usuario por otro canal, la aprueban (reinicia sus metodos) o la rechazan.

   PARTE 1 - Tabla de solicitudes.
   PARTE 2 - Opcion de menu "Solicitudes de seguridad" (solo Super Admin), ubicada junto a
             "Configuraciones generales BUFINS" (copia su grupo y categoria).
   ============================================================================ */


/* ============================ PARTE 1 - TABLA ============================= */

IF OBJECT_ID('dbo.SolicitudesReinicioAuth', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SolicitudesReinicioAuth
    (
        Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SolicitudesReinicioAuth PRIMARY KEY,
        IdUsuario       INT            NOT NULL,                 -- usuario que pide el reinicio
        Fecha           DATETIME       NOT NULL CONSTRAINT DF_SolReinicioAuth_Fecha DEFAULT (GETDATE()),
        Estado          VARCHAR(20)    NOT NULL CONSTRAINT DF_SolReinicioAuth_Estado DEFAULT ('Pendiente'),  -- Pendiente | Aprobada | Rechazada
        Mensaje         NVARCHAR(500)  NULL,
        Telefono        NVARCHAR(40)   NULL,
        IP              VARCHAR(64)    NULL,
        UserAgent       NVARCHAR(300)  NULL,
        IdResuelve      INT            NULL,                     -- Super Admin que la resolvio
        FechaResolucion DATETIME       NULL,
        NotaResolucion  NVARCHAR(500)  NULL
    );
    CREATE INDEX IX_SolReinicioAuth_Estado ON dbo.SolicitudesReinicioAuth (Estado, Fecha DESC);
    CREATE INDEX IX_SolReinicioAuth_Usuario ON dbo.SolicitudesReinicioAuth (IdUsuario, Estado);
END
GO


/* ============================ PARTE 2 - MENU ============================== */

IF NOT EXISTS (SELECT 1 FROM dbo.MenuOpciones WHERE Codigo = 'ADMIN_SOLICITUDES_SEGURIDAD')
BEGIN
    INSERT INTO dbo.MenuOpciones
        (Codigo, Nombre, NombreEN, Descripcion, Icono, Orden, Controller, [Action], Activo,
         IdGrupo, IdCategoria, SoloSuperAdmin, SoloAdminEmpresa, EsDestacado)
    SELECT TOP 1
        'ADMIN_SOLICITUDES_SEGURIDAD',
        'Solicitudes de seguridad',
        'Security requests',
        'Solicitudes de reinicio de metodos de autenticacion de los usuarios',
        'fas fa-user-shield',
        m.Orden + 1,
        'SolicitudesSeguridad', 'Index', 1,
        m.IdGrupo, m.IdCategoria, 1, 0, 0
    FROM dbo.MenuOpciones m
    WHERE m.Controller = 'ConfiguracionGlobal'
    ORDER BY m.Id;

    IF @@ROWCOUNT = 0
        PRINT 'No se encontro la opcion "Configuraciones generales BUFINS" (ejecuta antes el 027). Crea la opcion desde /MenuOpciones: controlador SolicitudesSeguridad, accion Index, codigo ADMIN_SOLICITUDES_SEGURIDAD, solo Super Admin.';
END
GO

SELECT Id, Codigo, Nombre, NombreEN, Controller, [Action], SoloSuperAdmin FROM dbo.MenuOpciones WHERE Codigo = 'ADMIN_SOLICITUDES_SEGURIDAD';
GO
