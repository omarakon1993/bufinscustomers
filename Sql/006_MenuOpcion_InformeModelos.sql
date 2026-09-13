/*
    006_MenuOpcion_InformeModelos.sql

    Crea la opción de menú "Informe de Modelos" (InformeModelosController/Index) — un
    informe de solo lectura sobre las 8 tablas de resultados de Ejecución de Modelos
    (ModeloBalance, ModeloBalanceDiff, ModeloBalancePpto, ModeloFlujoCaja,
    ModeloFlujoEfectivo, ModeloLineasNegocio, ModeloPYG, ModeloTesoreriaPpto), con
    exportación a Excel. Es un feature INDEPENDIENTE de InformeTablasDatosController:
    InformeModelosController tiene sus propias acciones ConsultarDatos/ExportarExcel
    (restringidas server-side a estas 8 tablas), y solo reutiliza como motor interno el
    servicio InformeTablasDatosService. Copia IdGrupo/IdCategoria de la opción
    INFORMES_TABLAS_DATOS para que aparezca en el mismo grupo visual del sidebar
    ("Informes").

    A diferencia del script 005 (que crea una opción Solo Super Admin), esta opción se
    crea SIN restricción de rol (SoloSuperAdmin=0, SoloAdminEmpresa=0) porque debe quedar
    habilitada para todos los usuarios:
      - Para usuarios NUEVOS esto ya es automático: AsignarPermisosPorDefecto (ver
        UsuarioController.cs) incluye toda opción activa sin esas dos banderas.
      - Para usuarios YA EXISTENTES se necesita el INSERT explícito en
        UsuarioMenuPermisos de más abajo (no toca los permisos que ya tenían).

    También desactiva la opción "Tablas de Datos" (INFORMES_TABLAS_DATOS) — reemplazada
    por este informe dedicado, por decisión del usuario. Es un soft-delete de la opción de
    MENÚ (Activo = 0, mismo patrón que el resto de la app); el código de esa pantalla
    (InformeTablasDatosController.InformeTablasDatos(), su ConsultarDatos/ExportarExcel
    propios, y Views/Informes/InformeTablasDatos.cshtml) se eliminó del proyecto — ya no
    existen. InformeTablasDatosController sigue existiendo solo con lo que usa Análisis IA
    (ObtenerAnios, ObtenerVariables, ConsultarConIA, ExportarExcelConIA, ValorarRespuestaIA).

    Seguro de re-ejecutar (no duplica la opción de menú ni los permisos ya otorgados).
*/
USE [bufinscustomers]
GO

IF NOT EXISTS (SELECT 1 FROM dbo.MenuOpciones WHERE Codigo = 'INFORMES_MODELOS')
BEGIN
    DECLARE @IdGrupo INT, @IdCategoria INT, @Orden INT, @NuevoId INT;

    SELECT TOP 1 @IdGrupo = IdGrupo, @IdCategoria = IdCategoria
    FROM dbo.MenuOpciones
    WHERE Codigo = 'INFORMES_TABLAS_DATOS';

    SELECT @Orden = ISNULL(MAX(Orden), 0) + 1
    FROM dbo.MenuOpciones
    WHERE (IdGrupo = @IdGrupo) OR (@IdGrupo IS NULL AND IdGrupo IS NULL);

    INSERT INTO dbo.MenuOpciones
        (Codigo, Nombre, NombreEN, Descripcion, Icono, Orden, Controller, [Action], Activo, IdGrupo, IdCategoria, SoloSuperAdmin, SoloAdminEmpresa, EsDestacado)
    VALUES
        ('INFORMES_MODELOS',
         N'Informe de Modelos', N'Model Report',
         N'Consulta y exporta a Excel los datos de las tablas de resultados de Ejecución de Modelos',
         'fas fa-table',
         @Orden,
         'InformeModelos', 'Index',
         1,               -- Activo
         @IdGrupo, @IdCategoria,
         0, 0, 0);         -- SoloSuperAdmin=0, SoloAdminEmpresa=0, EsDestacado=0 (para todos los roles)

    IF @IdGrupo IS NULL
        PRINT 'Aviso: no se encontró INFORMES_TABLAS_DATOS como referencia de grupo/categoría; ' +
              '"Informe de Modelos" se creó sin grupo/categoría asignados. Puedes ajustarlo desde /MenuOpciones (Editar).';

    SELECT @NuevoId = Id FROM dbo.MenuOpciones WHERE Codigo = 'INFORMES_MODELOS';

    -- Otorgar el acceso a todos los usuarios YA EXISTENTES que no sean Super Admin (Admin=2
    -- siempre tiene acceso total sin necesidad de fila). No toca ningún otro permiso del usuario.
    INSERT INTO dbo.UsuarioMenuPermisos (IdUsuario, IdMenuOpcion, UsuarioAsigno)
    SELECT u.Id, @NuevoId, u.Id
    FROM dbo.Usuarios u
    WHERE u.Admin IN (0, 1)
      AND NOT EXISTS (
          SELECT 1 FROM dbo.UsuarioMenuPermisos ump
          WHERE ump.IdUsuario = u.Id AND ump.IdMenuOpcion = @NuevoId
      );
END
GO

-- "Tablas de Datos" queda reemplazada por "Informe de Modelos" — se desactiva (soft-delete,
-- no se borra: el código de InformeTablasDatosController sigue en uso por Análisis IA).
UPDATE dbo.MenuOpciones SET Activo = 0 WHERE Codigo = 'INFORMES_TABLAS_DATOS';
GO
