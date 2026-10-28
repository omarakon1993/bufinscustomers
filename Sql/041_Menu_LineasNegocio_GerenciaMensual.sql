-- =====================================================================================================================
-- 041 · Opciones de menú de los informes nuevos
--   INFORMES_LINEAS_NEGOCIO   → InformeLineasNegocioController (Rentabilidad por línea de negocio)
--   INFORMES_GERENCIA_MENSUAL → InformeGerenciaMensualController (Informe mensual de gerencia)
-- Script MANUAL (compatible con SQL Server 2012). Es idempotente: si la opción ya existe no la duplica.
-- Las dos opciones quedan en la MISMA categoría/grupo que «Informe de indicadores» (INFORMES_INDICADORES); si esa opción no
-- existe, se usa la categoría llamada «Informes». Después de ejecutarlo, asigne el permiso a los usuarios desde Permisos o con
-- «Habilitar para todos» en el Gestor de menús (/MenuOpciones). El Super Admin las ve siempre.
-- Verifique los nombres de columna de MenuOpciones en su BD (son los mismos que usa Sql/038).
-- =====================================================================================================================
DECLARE @IdGrupo INT, @IdCategoria INT, @OrdenBase INT;

SELECT TOP 1 @IdGrupo = IdGrupo, @IdCategoria = IdCategoria, @OrdenBase = Orden
FROM dbo.MenuOpciones WHERE Codigo = 'INFORMES_INDICADORES';

IF @IdCategoria IS NULL
    SELECT TOP 1 @IdCategoria = Id FROM dbo.CategoriasMenu WHERE Nombre = N'Informes';

IF @OrdenBase IS NULL SET @OrdenBase = 12;

IF NOT EXISTS (SELECT 1 FROM dbo.MenuOpciones WHERE Codigo = 'INFORMES_LINEAS_NEGOCIO')
BEGIN
    INSERT INTO dbo.MenuOpciones
        (Codigo, Nombre, NombreEN, Descripcion, Icono, Orden, Controller, [Action], Activo,
         IdGrupo, IdCategoria, SoloSuperAdmin, SoloAdminEmpresa, EsDestacado)
    VALUES
        ('INFORMES_LINEAS_NEGOCIO', N'Rentabilidad por línea de negocio', N'Business line profitability',
         N'Estado de resultados, mezcla de ingresos y márgenes por línea de negocio', 'fas fa-sitemap', @OrdenBase + 1,
         'InformeLineasNegocio', 'Index', 1, @IdGrupo, @IdCategoria, 0, 0, 0);
END

IF NOT EXISTS (SELECT 1 FROM dbo.MenuOpciones WHERE Codigo = 'INFORMES_GERENCIA_MENSUAL')
BEGIN
    INSERT INTO dbo.MenuOpciones
        (Codigo, Nombre, NombreEN, Descripcion, Icono, Orden, Controller, [Action], Activo,
         IdGrupo, IdCategoria, SoloSuperAdmin, SoloAdminEmpresa, EsDestacado)
    VALUES
        ('INFORMES_GERENCIA_MENSUAL', N'Informe mensual de gerencia', N'Monthly management report',
         N'Resumen del cierre del mes: resultados, balance, caja, indicadores y comentarios de gestión en un solo documento', 'fas fa-file-lines', @OrdenBase + 2,
         'InformeGerenciaMensual', 'Index', 1, @IdGrupo, @IdCategoria, 0, 0, 0);
END
GO
