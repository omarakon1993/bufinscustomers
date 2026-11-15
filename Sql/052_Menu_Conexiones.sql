-- =====================================================================================
-- 052 · Renombrar la opcion de menu «Integraciones API» a «Conexiones API»
-- Compatible con SQL Server 2012. MANUAL e idempotente (se puede correr varias veces).
-- Solo cambia textos: el codigo (ADMIN_CONFIG_INTEGRACIONES), la ruta y los permisos no cambian.
-- Alternativa sin SQL: Configuracion > Gestor de menus, editar la opcion con estos mismos textos.
-- =====================================================================================

UPDATE dbo.MenuOpciones
   SET Nombre      = 'Conexiones API',
       NombreEN    = 'API connections',
       Descripcion = 'Conecte cada empresa a BUFINS: su direccion de API y su clave para que su sistema envie los datos solo'
 WHERE Codigo = 'ADMIN_CONFIG_INTEGRACIONES';
GO
