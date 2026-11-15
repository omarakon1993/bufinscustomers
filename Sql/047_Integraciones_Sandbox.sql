-- =====================================================================================
-- 047 · API de ingesta: entorno de pruebas (sandbox)
-- Compatible con SQL Server 2012. MANUAL: ejecutar despues del 046 (es idempotente).
--
-- Una integracion marcada Sandbox = 1 usa una clave de pruebas (prefijo bft_). Hace TODO el flujo igual que
-- una de produccion (staging, mismas validaciones, impacto, regla de oro, hallazgos) pero su «confirmar» es una
-- SIMULACION: nunca reemplaza datos reales, no deja respaldo ni dispara avisos a los revisores.
-- Sin esta columna la API sigue funcionando (todas las integraciones son de produccion) y la pantalla no ofrece
-- el tipo «Pruebas».
-- =====================================================================================

IF COL_LENGTH('dbo.Integraciones', 'Sandbox') IS NULL
    ALTER TABLE dbo.Integraciones ADD Sandbox BIT NOT NULL CONSTRAINT DF_Integraciones_Sandbox DEFAULT (0);
GO
