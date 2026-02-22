-- PROCEDURE: app_data.bbg_upsert_positions_from_inbound(boolean, character varying, date, character varying, character varying)

-- DROP PROCEDURE IF EXISTS app_data.bbg_upsert_positions_from_inbound(boolean, character varying, date, character varying, character varying);

CREATE OR REPLACE PROCEDURE app_data.bbg_upsert_positions_from_inbound(
	IN p_is_intraday boolean DEFAULT false,
	IN p_load_process character varying DEFAULT NULL::character varying,
	IN p_as_of_date date DEFAULT NULL::date,
	IN p_account_id_intraday character varying DEFAULT NULL::character varying,
	IN p_load_bb_entity_name character varying DEFAULT NULL::character varying)
LANGUAGE 'plpgsql'
AS $BODY$

/* ********************************************************
	CHANGE LOG:
	v1.00 LMV - Created to support Bloomberg Realtime Poller.
	Combined intial paint and intraday.
    v1.04 LMV - 12/01 Renamed account_currency and re-raise exception to be trapped on .net
    v1.05 LMV - 12/08 . Replaced quantity_sod => quantity_current_initial_paint, market_value_sod => market_value_initial_paint
                      . Added quantity_proposed_initial_paint, quantity_proposed
   v1.06 LMV - 12/16 Change to positions_inbound raw data
   v1.07 LMV - 01/08/26
             . Add fx rollup.
             . Removed position_without_pending <>  '0'; Inbound is already filtered and intraday can be short
   v1.08 LMV - 01/20/26 Handle short/long netting into a single position
   v1.09 LMV - 01/26/26
            Modified FX processing to use `deal_ticket_num` as the primary identifier.
            Per Steve’s review, FX is providing `deal_ticket_num`. Noticed that it corresponds to `master_ticket` in our tradeloader process and PM.
   v1.10 LMV - 02/03/26 Beta is sending deal deal_ticket_num = 0. Add a fix to exclude 0
   change app_config.bbg_positions_inbound_cols_map deal_ticket_num transform_expr = 'CAST(NULLIF(trim(deal_ticket_num), ''0'') as INTEGER)'
   v1.11 LMV - 02/06/26 Fix FX MV% calculation. Previously, MV% used total NAV FX only, not the portfolio’s full total NAV.
                        Added FX fields into the ins_bbg CTE and ensured they are included in the UNION ALL pipeline.
   v1.12 LMV - 02/10/26 FX having exposure already converted. Moved FX to its own section.
               Dynamic procedure will be migrated into a static proc to help during support.
*/

DECLARE
    v_job_id INTEGER := NULL;
    v_row_count INTEGER := 0;
    v_proc_name VARCHAR :='app_data.bbg_upsert_positions_from_inbound';
    v_status_msg TEXT;

    v_select_list   text;
    v_target_list   text;
    v_update_list   text;
    v_sql           text;
    v_sql_weights   text;
    v_weight_list_select   text;
    v_weight_list_upd   text;
    v_rows        bigint := 0;
BEGIN
    -- Start procedure logging
    CALL app_data.insert_procedure_start(v_proc_name, '', v_job_id);
--RAISE NOTICE 'Proc Name: %', v_proc_name;


    v_status_msg := FORMAT(
            'is_intraday=%s, load_process=%s, as_of_date=%s, account_id_intraday=%s, load_bb_entity_name=%s',
            p_is_intraday,
            COALESCE(p_load_process, 'NULL'),
            COALESCE(p_as_of_date::TEXT, 'NULL'),
            COALESCE(p_account_id_intraday, 'NULL'),
            COALESCE(p_load_bb_entity_name, 'NULL')
                    );
    CALL app_data.insert_procedure_runstatus(v_job_id, v_proc_name, '', v_status_msg);
--RAISE NOTICE 'Status: %', v_status_msg;

    BEGIN

        IF p_is_intraday THEN
            v_status_msg = 'Processing intraday records that are not yet processed';
            CALL app_data.insert_procedure_runstatus(v_job_id, v_proc_name, '', v_status_msg);
            --RAISE NOTICE 'Status: %', v_status_msg;
        ELSE
            v_status_msg = 'Processing initial painting records that are not yet processed';
            CALL app_data.insert_procedure_runstatus(v_job_id, v_proc_name, '', v_status_msg);
            --RAISE NOTICE 'Status: %', v_status_msg;
        END IF;

        v_status_msg = 'Processing initial painting records that are not yet processed';
        CALL app_data.insert_procedure_runstatus(v_job_id, v_proc_name, '', v_status_msg);
        --RAISE NOTICE 'Status: %', v_status_msg;

        /* select cols */
        SELECT
            string_agg(
                    format(
                            '%s AS %s',
                            COALESCE(
                                -- Replace token 'source_column' with the quoted actual source identifier
                                    replace(m.transform_expr, 'source_column', quote_ident(m.source_column)),
                                    quote_ident(m.source_column)
                            ),
                            quote_ident(m.target_column)
                    ),
                    ', ' ORDER BY m.target_column
            ) AS select_list
        INTO v_select_list
        FROM app_config.bbg_positions_inbound_cols_map m where is_active=true and source_kind <> 'computed_upd';


        /* target cols */
        select
            string_agg(quote_ident(m.target_column), ', ' ORDER BY m.target_column) AS target_list
        INTO v_target_list
        FROM app_config.bbg_positions_inbound_cols_map m where is_active=true and is_required=true;

        /* update cols */
        SELECT
            string_agg(replace(m.update_set_expr, 'target_column', quote_ident(m.target_column)), ', '
                       ORDER BY m.target_column
            ) AS update_list
        INTO v_update_list
        FROM app_config.bbg_positions_inbound_cols_map m
        WHERE is_active = TRUE
          AND is_required = TRUE
          AND update_set_expr is not null;

        IF v_select_list IS NULL OR v_target_list IS NULL THEN
            RAISE EXCEPTION 'No mappings found in app_config.bbg_column_map';
        END IF;

        /* Inserting all income api data. API is receiving data from a SAIM Profile that is displays multiple rows. FX is on upcoming discussions. */
        /* select autogenerated based on bbg_positions_inbound_cols_map definition */
        /*
        	• Enriches inbound Bloomberg positions from app_data.bbg_positions_inbound into a CTE (strict_inbound).
	        • Identifies “short” securities that have multiple rows per account/ID to roll them up into one
        	  consolidated row.
	        • Splits FX vs non‑FX processing:
		      - Non‑FX: inserts a union of (a) best row per key (row_number = 1) and (b) rolled‑up shorts.
		      - FX: inserts from the FX subset based on deal_ticket_num.
	        • Upserts into app_data.bbg_positions :
		      - Non‑FX and FX
              - On conflict, update

        	 1) strict_inbound
                This is gets all data layer:
	            • Cleans/normalizes types, trims and casts
	            • Derives keys/flags:
		            - enforce_uniqueness: special‑cases currency rows to relax uniqueness while bloomberg send us accrued.
        	          Cash is supposed to replace the income value, but BB at some instances is sending us
        	          two cash including accrued. (e.g., id_bb_unique = 'IX244867-0' with position mismatches).
		        . fx_flag: detects FX/FX‑forward style rows (based on base_crncy, security content).
		        . fut_flag, swap_flag: derived from FUT/Swap fields.
		        . figi: uses fallback NOFIGI_ROW_ID_{id} if FIGI is missing to keep rows addressable, but can be dropped
	            . Calculates portfolio weights (exposure and market value percentages):
		        . exposure_pct and market_value_pct .
	            . Derived Classified instrument based on Yellow Key
		            - loader_derived_asset_class, loader_derived_instrument_type, and loader_derived_yellow_key
        	        derived from security patterns.
	            . De‑dupes with ROW_NUMBER():
		        . Partitioned by supporting rollup aggregation.
        	    CTE is filter by (example):
        	    WHERE is_processed = false
                  AND as_of_date = '2026-02-10'
                  AND load_bb_entity_name = 'MLACCTS'
                  AND load_process = 'BBGRealtimePoller_PlusFI_MLACCTS'

        	2) PositionShortIdentifier
        	   Finds (account_id, id_bb_unique) that have multiple short rows (short_flag = true) so we can roll them
        	   up into a single consolidated row insert.

        	3) bbg_fx
        	   Extracts FX‑eligible rows from strict_inbound where:
        	   . fx_flag = true
        	   . deal_ticket_num is present and non‑zero.
               . feed the FX‑only insert/upsert path.

            4) ins_bbg_all_but_fx (Non‑FX insert + upsert)
               Performs a single INSERT into app_data.bbg_positions for Non‑FX rows based on a UNION of:
	           i. de‑dup rows\ row_number = 1, fx_flag = false, and not a short roll‑up member.
	           ii. short roll‑up rows\ agg short duplicates for the same (account_id, id_bb_unique, as_of_date):
		        . sums: market_value, quantity, exposure.
		        . BOOL flags like swap_flag, fut_flag.
		        . max fields

        	5) FX Insert + Upsert
        	   . FX rows are unique by ticket and a few dimensions—separate from Non‑FX logic.

        */
        v_sql := format('WITH strict_inbound AS (' ||
                'SELECT %s FROM app_data.bbg_positions_inbound WHERE is_processed = ''false''  ' ||
                ' AND as_of_date=''%s''' ||
                ' AND load_bb_entity_name=''%s''' ||
                ' AND load_process=''%s'') ' ||
                ' , /*Positions short*/
                    PositionShortIdentifier (short_account_id, short_bb_unique_id) AS (
                        select account_id,id_bb_unique from strict_inbound
                                where id_bb_unique in (select id_bb_unique
                                                       from strict_inbound
                                                       where short_flag=true)
                                group by account_id,id_bb_unique
                                having count(*)>1) ' ||
                '/*All FX*/'
                ' , bbg_fx as ('
                     ' SELECT %s FROM strict_inbound WHERE fx_flag = true and deal_ticket_num IS NOT NULL AND deal_ticket_num != 0 /*only fx has deal_ticket_num*/
                    )'
                '/*Insert all Non-FX*/'
                ' , ins_bbg_all_but_fx as ('
                    ' INSERT INTO app_data.bbg_positions (%s) '
                    ' SELECT %s FROM strict_inbound WHERE row_number = 1 AND fx_flag = false and id_bb_unique not in (select _short.short_bb_unique_id from PositionShortIdentifier _short) ' ||
                    ' UNION ALL /*handle short rollup. Improve select by migrating into a new map column specific for this short calc*/ ' ||
                ' SELECT
                    account_id,
                    as_of_date,
                    MAX(bb_security_identifier_desc)                             AS bb_security_identifier_desc,
                    MAX(create_user)                                             AS create_user,
                    MAX(currency)                                                AS currency,
                    MAX(cusip)                                                   AS cusip,
                    MAX(deal_ticket_num)                                         AS deal_ticket_num,
                    BOOL_AND(COALESCE(enforce_uniqueness, FALSE))                AS enforce_uniqueness,
                    SUM(COALESCE(exposure_amount, 0))                            AS exposure_amount,
                    SUM(COALESCE(exposure_pct, 0))                               AS exposure_pct,
                    MAX(figi)                                                    AS figi,
                    BOOL_OR(COALESCE(fut_flag, FALSE))                           AS fut_flag,
                    BOOL_OR(COALESCE(fx_flag, FALSE))                            AS fx_flag,
                    MAX(fx_rate)                                                 AS fx_rate,
                    id_bb_unique,
                    MAX(inbound_row_id)                                          AS inbound_row_id,
                    MAX(isin)                                                    AS isin,
                    MAX(last_update)                                             AS last_update,
                    MAX(last_update_by)                                          AS last_update_by,
                    MAX(load_bb_action)                                          AS load_bb_action,
                    MAX(load_bb_entity_name)                                     AS load_bb_entity_name,
                    MAX(load_bb_entity_type)                                     AS load_bb_entity_type,
                    MAX(load_bb_uuid)                                            AS load_bb_uuid,
                    MAX(load_date)                                               AS load_date,
                    MAX(loader_derived_asset_class)                              AS loader_derived_asset_class,
                    MAX(loader_derived_instrument_type)                          AS loader_derived_instrument_type,
                    MAX(loader_derived_yellow_key)                               AS loader_derived_yellow_key,
                    MAX(load_order)                                              AS load_order,
                    MAX(load_process)                                            AS load_process,
                    MAX(load_status)                                             AS load_status,
                    SUM(COALESCE(market_value_current, 0))                       AS market_value_current,
                    SUM(COALESCE(market_value_current_pct, 0))                   AS market_value_current_pct,
                    SUM(COALESCE(market_value_initial_paint, 0))                 AS market_value_initial_paint,
                    SUM(COALESCE(market_value_initial_paint_pct, 0))             AS market_value_initial_paint_pct,
                    SUM(COALESCE(market_value_raw, 0))                           AS market_value_raw,
                    MAX(msg_request_id)                                          AS msg_request_id,
                    MAX(COALESCE(nav_poller_calculated, 0))                      AS nav_poller_calculated,
                    SUM(COALESCE(quantity_current, 0))                           AS quantity_current,
                    SUM(COALESCE(quantity_current_initial_paint, 0))             AS quantity_current_initial_paint,
                    SUM(COALESCE(quantity_proposed, 0))                          AS quantity_proposed,
                    SUM(COALESCE(quantity_proposed_initial_paint, 0))            AS quantity_proposed_initial_paint,
                    BOOL_OR(COALESCE(swap_flag, FALSE))                          AS swap_flag,
                    CASE WHEN BOOL_OR(psi.short_bb_unique_id IS NOT NULL)  THEN TRUE  ELSE FALSE END AS short_flag
                    FROM strict_inbound si
                     LEFT JOIN PositionShortIdentifier psi
                            ON psi.short_account_id   = si.account_id
                           AND psi.short_bb_unique_id = si.id_bb_unique
                    where id_bb_unique in (select _short.short_bb_unique_id from PositionShortIdentifier _short)
                          or (fx_flag = true and (deal_ticket_num IS NULL OR deal_ticket_num = 0))
                    group by account_id, id_bb_unique, as_of_date
                     ON CONFLICT (
                    account_id,
                    as_of_date,
                    COALESCE(figi, ''''),
                    COALESCE(cusip, ''''),
                    load_bb_entity_type,
                    msg_request_id
                    )
                    WHERE enforce_uniqueness IS TRUE
                DO UPDATE
                       SET %s ) /*stop ins_bbg*/' ||

                '/*Insert FX only*/'
                ' INSERT INTO app_data.bbg_positions (%s)
                     SELECT %s FROM bbg_fx WHERE fx_flag=true
                     ON CONFLICT (
                        account_id,
                        as_of_date,
                        deal_ticket_num,
                        currency,
                        load_bb_entity_type,
                        msg_request_id
                        )
                    DO UPDATE
                       SET %s; ',
                v_select_list, /*SELECT %s FROM app_data.bbg_positions_inbound*/
                p_as_of_date,  /*AND as_of_date=%s*/
                p_load_bb_entity_name, /*AND load_bb_entity_name=%s*/
                p_load_process, /*AND load_process=%s*/
                v_target_list, /*SELECT %s FROM strict_inbound WHERE fx_flag = true*/
                v_target_list, /*INSERT INTO app_data.bbg_positions (%s) */
                v_target_list, /*SELECT %s FROM strict_inbound WHERE row_number = 1*/
                v_update_list, /* SET %s stop ins_bbg */

                v_target_list, /*INSERT INTO app_data.bbg_positions (%s) */
                v_target_list, /*SELECT %s FROM bbg_fx*/
                v_update_list
                 );

        -- Execute and report affected rows; consider if result query should be cached
        --RAISE NOTICE 'SQL: %', v_sql;

        CALL app_data.insert_procedure_runstatus(v_job_id, v_proc_name, '', 'DUMP-SQL (main): ' || v_sql);
        EXECUTE v_sql;

        -- Get number of rows inserted
        GET DIAGNOSTICS v_row_count = ROW_COUNT;
        v_status_msg := FORMAT('total rows inserted or updated: %s', v_row_count);
        CALL app_data.insert_procedure_runstatus(v_job_id, v_proc_name, '', v_status_msg);
        --RAISE NOTICE 'Status: %', v_status_msg;


        IF p_is_intraday THEN
            /* Intraday insert, recalculate weights */
            SELECT string_agg(format('%s AS %s',
                                     COALESCE(replace(m.transform_expr, 'target_column', quote_ident(m.target_column)),
                                              quote_ident(m.target_column))
                                  ,quote_ident(m.target_column)
                              ), ', ' ORDER BY m.target_column
                   ) AS _select_list,
                   string_agg(replace(m.update_set_expr, 'target_column', quote_ident(m.target_column)), ', '
                              ORDER BY m.target_column
                   ) AS _update_weight_list
            INTO v_weight_list_select, v_weight_list_upd
            FROM app_config.bbg_positions_inbound_cols_map m
            WHERE is_active = TRUE AND source_kind='computed_upd';

            v_sql_weights := format(
                    'WITH WeightData AS (' ||
                    'SELECT account_id, load_order, %s FROM app_data.bbg_positions ' ||
                    ' WHERE account_id = ''%s'' AND as_of_date=''%s'' AND load_bb_entity_name=''%s'' )' ||
                    ' UPDATE app_data.bbg_positions p SET ' ||
                    ' last_update_by = CURRENT_USER, last_update = CURRENT_TIMESTAMP, %s' ||
                    ' FROM WeightData ' ||
                    ' WHERE p.account_id = ''%s'' ' ||
                    ' AND p.as_of_date = ''%s'' ' ||
                    ' AND load_bb_entity_name = ''%s'' ' ||
                    ' AND p.load_order = WeightData.load_order ',
                    v_weight_list_select,
                    p_account_id_intraday,
                    p_as_of_date,
                    p_load_bb_entity_name,
                    v_weight_list_upd,
                    p_account_id_intraday,
                    p_as_of_date,
                    p_load_bb_entity_name
                             );

            --RAISE NOTICE 'SQL: %', v_sql_weights;
            CALL app_data.insert_procedure_runstatus(v_job_id, v_proc_name, '', 'DUMP-SQL (weight): ' || v_sql_weights);
            EXECUTE v_sql_weights;

            GET DIAGNOSTICS v_row_count = ROW_COUNT;
            v_status_msg := FORMAT('Recalculated mv,exposure pct; nav; rows updated: %s', v_row_count);
            CALL app_data.insert_procedure_runstatus(v_job_id, v_proc_name, '', v_status_msg);
            --RAISE NOTICE 'Status: %', v_status_msg;

            UPDATE app_data.bbg_positions_inbound
            SET is_processed = 'true'
            WHERE load_process = p_load_process AND is_processed = 'false'
              AND is_intraday = 'true' AND as_of_date = p_as_of_date
              AND load_bb_entity_name = p_load_bb_entity_name; /* AND position_without_pending <>  '0';*/

            GET DIAGNOSTICS v_row_count = ROW_COUNT;
            v_status_msg := FORMAT('total intraday rows updated is_processed=true: %s', v_row_count);
            CALL app_data.insert_procedure_runstatus(v_job_id, v_proc_name, '', v_status_msg);


            /* Painting inserted; Mark is_processed to true */
        ELSE
            UPDATE app_data.bbg_positions_inbound
            SET is_processed = 'true'
            WHERE load_process = p_load_process AND is_processed = 'false'
              AND is_intraday = 'false' AND as_of_date = p_as_of_date
              AND load_bb_entity_name = p_load_bb_entity_name; /*AND position_without_pending <>  '0';*/

            GET DIAGNOSTICS v_row_count = ROW_COUNT;
            v_status_msg := FORMAT('total intial paint rows updated is_processed=true: %s', v_row_count);
            CALL app_data.insert_procedure_runstatus(v_job_id, v_proc_name, '', v_status_msg);
            --RAISE NOTICE 'Status: %', v_status_msg;
        END IF;

    EXCEPTION
        WHEN OTHERS THEN
            RAISE WARNING 'Failed: %s', SQLERRM;
            v_status_msg := FORMAT(
                    'is_intraday=%s, load_process=%s, as_of_date=%s, account_id_intraday=%s, load_bb_entity_name=%s, Warning=%s',
                    p_is_intraday,
                    COALESCE(p_load_process, 'NULL'),
                    COALESCE(p_as_of_date::TEXT, 'NULL'),
                    COALESCE(p_account_id_intraday, 'NULL'),
                    COALESCE(p_load_bb_entity_name, 'NULL'),
                    SQLERRM
                            );
-- Re-raise the error so the caller sees it
            RAISE EXCEPTION 'Process failed: %', SQLERRM;

    END;
END;
$BODY$;


ALTER PROCEDURE app_data.bbg_upsert_positions_from_inbound(boolean, character varying, date, character varying, character varying) OWNER TO postgres;


