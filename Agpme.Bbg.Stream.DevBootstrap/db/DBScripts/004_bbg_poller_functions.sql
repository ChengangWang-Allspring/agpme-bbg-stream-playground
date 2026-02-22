--#################################
-- Create bbg poller functions
--#################################

-- FUNCTION: app_data.is_numeric(text, integer, integer)

-- DROP FUNCTION IF EXISTS app_data.is_numeric(text, integer, integer);

CREATE OR REPLACE FUNCTION app_data.is_numeric(
	val text,
	p_precision integer,
	p_scale integer)
    RETURNS boolean
    LANGUAGE 'plpgsql'
    COST 100
    IMMUTABLE PARALLEL UNSAFE
AS $BODY$

DECLARE
  cleaned TEXT;
  num NUMERIC;
  max_abs NUMERIC;
BEGIN

	/* ********************************************************
	   PURPOSE: Determines whether the input text represents a valid numeric value
	            with a given precision and scale. It removes commas, validates
	            numeric format, rounds to scale, and checks if the value fits
	            within the allowed precision and scale.
	
	   CHANGE LOG:
	       2025/10/13 v1.00 Created by Chenggang Wang
	********************************************************* */
	-- Remove commas
	cleaned := REGEXP_REPLACE(val, ',', '', 'g');
	
	-- Validate numeric format first
	IF cleaned !~ '^[-+]?[0-9]*\.?[0-9]+$' THEN
	RETURN FALSE;
	END IF;
	
	-- Safe to cast now
	num := cleaned::NUMERIC;
	
	-- Round to scale
	num := ROUND(num, p_scale);
	
	-- Calculate max absolute value allowed
	max_abs := POWER(10, p_precision - p_scale);
	
	-- Check if number fits within precision/scale
	RETURN ABS(num) < max_abs;
END;
$BODY$;

ALTER FUNCTION app_data.is_numeric(text, integer, integer)
    OWNER TO postgres;


