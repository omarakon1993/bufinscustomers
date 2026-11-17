-- =====================================================================================================================
-- 053 · Opción de menú «Informe para comité»
--   INFORMES_COMITE → InformeComiteController (el cierre de un mes como documento para presentar, decidir y firmar)
-- Script MANUAL (compatible con SQL Server 2012). Idempotente: si la opción ya existe no la duplica.
-- Queda en la MISMA categoría/grupo que «Informe mensual de gerencia» (INFORMES_GERENCIA_MENSUAL); si esa opción no existe, en la de «Informe de
-- indicadores» (INFORMES_INDICADORES) o, en último caso, en la categoría «Informes». Después de ejecutarlo, asigne el permiso a los usuarios desde Permisos
-- o con «Habilitar para todos» en el Gestor de menús (/MenuOpciones). El Super Admin la ve siempre.
-- Antes el comité era una vista del Informe mensual de gerencia y usaba su permiso: quien lo necesite debe recibir ahora INFORMES_COMITE.
-- =====================================================================================================================
DECLARE @IdGrupo INT, @IdCategoria INT, @OrdenBase INT;

SELECT TOP 1 @IdGrupo = IdGrupo, @IdCategoria = IdCategoria, @OrdenBase = Orden
FROM dbo.MenuOpciones WHERE Codigo = 'INFORMES_GERENCIA_MENSUAL';

IF @IdCategoria IS NULL
    SELECT TOP 1 @IdGrupo = IdGrupo, @IdCategoria = IdCategoria, @OrdenBase = Orden
    FROM dbo.MenuOpciones WHERE Codigo = 'INFORMES_INDICADORES';

IF @IdCategoria IS NULL
    SELECT TOP 1 @IdCategoria = Id FROM dbo.CategoriasMenu WHERE Nombre = N'Informes';

IF @OrdenBase IS NULL SET @OrdenBase = 12;

IF NOT EXISTS (SELECT 1 FROM dbo.MenuOpciones WHERE Codigo = 'INFORMES_COMITE')
BEGIN
    INSERT INTO dbo.MenuOpciones
        (Codigo, Nombre, NombreEN, Descripcion, Icono, Orden, Controller, [Action], Activo,
         IdGrupo, IdCategoria, SoloSuperAdmin, SoloAdminEmpresa, EsDestacado)
    VALUES
        ('INFORMES_COMITE', N'Informe para comité', N'Committee report',
         N'Resultados, balance y caja, PYG en una hoja, indicadores, líneas de negocio, comentarios, decisiones y firmas del cierre', 'fas fa-file-signature', @OrdenBase + 1,
         'InformeComite', 'Index', 1, @IdGrupo, @IdCategoria, 0, 0, 0);
END
