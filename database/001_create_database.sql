-- TIAANO VMS bootstrap
IF DB_ID('TiaanoVms') IS NULL
BEGIN
    CREATE DATABASE TiaanoVms;
END
GO
