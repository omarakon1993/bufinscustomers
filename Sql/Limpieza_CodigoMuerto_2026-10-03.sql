/*
  LIMPIEZA DE OBJETOS MUERTOS EN LA BD  (bufinscustomers) - inventario del 2026-10-03

  *** ORDEN IMPORTANTE ***  Despliegue PRIMERO la nueva version del sitio (la que ya no usa widgets, resumen IA ni tablas *_VT)
  y DESPUES ejecute este script. Si se ejecuta antes, la version vieja que siga publicada fallaria (Home, Configuracion de empresa).

  Ejecutar por PASOS en SSMS (seleccione y ejecute cada bloque; no todo de una).
  Cada paso verifica antes de borrar y el PASO 0 deja copia de respaldo (zz_bak_*) que usted elimina cuando confirme que todo esta bien.

  Lo que SE ELIMINA (verificado: 0 referencias en el codigo C#, en otros SPs ni por llaves foraneas):
    PASO 1  Tabla DashboardTarjetas           (widgets del Home, ya eliminados del codigo; 4 filas)
    PASO 1  Tabla FinalBalancePruebaWidget    (resto de un widget; 1.836 filas; nadie la referencia)
    PASO 1  Tabla onfiguracionCuentasCorreo   (copia con nombre mal escrito, 0 filas; la real es ConfiguracionCuentasCorreo)
    PASO 1  SP sp_GenerarTablaBalance         (roto: lee/escribe TablaBalance y Z_BalancePrueba, que ya no existen)
    PASO 2  Opciones de menu ADMIN_CONFIG_WIDGETS, ADMIN_CONFIG_RESUMEN_IA e INFORMES_TABLAS_DATOS
            (apuntan a controladores/acciones que ya no existen; la de widgets sigue ACTIVA y daria un enlace roto en el sidebar)
    PASO 3  Columna Usuarios.LimiteConsultasIA (el codigo ya no la usa; reemplazada por AccesoConsultasIA / presupuesto por empresa)
            - requiere primero reescribir sp_ObtenerUsuarios sin esa columna
    PASO 4  Tabla EmpresaTablasResumenIA      (0 filas; el resumen IA del Home y su pestana de configuracion se eliminaron)
    PASO 4  10 tablas *_VT                    (TableBalance_Datos_VT ... ~70.000 filas; el informe "Tablas de datos" se elimino
                                               y la IA solo lee las tablas Modelo*)

  NO se elimina (parece muerto pero NO lo es):
    - sp_ModeloBalance/PYG/BalanceDiff/BalancePpto/FlujoCaja/FlujoEfectivo/LineasNegocio/TesoreriaPpto: se llaman dinamicamente desde ModelosEjecucion.NombreSP.
    - Tablas Rel_*, Config*, Ini_*, Staging_Ini_*, Modelo*, Z_TablaPUC: las usan SPs o el codigo (a veces por nombre armado en tiempo de ejecucion).
*/
USE [bufinscustomers];
GO

/* ===================================================================================
   PASO 0 - RESPALDO (conserva los datos de lo que se va a borrar)
   =================================================================================== */
IF OBJECT_ID('dbo.zz_bak_DashboardTarjetas') IS NULL        SELECT * INTO dbo.zz_bak_DashboardTarjetas        FROM dbo.DashboardTarjetas;
IF OBJECT_ID('dbo.zz_bak_FinalBalancePruebaWidget') IS NULL SELECT * INTO dbo.zz_bak_FinalBalancePruebaWidget FROM dbo.FinalBalancePruebaWidget;
IF OBJECT_ID('dbo.zz_bak_EmpresaTablasResumenIA') IS NULL   SELECT * INTO dbo.zz_bak_EmpresaTablasResumenIA   FROM dbo.EmpresaTablasResumenIA;
IF OBJECT_ID('dbo.zz_bak_MenuOpciones_limpieza') IS NULL    SELECT * INTO dbo.zz_bak_MenuOpciones_limpieza    FROM dbo.MenuOpciones
                                                            WHERE Codigo IN ('ADMIN_CONFIG_WIDGETS','ADMIN_CONFIG_RESUMEN_IA','INFORMES_TABLAS_DATOS');

-- Las 10 tablas *_VT, completas, con el prefijo zz_bak_
DECLARE @vt SYSNAME, @sqlbak NVARCHAR(400);
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT name FROM sys.tables WHERE name LIKE 'Table%[_]Datos[_]VT';
OPEN c; FETCH NEXT FROM c INTO @vt;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF OBJECT_ID('dbo.zz_bak_' + @vt) IS NULL
    BEGIN
        SET @sqlbak = N'SELECT * INTO dbo.' + QUOTENAME('zz_bak_' + @vt) + N' FROM dbo.' + QUOTENAME(@vt);
        EXEC (@sqlbak);
    END
    FETCH NEXT FROM c INTO @vt;
END
CLOSE c; DEALLOCATE c;

-- Definicion de los SPs que se tocan (por si quiere restaurarlos):
SELECT OBJECT_NAME(object_id) AS sp, definition FROM sys.sql_modules WHERE object_id IN (OBJECT_ID('dbo.sp_GenerarTablaBalance'), OBJECT_ID('dbo.sp_ObtenerUsuarios'));
-- >>> Copie el resultado anterior a un archivo antes de continuar.
GO

/* ===================================================================================
   PASO 1 - TABLAS Y SP MUERTOS
   =================================================================================== */
BEGIN TRY
    BEGIN TRANSACTION;

    -- Seguridad: abortar si alguna ya tiene dependencias (FK o modulos) que no vimos.
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE referenced_object_id IN (OBJECT_ID('dbo.DashboardTarjetas'), OBJECT_ID('dbo.FinalBalancePruebaWidget'), OBJECT_ID('dbo.onfiguracionCuentasCorreo')))
        THROW 50001, 'Hay llaves foraneas apuntando a las tablas a eliminar. Revise antes de continuar.', 1;
    IF EXISTS (SELECT 1 FROM sys.sql_expression_dependencies WHERE referenced_id IN (OBJECT_ID('dbo.DashboardTarjetas'), OBJECT_ID('dbo.FinalBalancePruebaWidget'), OBJECT_ID('dbo.onfiguracionCuentasCorreo'), OBJECT_ID('dbo.sp_GenerarTablaBalance')))
        THROW 50002, 'Hay objetos que dependen de lo que se va a eliminar. Revise antes de continuar.', 1;
    IF (SELECT COUNT(*) FROM dbo.onfiguracionCuentasCorreo) > 0
        THROW 50003, 'onfiguracionCuentasCorreo ya no esta vacia; no se elimina.', 1;

    DROP TABLE dbo.DashboardTarjetas;
    DROP TABLE dbo.FinalBalancePruebaWidget;
    DROP TABLE dbo.onfiguracionCuentasCorreo;
    DROP PROCEDURE dbo.sp_GenerarTablaBalance;

    COMMIT TRANSACTION;
    PRINT 'PASO 1 OK';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    SELECT ERROR_NUMBER() AS Numero, ERROR_MESSAGE() AS Mensaje;
END CATCH
GO

/* ===================================================================================
   PASO 2 - OPCIONES DE MENU HUERFANAS
   (despues de ejecutarlo, el sidebar se refresca solo en <= 10 min, o reinicie el sitio / cierre sesion)
   =================================================================================== */
BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @ids TABLE (Id INT);
    INSERT INTO @ids SELECT Id FROM dbo.MenuOpciones WHERE Codigo IN ('ADMIN_CONFIG_WIDGETS','ADMIN_CONFIG_RESUMEN_IA','INFORMES_TABLAS_DATOS');

    DELETE FROM dbo.UsuarioMenuPermisos WHERE IdMenuOpcion IN (SELECT Id FROM @ids);  -- hoy 0 filas, por si acaso
    DELETE FROM dbo.MenuOpciones        WHERE Id IN (SELECT Id FROM @ids);

    COMMIT TRANSACTION;
    PRINT 'PASO 2 OK';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    SELECT ERROR_NUMBER() AS Numero, ERROR_MESSAGE() AS Mensaje;
END CATCH
GO

/* ===================================================================================
   PASO 3 - COLUMNA Usuarios.LimiteConsultasIA
   Primero se reescribe sp_ObtenerUsuarios (era el unico objeto que la leia; el C# no la usa).
   =================================================================================== */
ALTER PROCEDURE [dbo].[sp_ObtenerUsuarios]
AS
BEGIN
    SELECT
        u.Id,
        u.Usuario,
        u.Clave,
        u.Nombre,
        u.Apellidos,
        u.Correo,
        u.Telefono,
        u.Admin,
        u.IdEmpresa,
        ui.ImagenBase64,
        ui.TipoImagen,
        ui.NombreImagen
    FROM Usuarios u
    LEFT JOIN UsuarioImagenes ui ON u.Id = ui.UsuarioId
    ORDER BY u.Id;
END
GO

-- Respaldo de los valores y borrado de la columna (quita antes su DEFAULT si lo tiene).
IF OBJECT_ID('dbo.zz_bak_Usuarios_LimiteConsultasIA') IS NULL AND COL_LENGTH('dbo.Usuarios', 'LimiteConsultasIA') IS NOT NULL
    EXEC (N'SELECT Id, LimiteConsultasIA INTO dbo.zz_bak_Usuarios_LimiteConsultasIA FROM dbo.Usuarios');

DECLARE @df SYSNAME = (SELECT dc.name FROM sys.default_constraints dc
                       JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
                       WHERE dc.parent_object_id = OBJECT_ID('dbo.Usuarios') AND c.name = 'LimiteConsultasIA');
IF @df IS NOT NULL EXEC (N'ALTER TABLE dbo.Usuarios DROP CONSTRAINT ' + @df);
IF COL_LENGTH('dbo.Usuarios', 'LimiteConsultasIA') IS NOT NULL
    ALTER TABLE dbo.Usuarios DROP COLUMN LimiteConsultasIA;
PRINT 'PASO 3 OK';
GO

/* ===================================================================================
   PASO 4 - EmpresaTablasResumenIA y las 10 tablas *_VT
   (ejecutar solo con la nueva version del sitio ya desplegada)
   =================================================================================== */
BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE referenced_object_id = OBJECT_ID('dbo.EmpresaTablasResumenIA')
                  OR referenced_object_id IN (SELECT object_id FROM sys.tables WHERE name LIKE 'Table%[_]Datos[_]VT'))
        THROW 50004, 'Hay llaves foraneas apuntando a las tablas a eliminar. Revise antes de continuar.', 1;
    IF EXISTS (SELECT 1 FROM sys.sql_expression_dependencies
               WHERE referenced_id = OBJECT_ID('dbo.EmpresaTablasResumenIA')
                  OR referenced_id IN (SELECT object_id FROM sys.tables WHERE name LIKE 'Table%[_]Datos[_]VT'))
        THROW 50005, 'Hay SPs/objetos que dependen de estas tablas. Revise antes de continuar.', 1;

    DROP TABLE dbo.EmpresaTablasResumenIA;

    DECLARE @t SYSNAME, @sql NVARCHAR(300);
    DECLARE c2 CURSOR LOCAL FAST_FORWARD FOR SELECT name FROM sys.tables WHERE name LIKE 'Table%[_]Datos[_]VT';
    OPEN c2; FETCH NEXT FROM c2 INTO @t;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @sql = N'DROP TABLE dbo.' + QUOTENAME(@t);
        EXEC (@sql);
        FETCH NEXT FROM c2 INTO @t;
    END
    CLOSE c2; DEALLOCATE c2;

    COMMIT TRANSACTION;
    PRINT 'PASO 4 OK';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    SELECT ERROR_NUMBER() AS Numero, ERROR_MESSAGE() AS Mensaje;
END CATCH
GO

/* ===================================================================================
   VERIFICACION FINAL (las 3 consultas deben devolver 0 filas)
   =================================================================================== */
SELECT name FROM sys.objects
WHERE name IN ('DashboardTarjetas','FinalBalancePruebaWidget','onfiguracionCuentasCorreo','sp_GenerarTablaBalance','EmpresaTablasResumenIA')
   OR name LIKE 'Table%[_]Datos[_]VT';
SELECT Codigo FROM dbo.MenuOpciones WHERE Codigo IN ('ADMIN_CONFIG_WIDGETS','ADMIN_CONFIG_RESUMEN_IA','INFORMES_TABLAS_DATOS');
SELECT name FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Usuarios') AND name = 'LimiteConsultasIA';
GO

/* ===================================================================================
   CUANDO CONFIRME QUE TODO FUNCIONA (dias despues) - eliminar TODOS los respaldos:
     DECLARE @s NVARCHAR(MAX) = N'';
     SELECT @s += N'DROP TABLE dbo.' + QUOTENAME(name) + N';' FROM sys.tables WHERE name LIKE 'zz[_]bak[_]%';
     PRINT @s;      -- revise la lista y luego:  EXEC (@s);
   =================================================================================== */
