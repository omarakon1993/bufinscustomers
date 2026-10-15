/* ============================================================================
   031 - Aviso para que los usuarios conozcan y configuren las passkeys
   ----------------------------------------------------------------------------
   Script MANUAL. SQL Server 2012+. Se puede ejecutar varias veces.
   Guarda, por usuario, hasta cuando NO se le vuelve a mostrar la franja del Home que
   invita a agregar una passkey:
     NULL                  -> se muestra
     fecha futura          -> pospuesto ("Mas tarde": 7 dias)
     9999-12-31            -> "No volver a mostrar"
   Sin este script la franja del Home no aparece (no hay donde recordar la decision);
   la etiqueta "Nuevo" del menu y la oferta tras activar el 2FA funcionan igual.
   ============================================================================ */

IF COL_LENGTH('dbo.Usuarios', 'PasskeyAvisoHasta') IS NULL
    ALTER TABLE dbo.Usuarios ADD PasskeyAvisoHasta DATETIME NULL;
GO

SELECT COUNT(*) AS UsuariosConAvisoPospuesto FROM dbo.Usuarios WHERE PasskeyAvisoHasta IS NOT NULL;
GO
