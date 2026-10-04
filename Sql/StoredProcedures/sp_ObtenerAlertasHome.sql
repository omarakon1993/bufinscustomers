USE [bufinscustomers]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- Crea un cascarón la primera vez para poder usar ALTER siempre (compatible con SQL Server < 2016 SP1).
IF OBJECT_ID(N'dbo.sp_ObtenerAlertasHome', N'P') IS NULL
    EXEC (N'CREATE PROCEDURE dbo.sp_ObtenerAlertasHome AS BEGIN SET NOCOUNT ON; END');
GO

/*
  Alertas de la página principal (Home). Misma estructura y mismo esquema de hallazgos que
  dbo.sp_ValidarCargueStaging, pero en vez de escribir en CarguesLotesErrores acumula en #Alertas
  y las devuelve (solo lectura: no abre transacción ni modifica nada).

  @IdsEmpresas : lista separada por comas con las empresas que el usuario puede ver (la arma C# según su
                 rol/grupo). NULL = todas las empresas (Super Admin).

  Cada BLOQUE evalúa UNA regla y hace INSERT INTO #Alertas por cada empresa (#Empresas) que la incumple:
      - Una alerta por empresa  ->  JOIN/EXISTS contra #Empresas y se inserta e.IdEmpresa.
      - Severidad: 'Error' (rojo) | 'Advertencia' (amarillo) | 'Info' (azul).
  Para agregar una alerta nueva: copiar la PLANTILLA de abajo, ponerle número/código y listo — sin tocar C#.

  Devuelve 2 result sets:
      1) CodMessage, ErrorMessage, TotalErrores, TotalAdvertencias   (cierre, igual que sp_ValidarCargueStaging)
      2) Un renglón por alerta: IdEmpresa, NombreEmpresa, Severidad, CodigoRegla, Titulo, TituloEn, Mensaje, MensajeEn

  Prueba:  EXEC dbo.sp_ObtenerAlertasHome @IdsEmpresas = NULL;
           EXEC dbo.sp_ObtenerAlertasHome @IdsEmpresas = N'1,5,9';
*/
ALTER PROCEDURE [dbo].[sp_ObtenerAlertasHome]
    @IdsEmpresas NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY

        ------------------------------------------------
        ----------- PREPARACION (no modificar) ---------
        ------------------------------------------------

        -- Empresas dentro del alcance del usuario.
        CREATE TABLE #Empresas (IdEmpresa INT NOT NULL PRIMARY KEY, Nombre NVARCHAR(200) NULL);

        INSERT INTO #Empresas (IdEmpresa, Nombre)
        SELECT e.EmpId, e.EmpNombre
        FROM dbo.Empresas e
        WHERE @IdsEmpresas IS NULL
           OR e.EmpId IN (
                SELECT TRY_CAST(x.i.value('.', 'NVARCHAR(20)') AS INT)
                FROM (SELECT CAST(N'<i>' + REPLACE(@IdsEmpresas, N',', N'</i><i>') + N'</i>' AS XML) AS d) t
                CROSS APPLY t.d.nodes('/i') x(i));

        -- Hallazgos (mismas columnas que CarguesLotesErrores, cambiando IdLote por IdEmpresa).
        CREATE TABLE #Alertas (
            IdEmpresa   INT            NOT NULL,
            Severidad   VARCHAR(20)    NOT NULL,   -- 'Error' | 'Advertencia' | 'Info'
            CodigoRegla VARCHAR(60)    NOT NULL,
            Titulo      NVARCHAR(200)  NULL,
            TituloEn    NVARCHAR(200)  NULL,
            Mensaje     NVARCHAR(MAX)  NULL,
            MensajeEn   NVARCHAR(MAX)  NULL);

        ------------------------------------------------
        ------ PLANTILLA - copiar y numerar ------------
        ------------------------------------------------
        /*
        ------------------------------------------------
        ----------- BLOQUE N - CODIGO_DE_LA_REGLA ------
        ------------------------------------------------

        -- Explicar en una línea qué se valida y por qué.
        INSERT INTO #Alertas (IdEmpresa, Severidad, CodigoRegla, Titulo, TituloEn, Mensaje, MensajeEn)
        SELECT e.IdEmpresa, 'Advertencia', 'CODIGO_DE_LA_REGLA',
               N'Título corto', N'Short title',
               N'Mensaje para ' + e.Nombre + N': qué pasa y qué debe hacer el usuario.',
               N'Message for ' + e.Nombre + N': what is happening and what the user should do.'
        FROM #Empresas e
        WHERE NOT EXISTS (SELECT 1 FROM dbo.TablaX t WHERE t.IdEmpresa = e.IdEmpresa);   -- <- condición de la alerta
        */

        -- ==== AQUI VAN LOS BLOQUES ====


        ------------------------------------------------
        -- Cierre: totales y resultado.
        ------------------------------------------------
        DECLARE @TotalErrores INT, @TotalAdvertencias INT;
        SELECT @TotalErrores      = ISNULL(SUM(CASE WHEN Severidad = 'Error'       THEN 1 ELSE 0 END), 0),
               @TotalAdvertencias = ISNULL(SUM(CASE WHEN Severidad = 'Advertencia' THEN 1 ELSE 0 END), 0)
        FROM #Alertas;

        SELECT 1 AS CodMessage, NULL AS ErrorMessage, @TotalErrores AS TotalErrores, @TotalAdvertencias AS TotalAdvertencias;

        SELECT a.IdEmpresa, e.Nombre AS NombreEmpresa, a.Severidad, a.CodigoRegla, a.Titulo, a.TituloEn, a.Mensaje, a.MensajeEn
        FROM #Alertas a
        JOIN #Empresas e ON e.IdEmpresa = a.IdEmpresa
        ORDER BY CASE a.Severidad WHEN 'Error' THEN 0 WHEN 'Advertencia' THEN 1 ELSE 2 END, e.Nombre, a.CodigoRegla;

        --EXEC dbo.sp_ObtenerAlertasHome @IdsEmpresas = NULL;

    END TRY
    BEGIN CATCH
        SELECT 0 AS CodMessage, ERROR_MESSAGE() AS ErrorMessage;
    END CATCH
END
GO
