-- =====================================================================================
-- Widget de Advertencia (Tipo=3, Subtipo='plantilla') para el Gestor de Widgets.
-- Se evalua automaticamente justo despues de cada cargue de Excel exitoso (DatosController.
-- CargarExcel) contra la empresa recien cargada: detecta si el campo "Pais" quedo escrito
-- de forma distinta entre las tablas Ini_ para esa misma empresa (ej. "Col" / "Colombia" /
-- vacio), sin marcar como error a una empresa que legitimamente opera en varios paises
-- distintos (ej. Honduras, Peru, Ecuador... todos con una sola grafia cada uno).
--
-- Reglas que dispara la alerta:
--   1) Hay filas sin pais (NULL o vacio) mientras otras filas de la misma empresa si lo
--      tienen.
--   2) Dos valores de pais para la misma empresa donde uno es prefijo/abreviatura del otro,
--      comparando sin tildes y sin distinguir mayusculas/minusculas (COLLATE ..._CI_AI) -
--      esto atrapa "Col" -> "Colombia" y "Mexico" vs "Mexico" (solo tilde) sin confundir
--      paises realmente distintos como Peru y Colombia.
--
-- Tablas Ini_ consideradas (las que si tienen columna Pais, segun confirmo el usuario):
-- Ini_BalancePrueba, Ini_CteYnoCte, Ini_PptoPYG, Ini_PptoPYGConAjuste,
-- Ini_PresupuestoBalance, Ini_PYG, Ini_PYGDetalladoConAjuste.
-- Quedan fuera Ini_EjecPCH e Ini_PCH (no tienen columna Pais).
--
-- Seguro de re-ejecutar: si ya existe un widget con este Nombre, no inserta duplicado.
-- =====================================================================================

IF NOT EXISTS (SELECT 1 FROM dbo.DashboardTarjetas WHERE Nombre = N'Pais inconsistente por empresa')
BEGIN
    INSERT INTO dbo.DashboardTarjetas
        (Nombre, NombreEn, Tipo, Icono, ColorIcono, Orden, Activo,
         ConsultaSQL, UnidadValor, TipoGrafico, FondoOscuro,
         TipoFuente, NombreSP, Severidad, Subtipo)
    VALUES
        (
            N'Pais inconsistente por empresa',
            N'Inconsistent country per company',
            3,
            N'fas fa-globe-americas',
            N'#f59e0b',
            (SELECT ISNULL(MAX(Orden), 0) + 1 FROM dbo.DashboardTarjetas),
            1,
            N'WITH PaisesPorTabla AS (
    SELECT IdEmpresa_Log, LTRIM(RTRIM(Pais)) AS PaisOriginal FROM Ini_BalancePrueba WHERE IdEmpresa_Log = @IdEmpresa
    UNION ALL
    SELECT IdEmpresa_Log, LTRIM(RTRIM(Pais)) FROM Ini_CteYnoCte WHERE IdEmpresa_Log = @IdEmpresa
    UNION ALL
    SELECT IdEmpresa_Log, LTRIM(RTRIM(Pais)) FROM Ini_PptoPYG WHERE IdEmpresa_Log = @IdEmpresa
    UNION ALL
    SELECT IdEmpresa_Log, LTRIM(RTRIM(Pais)) FROM Ini_PptoPYGConAjuste WHERE IdEmpresa_Log = @IdEmpresa
    UNION ALL
    SELECT IdEmpresa_Log, LTRIM(RTRIM(Pais)) FROM Ini_PresupuestoBalance WHERE IdEmpresa_Log = @IdEmpresa
    UNION ALL
    SELECT IdEmpresa_Log, LTRIM(RTRIM(Pais)) FROM Ini_PYG WHERE IdEmpresa_Log = @IdEmpresa
    UNION ALL
    SELECT IdEmpresa_Log, LTRIM(RTRIM(Pais)) FROM Ini_PYGDetalladoConAjuste WHERE IdEmpresa_Log = @IdEmpresa
),
Distintos AS (
    SELECT DISTINCT IdEmpresa_Log, PaisOriginal FROM PaisesPorTabla
),
ConVacios AS (
    SELECT IdEmpresa_Log,
           MAX(CASE WHEN PaisOriginal IS NULL OR PaisOriginal = '''' THEN 1 ELSE 0 END) AS TieneVacios,
           MAX(CASE WHEN PaisOriginal IS NOT NULL AND PaisOriginal <> '''' THEN 1 ELSE 0 END) AS TieneConValor
    FROM Distintos
    GROUP BY IdEmpresa_Log
),
Pares AS (
    SELECT DISTINCT a.IdEmpresa_Log, a.PaisOriginal AS ValorA, b.PaisOriginal AS ValorB
    FROM Distintos a
    JOIN Distintos b
      ON a.IdEmpresa_Log = b.IdEmpresa_Log
     AND a.PaisOriginal <> b.PaisOriginal
     AND a.PaisOriginal COLLATE Latin1_General_BIN2 < b.PaisOriginal COLLATE Latin1_General_BIN2
    WHERE a.PaisOriginal IS NOT NULL AND a.PaisOriginal <> ''''
      AND b.PaisOriginal IS NOT NULL AND b.PaisOriginal <> ''''
      AND LEN(a.PaisOriginal) >= 2 AND LEN(b.PaisOriginal) >= 2
      AND (
            LEFT(b.PaisOriginal COLLATE Latin1_General_CI_AI, LEN(a.PaisOriginal)) = a.PaisOriginal COLLATE Latin1_General_CI_AI
            OR
            LEFT(a.PaisOriginal COLLATE Latin1_General_CI_AI, LEN(b.PaisOriginal)) = b.PaisOriginal COLLATE Latin1_General_CI_AI
          )
),
ParesAgg AS (
    SELECT IdEmpresa_Log,
           STRING_AGG(CONVERT(NVARCHAR(MAX), ValorA + '' / '' + ValorB), '', '') AS DetallePares,
           COUNT(*) AS NumPares
    FROM Pares
    GROUP BY IdEmpresa_Log
)
SELECT e.EmpId AS IdEmpresa,
       e.EmpNombre AS NombreEmpresa,
       ''Pais escrito de forma distinta para la misma empresa'' AS Titulo,
       ''Country spelled differently for the same company'' AS TituloEn,
       CASE
           WHEN p.NumPares > 0 AND v.TieneVacios = 1
               THEN ''Posibles variantes del mismo pais ('' + p.DetallePares + '') y ademas filas sin pais registrado''
           WHEN p.NumPares > 0
               THEN ''Posibles variantes del mismo pais escritas distinto: '' + p.DetallePares
           ELSE ''Hay filas cargadas sin pais mientras otras filas de la misma empresa si lo tienen''
       END AS Mensaje,
       CASE
           WHEN p.NumPares > 0 AND v.TieneVacios = 1
               THEN ''Possible variants of the same country ('' + p.DetallePares + '') plus rows with no country recorded''
           WHEN p.NumPares > 0
               THEN ''Possible variants of the same country written differently: '' + p.DetallePares
           ELSE ''Some rows were loaded without a country while other rows for the same company do have one''
       END AS MensajeEn
FROM ConVacios v
JOIN Empresas e ON e.EmpId = v.IdEmpresa_Log
LEFT JOIN ParesAgg p ON p.IdEmpresa_Log = v.IdEmpresa_Log
WHERE (p.NumPares > 0) OR (v.TieneVacios = 1 AND v.TieneConValor = 1)',
            NULL,
            NULL,
            0,
            1,
            NULL,
            N'warning',
            N'plantilla'
        )
END
GO

-- Verificacion rapida (opcional): ejecutar para confirmar que quedo insertado
-- SELECT Id, Nombre, Tipo, Subtipo, Severidad, ConsultaSQL FROM dbo.DashboardTarjetas WHERE Nombre = N'Pais inconsistente por empresa';
