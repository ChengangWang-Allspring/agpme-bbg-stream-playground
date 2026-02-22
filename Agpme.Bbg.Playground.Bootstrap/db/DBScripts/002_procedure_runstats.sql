--#################################
-- dependent procedure_runstatus objects
--#################################

CREATE TABLE IF NOT EXISTS app_data.procedure_runstatus (
    job_id SERIAL PRIMARY KEY,
    date_job_start TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    date_job_end TIMESTAMP,
    job_status_code INTEGER,
    job_status_description TEXT
);
ALTER TABLE IF EXISTS app_data.procedure_runstatus OWNER to postgres;

CREATE TABLE IF NOT EXISTS app_data.procedure_runstatus_detail (
    job_id INTEGER NOT NULL,
    procedure_name TEXT NOT NULL,
    parameter_list TEXT,
    status_text TEXT,
    date_status TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);
ALTER TABLE IF EXISTS app_data.procedure_runstatus_detail OWNER to postgres;
	
	
	
CREATE OR REPLACE PROCEDURE app_data.insert_procedure_runstatus(
    IN p_job_id INTEGER,
    IN p_procedure_name TEXT,
    IN p_parameter_list TEXT,
    IN p_status_text TEXT
)
LANGUAGE plpgsql
AS $$
BEGIN
    INSERT INTO app_data.procedure_runstatus_detail (
        job_id, procedure_name, parameter_list, status_text, date_status
    )
    VALUES (
        p_job_id, p_procedure_name, p_parameter_list, p_status_text, CURRENT_TIMESTAMP
    );
	
	-- This RAISE NOTICE is used to trace log steps during procedure execution.
	-- It helps identify the exact timestamp and job ID at each stage, which is 
	-- especially useful for debugging or auditing in case of rollback due to exceptions.
	RAISE NOTICE '% [%]: %', TO_CHAR(CURRENT_TIMESTAMP, 'YYYY-MM-DD HH24:MI:SS.MS'), p_job_id, p_status_text;	
END;
$$;

ALTER PROCEDURE app_data.insert_procedure_runstatus(INTEGER, TEXT, TEXT, TEXT) OWNER TO postgres;
	
	
CREATE OR REPLACE PROCEDURE app_data.insert_procedure_start(
    IN p_procedure_name TEXT,
    IN p_parameter_list TEXT,
    INOUT p_job_id INTEGER
)
LANGUAGE plpgsql
AS $$
BEGIN
    -- If job_id is NULL, this is a new parent job
    IF p_job_id IS NULL THEN
        INSERT INTO app_data.procedure_runstatus (date_job_start)
        VALUES (CURRENT_TIMESTAMP);

        SELECT currval(pg_get_serial_sequence('app_data.procedure_runstatus', 'job_id')) INTO p_job_id;
    END IF;

    -- Insert detail record for either parent or child procedure
    INSERT INTO app_data.procedure_runstatus_detail (
        job_id, procedure_name, parameter_list, status_text, date_status
    )
    VALUES (
        p_job_id, p_procedure_name, p_parameter_list, 'Started', CURRENT_TIMESTAMP
    );
END;
$$;
ALTER PROCEDURE app_data.insert_procedure_start(TEXT, TEXT, INTEGER) OWNER TO postgres;


CREATE OR REPLACE PROCEDURE app_data.insert_procedure_end(
    IN p_job_id INTEGER,
    IN p_procedure_name TEXT,
    IN p_parameter_list TEXT,
    IN p_job_status_code INTEGER DEFAULT NULL,
    IN p_job_status_description TEXT DEFAULT NULL
)
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE app_data.procedure_runstatus
    SET date_job_end = CURRENT_TIMESTAMP,
        job_status_code = p_job_status_code,
        job_status_description = p_job_status_description
    WHERE job_id = p_job_id;

    INSERT INTO app_data.procedure_runstatus_detail (
        job_id, procedure_name, parameter_list, status_text, date_status
    )
    VALUES (
        p_job_id, p_procedure_name, p_parameter_list, 'Completed', CURRENT_TIMESTAMP
    );
END;
$$;
ALTER PROCEDURE app_data.insert_procedure_end(INTEGER,TEXT, TEXT, INTEGER,TEXT) OWNER TO postgres;
