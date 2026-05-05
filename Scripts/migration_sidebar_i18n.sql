-- ============================================================
-- Migración: Multi-idioma sidebar
-- Ejecutar en orden. Los ALTER TABLE solo si las columnas aún no existen.
-- ============================================================

-- 1. Agregar columnas NombreEN (ignorar si ya existen)
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='CategoriasMenu' AND COLUMN_NAME='NombreEN')
    ALTER TABLE CategoriasMenu ADD NombreEN NVARCHAR(150) NULL;

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='GruposMenu' AND COLUMN_NAME='NombreEN')
    ALTER TABLE GruposMenu ADD NombreEN NVARCHAR(150) NULL;

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='MenuOpciones' AND COLUMN_NAME='NombreEN')
    ALTER TABLE MenuOpciones ADD NombreEN NVARCHAR(150) NULL;
GO

-- ============================================================
-- 2. CategoriasMenu — por Nombre exacto
-- ============================================================
UPDATE CategoriasMenu SET NombreEN = 'Data'           WHERE Nombre = 'Datos';
UPDATE CategoriasMenu SET NombreEN = 'Reports'        WHERE Nombre = 'Informes';
UPDATE CategoriasMenu SET NombreEN = 'Administration' WHERE Nombre = 'Administración';
UPDATE CategoriasMenu SET NombreEN = 'Configuration'  WHERE Nombre = 'Configuración';
GO

-- ============================================================
-- 3. GruposMenu — por Id (nombres exactos visto en BD)
-- ============================================================
UPDATE GruposMenu SET NombreEN = 'Configuration'   WHERE Id = 1;  -- Configuración
UPDATE GruposMenu SET NombreEN = 'Managers'        WHERE Id = 2;  -- Gestores
UPDATE GruposMenu SET NombreEN = 'Reports'         WHERE Id = 3;  -- Informes
UPDATE GruposMenu SET NombreEN = 'Execution Model' WHERE Id = 4;  -- Modelo        (ya tenía valor)
UPDATE GruposMenu SET NombreEN = 'Excel Upload'    WHERE Id = 5;  -- Plantilla
UPDATE GruposMenu SET NombreEN = 'Power BI Reports' WHERE Id = 6; -- Reportes      (ya tenía valor)
GO

-- ============================================================
-- 4. MenuOpciones — por Codigo exacto
-- ============================================================
UPDATE MenuOpciones SET NombreEN = 'Excel Upload'             WHERE Codigo = 'DATOS_PLANTILLA_CARGUE';
UPDATE MenuOpciones SET NombreEN = 'Run Model'                WHERE Codigo = 'DATOS_MODELO_EJECUCION';
UPDATE MenuOpciones SET NombreEN = 'Power BI Dashboards'      WHERE Codigo = 'INFORMES_REPORTES_PBI';
UPDATE MenuOpciones SET NombreEN = 'Upload Audit'             WHERE Codigo = 'INFORMES_AUDITORIA_CARGUES';
UPDATE MenuOpciones SET NombreEN = 'Data Tables'              WHERE Codigo = 'INFORMES_TABLAS_DATOS';
UPDATE MenuOpciones SET NombreEN = 'Relationships'            WHERE Codigo = 'INFORMES_RELACIONAMIENTOS';
UPDATE MenuOpciones SET NombreEN = 'AI Analysis'              WHERE Codigo = 'INFORMES_ANALISIS_IA';
UPDATE MenuOpciones SET NombreEN = 'PBI Variables'            WHERE Codigo = 'INFORMES_VARIABLES_PBI';
UPDATE MenuOpciones SET NombreEN = 'User Manager'             WHERE Codigo = 'ADMIN_USUARIOS_GESTOR';
UPDATE MenuOpciones SET NombreEN = 'Company Manager'          WHERE Codigo = 'ADMIN_EMPRESAS_GESTOR';
UPDATE MenuOpciones SET NombreEN = 'Report Manager'           WHERE Codigo = 'ADMIN_REPORTES_GESTOR';
UPDATE MenuOpciones SET NombreEN = 'Company Config'           WHERE Codigo = 'ADMIN_CONFIG_EMPRESAS';
UPDATE MenuOpciones SET NombreEN = 'Relationships Config'     WHERE Codigo = 'ADMIN_CONFIG_RELACIONAMIENTOS';
UPDATE MenuOpciones SET NombreEN = 'Menu Manager'             WHERE Codigo = 'ADMIN_CONFIG_MENU';
UPDATE MenuOpciones SET NombreEN = 'Model Manager'            WHERE Codigo = 'ADMIN_CONFIG_MODELOS';
UPDATE MenuOpciones SET NombreEN = 'Prompt Manager'           WHERE Codigo = 'ADMIN_CONFIG_PROMPTS';
UPDATE MenuOpciones SET NombreEN = 'PBI Variables Config'     WHERE Codigo = 'ADMIN_CONFIG_VARIABLES_PBI';
UPDATE MenuOpciones SET NombreEN = 'Widget Manager'           WHERE Codigo = 'ADMIN_CONFIG_WIDGETS';
UPDATE MenuOpciones SET NombreEN = 'Category Manager'         WHERE Codigo = 'ADMIN_CONFIG_CATEGORIAS';
UPDATE MenuOpciones SET NombreEN = 'Group Manager'            WHERE Codigo = 'ADMIN_CONFIG_GRUPOS';
GO

-- ============================================================
-- 5. Stored Procedure actualizado
-- ============================================================
ALTER PROCEDURE [dbo].[sp_ObtenerMenuUsuario]
    @IdUsuario  INT,
    @NivelAdmin TINYINT
AS
BEGIN
    SET NOCOUNT ON;

    IF @NivelAdmin = 2
    BEGIN
        -- Super Admin: todas las opciones activas
        SELECT m.Id, m.Codigo, m.Nombre, m.NombreEN, m.Descripcion, m.Icono, m.Orden,
               m.Controller, m.[Action], m.IdGrupo, m.SoloSuperAdmin, m.SoloAdminEmpresa,
               g.Nombre   AS NombreGrupo,
               g.NombreEN AS NombreGrupoEN,
               g.Icono    AS IconoGrupo,
               c.Nombre   AS Categoria,
               c.NombreEN AS NombreCategoriaEN,
               c.Icono    AS IconoCategoria,
               c.Orden    AS OrdenCategoria
        FROM   [dbo].[MenuOpciones]   m
        JOIN   [dbo].[GruposMenu]     g ON m.IdGrupo    = g.Id
        JOIN   [dbo].[CategoriasMenu] c ON g.IdCategoria = c.Id
        WHERE  m.Activo = 1
        ORDER BY c.Orden, m.Orden;
    END
    ELSE
    BEGIN
        -- Usuario normal / admin empresa: solo sus opciones asignadas
        SELECT m.Id, m.Codigo, m.Nombre, m.NombreEN, m.Descripcion, m.Icono, m.Orden,
               m.Controller, m.[Action], m.IdGrupo, m.SoloSuperAdmin, m.SoloAdminEmpresa,
               g.Nombre   AS NombreGrupo,
               g.NombreEN AS NombreGrupoEN,
               g.Icono    AS IconoGrupo,
               c.Nombre   AS Categoria,
               c.NombreEN AS NombreCategoriaEN,
               c.Icono    AS IconoCategoria,
               c.Orden    AS OrdenCategoria
        FROM   [dbo].[MenuOpciones]       m
        JOIN   [dbo].[GruposMenu]         g ON m.IdGrupo    = g.Id
        JOIN   [dbo].[CategoriasMenu]     c ON g.IdCategoria = c.Id
        JOIN   [dbo].[UsuarioMenuPermisos] p ON m.Id = p.IdMenuOpcion
                                             AND p.IdUsuario = @IdUsuario
        WHERE  m.Activo = 1
        ORDER BY c.Orden, m.Orden;
    END
END;
GO
