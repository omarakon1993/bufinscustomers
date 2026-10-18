/* ============================================================================
   035 - Umbral de bloqueo de cargues (regla de oro: nunca dejar un anio vacio)
   ----------------------------------------------------------------------------
   Script MANUAL. SQL Server 2012+. Se puede ejecutar varias veces.
   Agrega el parametro Cargue_UmbralBloqueoPct (Configuraciones generales BUFINS, tipo
   «Cargues de datos»): si un cargue, «Deshacer este cargue» o «Recuperar» dejaria alguna tabla
   con menos de este porcentaje (1-50) de las filas que tiene hoy (incluye quedar vacia), la
   operacion NO se permite para NADIE. Nunca es mayor que Cargue_UmbralReemplazoPct (50).
   Sin este script se usa el valor por defecto (10) y todo funciona igual.
   ============================================================================ */

IF NOT EXISTS (SELECT 1 FROM dbo.ConfiguracionSistema WHERE Clave = 'Cargue_UmbralBloqueoPct')
    INSERT INTO dbo.ConfiguracionSistema (Clave, Valor, Descripcion)
    VALUES ('Cargue_UmbralBloqueoPct', '10',
            N'Bloqueo total: si un cargue o un deshacer dejaria alguna tabla con menos de este porcentaje (1-50) de las filas que tiene hoy (incluye quedar vacia), no se permite para nadie. No puede ser mayor que Cargue_UmbralReemplazoPct.');
GO

IF COL_LENGTH('dbo.ConfiguracionSistema', 'IdTipo') IS NOT NULL
   AND OBJECT_ID('dbo.ConfiguracionSistemaTipos', 'U') IS NOT NULL
    EXEC(N'UPDATE c SET c.IdTipo = t.Id
             FROM dbo.ConfiguracionSistema c
             JOIN dbo.ConfiguracionSistemaTipos t ON t.Nombre = N''Cargues de datos''
            WHERE c.IdTipo IS NULL AND c.Clave = ''Cargue_UmbralBloqueoPct''');
GO

SELECT Clave, Valor FROM dbo.ConfiguracionSistema WHERE Clave LIKE 'Cargue[_]%';
GO
