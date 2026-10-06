/* ============================================================================================
   019 — Página web de la empresa (2026-10). Ejecutar manualmente; es re-ejecutable.
   Gestor de Empresas: nuevo campo opcional "Página web" (URL http/https validada en el servidor).
   Antes de ejecutarlo la aplicación sigue funcionando, pero la página web no se puede guardar
   (el gestor avisa que falta la columna).
   ============================================================================================ */
IF COL_LENGTH('dbo.Empresas', 'EmpPaginaWeb') IS NULL
    ALTER TABLE dbo.Empresas ADD EmpPaginaWeb NVARCHAR(300) NULL;
GO
