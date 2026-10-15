/* ============================================================================
   032 - Opcion de menu "Documentacion BUFINS" (biblioteca de documentacion, solo Super Admin)
   ----------------------------------------------------------------------------
   Script MANUAL. SQL Server 2012+. Se puede ejecutar varias veces.
   Crea la opcion junto a "Configuraciones generales BUFINS" (copia su grupo y categoria,
   es decir, queda en Configuracion). Si no existe esa opcion (script 027), crea esta
   desde /MenuOpciones: controlador DocumentacionBufins, accion Index, codigo
   ADMIN_DOCUMENTACION_BUFINS, solo Super Admin.
   ============================================================================ */

IF NOT EXISTS (SELECT 1 FROM dbo.MenuOpciones WHERE Codigo = 'ADMIN_DOCUMENTACION_BUFINS')
BEGIN
    INSERT INTO dbo.MenuOpciones
        (Codigo, Nombre, NombreEN, Descripcion, Icono, Orden, Controller, [Action], Activo,
         IdGrupo, IdCategoria, SoloSuperAdmin, SoloAdminEmpresa, EsDestacado)
    SELECT TOP 1
        'ADMIN_DOCUMENTACION_BUFINS',
        'Documentacion BUFINS',
        'BUFINS documentation',
        'Biblioteca con toda la documentacion del sistema (solo Super Admin)',
        'fas fa-book',
        m.Orden + 2,
        'DocumentacionBufins', 'Index', 1,
        m.IdGrupo, m.IdCategoria, 1, 0, 0
    FROM dbo.MenuOpciones m
    WHERE m.Controller = 'ConfiguracionGlobal'
    ORDER BY m.Id;

    IF @@ROWCOUNT = 0
        PRINT 'No se encontro la opcion "Configuraciones generales BUFINS" (ejecuta antes el 027). Crea la opcion desde /MenuOpciones.';
END
GO

SELECT Id, Codigo, Nombre, NombreEN, Controller, [Action], SoloSuperAdmin FROM dbo.MenuOpciones WHERE Codigo = 'ADMIN_DOCUMENTACION_BUFINS';
GO
