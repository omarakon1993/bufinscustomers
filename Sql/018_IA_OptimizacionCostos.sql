/* ============================================================================================
   018 — Optimización de costos de IA (2026-10). Ejecutar manualmente; es re-ejecutable.
   Antes de este script todo sigue funcionando: el código detecta si las columnas/claves existen.
   ============================================================================================ */

-- 1) Tokens de entrada servidos desde la caché de prompts de OpenAI (se ven en "Uso y costos").
--    Tras ejecutarlo, reiniciar el app pool (o esperar al próximo reciclaje): el código recuerda
--    en memoria que la columna no existía.
IF COL_LENGTH('dbo.IAUsoLog', 'TokensCacheados') IS NULL
    ALTER TABLE dbo.IAUsoLog ADD TokensCacheados INT NULL;
GO

-- 2) Claves nuevas de ConfiguracionSistema (aparecen en Configuración IA → General).
IF NOT EXISTS (SELECT 1 FROM dbo.ConfiguracionSistema WHERE Clave = 'IA_CacheHorasModelo')
    INSERT INTO dbo.ConfiguracionSistema (Clave, Valor, Descripcion)
    VALUES ('IA_CacheHorasModelo', '720',
            'Horas que se conserva el resumen/insight de IA mientras los datos del modelo no cambien (máx. 720). Si el modelo se ejecuta de nuevo y los datos cambian, la respuesta se regenera sola.');

IF NOT EXISTS (SELECT 1 FROM dbo.ConfiguracionSistema WHERE Clave = 'IA_ModeloSimple')
    INSERT INTO dbo.ConfiguracionSistema (Clave, Valor, Descripcion)
    VALUES ('IA_ModeloSimple', '',
            'Modelo económico para tareas de plantilla fija (insights PYG/Balance, resumen gerencial, solo cifras). Vacío = usar siempre el modelo general.');

IF NOT EXISTS (SELECT 1 FROM dbo.ConfiguracionSistema WHERE Clave = 'IA_FactorCostoCache')
    INSERT INTO dbo.ConfiguracionSistema (Clave, Valor, Descripcion)
    VALUES ('IA_FactorCostoCache', '0.5',
            'Fracción del precio de entrada que se cobra por los tokens servidos desde la caché de prompts (0.5 = gpt-4o/4o-mini; ≈0.1 = gpt-5.x). Admite sufijo por modelo: IA_FactorCostoCache:gpt-5-mini.');
GO

/* 3) Tarifas por modelo (opcional, recomendado si se usa IA_ModeloSimple con un modelo de otro precio).
      Se escriben como IA_CostoPor1kTokensPrompt:<modelo> / IA_CostoPor1kTokensRespuesta:<modelo> (USD por 1.000 tokens).
      Si no existe la del modelo usado, se aplica la global. Ejemplo (verificar precios vigentes):

   INSERT INTO dbo.ConfiguracionSistema (Clave, Valor, Descripcion) VALUES
     ('IA_CostoPor1kTokensPrompt:gpt-4o-mini',    '0.00015', 'USD por 1.000 tokens de entrada de gpt-4o-mini'),
     ('IA_CostoPor1kTokensRespuesta:gpt-4o-mini', '0.0006',  'USD por 1.000 tokens de salida de gpt-4o-mini');
*/
