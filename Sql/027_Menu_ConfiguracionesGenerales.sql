/* ============================================================================
   027 - Opcion de menu "Configuraciones generales BUFINS"
   ----------------------------------------------------------------------------
   Script MANUAL. Se puede ejecutar varias veces.
   La pantalla dejo de ser solo de IA: el controlador paso de ConfiguracionGlobalIA a
   ConfiguracionGlobal (URL nueva: /ConfiguracionGlobal). Este script actualiza la
   opcion de menu para que apunte al controlador nuevo y la renombra.

   IMPORTANTE: ejecutarlo al desplegar la version nueva del codigo (hasta entonces,
   el menu apunta al controlador anterior y viceversa).
   ============================================================================ */
SELECT Id, Codigo, Nombre, NombreEN, Controller FROM dbo.MenuOpciones
 WHERE Controller IN ('ConfiguracionGlobalIA', 'ConfiguracionGlobal');   -- antes

UPDATE dbo.MenuOpciones
   SET Controller = 'ConfiguracionGlobal',
       Nombre     = 'Configuraciones generales BUFINS',
       NombreEN   = 'BUFINS general settings'
 WHERE Controller IN ('ConfiguracionGlobalIA', 'ConfiguracionGlobal');

SELECT Id, Codigo, Nombre, NombreEN, Controller FROM dbo.MenuOpciones
 WHERE Controller = 'ConfiguracionGlobal';   -- despues
GO
