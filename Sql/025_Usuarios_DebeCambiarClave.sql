-- Cambio de contraseña obligatorio en el primer inicio de sesión.
-- Ejecutar manualmente. Compatible con SQL Server 2012. Re-ejecutable.
IF COL_LENGTH('dbo.Usuarios', 'DebeCambiarClave') IS NULL
    ALTER TABLE dbo.Usuarios ADD DebeCambiarClave BIT NOT NULL CONSTRAINT DF_Usuarios_DebeCambiarClave DEFAULT (0);
GO
