/*
    005_MenuOpcion_Escenarios_OPCIONAL.sql

    Alternativa por script a crear la opción de menú "Escenarios" desde la UI de
    /MenuOpciones (Gestor de Menú) — que sigue siendo la forma recomendada porque valida
    todo desde la propia pantalla. Este script hace lo mismo por si prefieres correrlo
    junto con los demás.

    Apunta a GestorEscenariosController/Index (Super Admin, ver CLAUDE.md "Escenarios de
    datos"). Copia IdGrupo/IdCategoria de la opción ADMIN_CONFIG_GRUPOS_EMPRESARIALES
    (Configuración, Solo Super Admin) para que aparezca en el mismo grupo visual del
    sidebar que "Grupos empresariales" — ajusta esos dos INSERT si prefieres otro grupo.

    Seguro de re-ejecutar (no duplica si el Código ya existe).
*/
USE [bufinscustomers]
GO

IF NOT EXISTS (SELECT 1 FROM dbo.MenuOpciones WHERE Codigo = 'ADMIN_CONFIG_ESCENARIOS')
BEGIN
    DECLARE @IdGrupo INT, @IdCategoria INT, @Orden INT;

    SELECT TOP 1 @IdGrupo = IdGrupo, @IdCategoria = IdCategoria
    FROM dbo.MenuOpciones
    WHERE Codigo = 'ADMIN_CONFIG_GRUPOS_EMPRESARIALES';

    SELECT @Orden = ISNULL(MAX(Orden), 0) + 1
    FROM dbo.MenuOpciones
    WHERE (IdGrupo = @IdGrupo) OR (@IdGrupo IS NULL AND IdGrupo IS NULL);

    INSERT INTO dbo.MenuOpciones
        (Codigo, Nombre, NombreEN, Descripcion, Icono, Orden, Controller, [Action], Activo, IdGrupo, IdCategoria, SoloSuperAdmin, SoloAdminEmpresa, EsDestacado)
    VALUES
        ('ADMIN_CONFIG_ESCENARIOS',
         N'Escenarios', N'Scenarios',
         N'Gestor de escenarios de datos (copias paralelas de la información por empresa)',
         'fas fa-layer-group',
         @Orden,
         'GestorEscenarios', 'Index',
         1,               -- Activo
         @IdGrupo, @IdCategoria,
         1, 0, 0);         -- SoloSuperAdmin=1, SoloAdminEmpresa=0, EsDestacado=0

    IF @IdGrupo IS NULL
        PRINT 'Aviso: no se encontró ADMIN_CONFIG_GRUPOS_EMPRESARIALES como referencia de grupo/categoría; ' +
              '"Escenarios" se creó sin grupo/categoría asignados. Puedes ajustarlo desde /MenuOpciones (Editar).';
END
GO
