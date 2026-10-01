-- Reemplaza el viejo tope numérico diario por usuario (Usuarios.LimiteConsultasIA) por un simple
-- interruptor de acceso: NULL = acceso permitido (valor por defecto), 0/false = sin acceso,
-- 1/true = acceso explícito. El control fino de "cuánto IA se puede gastar" pasa a ser el
-- presupuesto mensual de tokens de la empresa (ver 009_ConfiguracionIAEmpresa_CreateTable.sql),
-- no un conteo de consultas por usuario.
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Usuarios') AND name = 'AccesoConsultasIA')
BEGIN
    ALTER TABLE dbo.Usuarios ADD AccesoConsultasIA BIT NULL;
END
GO

-- Opcional — ejecutar manualmente más adelante, una vez confirmado que nada depende ya de la
-- columna vieja (quedó sin uso en el código desde este cambio):
-- ALTER TABLE dbo.Usuarios DROP COLUMN LimiteConsultasIA;
