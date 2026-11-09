/*
  044 - Acceso a la documentacion: que documentos son SOLO para Super Admin.
  Script manual (SQL Server 2012 o superior). Es seguro ejecutarlo mas de una vez.

  Que hace
    1) Crea dbo.DocumentacionAcceso (una fila por documento de la biblioteca "Documentacion BUFINS" cuyo valor se haya cambiado en pantalla).
       Sin fila, el documento usa su valor por defecto del catalogo (Helpers/DocumentacionBibliotecaCatalogo): los de seguridad, los
       cargues y la documentacion tecnica son solo Super Admin; los de informes los ven quienes tengan el informe.
    2) (OPCIONAL, paso 2 abajo) Permite asignar la opcion de menu "Documentacion BUFINS" a otros usuarios. Mientras no se ejecute,
       solo el Super Admin entra a la biblioteca (comportamiento actual). Quien la reciba ve unicamente los documentos que NO esten
       marcados como solo Super Admin; los marcados siguen restringidos tambien en la capsula "Documentacion" de cada informe.

  Sin este script la pantalla funciona con los valores por defecto y avisa que no se pueden guardar cambios.
*/
IF OBJECT_ID('dbo.DocumentacionAcceso', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DocumentacionAcceso
    (
        Clave              NVARCHAR(80) NOT NULL,           -- clave del documento en la biblioteca (p. ej. informe-pyg)
        SoloSuperAdmin     BIT          NOT NULL,
        FechaActualizacion DATETIME     NOT NULL CONSTRAINT DF_DocumentacionAcceso_Fecha DEFAULT (GETDATE()),
        IdUsuario          INT          NULL,               -- quien hizo el ultimo cambio
        CONSTRAINT PK_DocumentacionAcceso PRIMARY KEY (Clave)
    );
END
GO

/* ---- PASO 2 (opcional): abrir la opcion de menu a usuarios que no son Super Admin ----
   Ejecutar solo cuando quiera que Admins de Empresa / usuarios puedan ver la biblioteca (con sus permisos asignados desde Permisos).

UPDATE dbo.MenuOpciones SET SoloSuperAdmin = 0 WHERE Codigo = 'ADMIN_DOCUMENTACION_BUFINS';
*/
