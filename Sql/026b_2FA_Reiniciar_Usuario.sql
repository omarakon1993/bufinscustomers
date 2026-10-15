/* ============================================================================
   026b - REINICIAR LOS METODOS DE AUTENTICACION DE UN USUARIO (emergencia)
   ----------------------------------------------------------------------------
   Usar SOLO si un usuario perdio el celular y sus passkeys y
   ningun Super Admin puede reiniciarlo desde el gestor de usuarios (por ejemplo,
   si es el unico Super Admin).
   El usuario configurara un autenticador nuevo en su siguiente inicio de sesion.
   ============================================================================ */
DECLARE @IdUsuario INT = 0;   -- <-- EDITAR: Id del usuario (SELECT Id, Usuario, Correo FROM dbo.Usuarios)

IF @IdUsuario > 0
BEGIN
    UPDATE dbo.Usuarios
       SET TotpActivo = 0, TotpSecretoCifrado = NULL, TotpActivadoEn = NULL, TotpUltimoPaso = NULL
     WHERE Id = @IdUsuario;

    -- Si ya ejecutaste el script 029 (passkeys), tambien se borran las passkeys del usuario.
    IF OBJECT_ID('dbo.UsuarioPasskeys', 'U') IS NOT NULL
        DELETE FROM dbo.UsuarioPasskeys WHERE IdUsuario = @IdUsuario;

    SELECT Id, Usuario, Correo, TotpActivo FROM dbo.Usuarios WHERE Id = @IdUsuario;
END
ELSE
    PRINT 'Edita @IdUsuario antes de ejecutar.';
GO
