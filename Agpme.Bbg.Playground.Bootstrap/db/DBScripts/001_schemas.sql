--#################################
-- Create the two schemas 
--#################################

CREATE SCHEMA IF NOT EXISTS app_data;
CREATE SCHEMA IF NOT EXISTS app_config;

-- Assign ownership to a test role/username `postgres`
ALTER SCHEMA app_data OWNER TO postgres;
ALTER SCHEMA app_config OWNER TO postgres;
