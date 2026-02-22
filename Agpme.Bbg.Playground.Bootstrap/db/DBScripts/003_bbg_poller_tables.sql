--#################################
-- Create bbg poller related metadata, inbound and final tables
--#################################

CREATE SEQUENCE IF NOT EXISTS app_config.bbg_column_map_map_id_seq
    INCREMENT 1
    START 1
    MINVALUE 1
    MAXVALUE 9223372036854775807
    CACHE 1;

ALTER SEQUENCE app_config.bbg_column_map_map_id_seq OWNER TO postgres;

CREATE TABLE IF NOT EXISTS app_config.bbg_positions_inbound_cols_map
(
    map_id bigint NOT NULL DEFAULT nextval('app_config.bbg_column_map_map_id_seq'::regclass),
    domain text COLLATE pg_catalog."default" NOT NULL DEFAULT 'positions'::text,
    source_column text COLLATE pg_catalog."default" NOT NULL,
    target_column text COLLATE pg_catalog."default" NOT NULL,
    data_type text COLLATE pg_catalog."default" NOT NULL,
    comments text COLLATE pg_catalog."default",
    is_active boolean NOT NULL DEFAULT true,
    is_required boolean NOT NULL DEFAULT false,
    default_expr text COLLATE pg_catalog."default",
    transform_expr text COLLATE pg_catalog."default",
    update_set_expr text COLLATE pg_catalog."default",
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    created_by text COLLATE pg_catalog."default" NOT NULL DEFAULT CURRENT_USER,
    updated_at timestamp with time zone NOT NULL DEFAULT now(),
    updated_by text COLLATE pg_catalog."default" NOT NULL DEFAULT CURRENT_USER,
    source_kind text COLLATE pg_catalog."default" NOT NULL DEFAULT 'json'::text,
    CONSTRAINT bbg_column_map_pkey PRIMARY KEY (map_id),
    CONSTRAINT bbg_column_map_source_kind_check CHECK (source_kind = ANY (ARRAY['json'::text, 'loader'::text, 'default'::text, 'computed'::text, 'computed_upd'::text, 'internal'::text]))
);

ALTER TABLE app_config.bbg_positions_inbound_cols_map OWNER to postgres;


CREATE SEQUENCE IF NOT EXISTS app_data.bbg_positions_inbound_id_seq
    INCREMENT 1
    START 1
    MINVALUE 1
    MAXVALUE 9223372036854775807
    CACHE 1;

ALTER SEQUENCE app_data.bbg_positions_inbound_id_seq OWNER TO postgres;


CREATE SEQUENCE IF NOT EXISTS app_data.bbg_positions_inbound_load_order_seq
    INCREMENT 1
    START 1
    MINVALUE 1
    MAXVALUE 2147483647
    CACHE 1;

ALTER SEQUENCE app_data.bbg_positions_inbound_load_order_seq OWNER TO postgres;


CREATE SEQUENCE IF NOT EXISTS app_data.bbg_positions_inbound_row_id_seq
    INCREMENT 1
    START 1
    MINVALUE 1
    MAXVALUE 9223372036854775807
    CACHE 1;

ALTER SEQUENCE app_data.bbg_positions_inbound_row_id_seq OWNER TO postgres;


CREATE SEQUENCE IF NOT EXISTS app_data.bbg_positions_load_order_seq
    INCREMENT 1
    START 1
    MINVALUE 1
    MAXVALUE 2147483647
    CACHE 1;

ALTER SEQUENCE app_data.bbg_positions_load_order_seq OWNER TO postgres;


CREATE TABLE IF NOT EXISTS app_data.bbg_positions_inbound
(
    id bigint NOT NULL DEFAULT nextval('app_data.bbg_positions_inbound_id_seq'::regclass),
    as_of_date date,
    load_bb_entity_name text COLLATE pg_catalog."default",
    account text COLLATE pg_catalog."default",
    id_bb_unique text COLLATE pg_catalog."default",
    id_cusip text COLLATE pg_catalog."default",
    id_bb_global text COLLATE pg_catalog."default",
    id_isin text COLLATE pg_catalog."default",
    crncy text COLLATE pg_catalog."default",
    security text COLLATE pg_catalog."default",
    id_firm_id text COLLATE pg_catalog."default",
    action text COLLATE pg_catalog."default",
    amt_outstanding text COLLATE pg_catalog."default",
    ask_spread text COLLATE pg_catalog."default",
    ask_yield_aim text COLLATE pg_catalog."default",
    audit_identifier text COLLATE pg_catalog."default",
    avg_cost text COLLATE pg_catalog."default",
    base_crncy text COLLATE pg_catalog."default",
    best_execution_custom_note text COLLATE pg_catalog."default",
    bid_spread_to_benchmark text COLLATE pg_catalog."default",
    bid_yield_aim text COLLATE pg_catalog."default",
    bnchmrk_pricer text COLLATE pg_catalog."default",
    bnchmrk_sec text COLLATE pg_catalog."default",
    book_total_transaction_cost text COLLATE pg_catalog."default",
    business_unit_name text COLLATE pg_catalog."default",
    business_unit_number text COLLATE pg_catalog."default",
    cavc_real_pl text COLLATE pg_catalog."default",
    cavc_unreal_pl text COLLATE pg_catalog."default",
    cfd text COLLATE pg_catalog."default",
    correlation_id text COLLATE pg_catalog."default",
    cum_avg_cost text COLLATE pg_catalog."default",
    cum_rlzd_pnl_firm_crncy text COLLATE pg_catalog."default",
    cum_trans_cost text COLLATE pg_catalog."default",
    cumul_avg_cost text COLLATE pg_catalog."default",
    cumul_cost_incl_trans text COLLATE pg_catalog."default",
    daily_rlzd_fxe text COLLATE pg_catalog."default",
    daily_unrlzd_fxe text COLLATE pg_catalog."default",
    deal_counterparty text COLLATE pg_catalog."default",
    deal_order_num text COLLATE pg_catalog."default",
    deal_ticket_num text COLLATE pg_catalog."default",
    dealer_branch_country_default text COLLATE pg_catalog."default",
    dealer_onboarding_lei_cntrprty text COLLATE pg_catalog."default",
    delta_shares text COLLATE pg_catalog."default",
    des_format2 text COLLATE pg_catalog."default",
    dlr_onboarding_branch_cntry_rec text COLLATE pg_catalog."default",
    dv01_scaled_hc_lgm_model text COLLATE pg_catalog."default",
    equity_sales_credit text COLLATE pg_catalog."default",
    equity_total_markup text COLLATE pg_catalog."default",
    eqy_buy_share_volume text COLLATE pg_catalog."default",
    eqy_sell_share_volume text COLLATE pg_catalog."default",
    execution_allocation_id text COLLATE pg_catalog."default",
    exp_val01_cn text COLLATE pg_catalog."default",
    fut_contract_dt text COLLATE pg_catalog."default",
    fut_month_yr text COLLATE pg_catalog."default",
    fx_avg_cost text COLLATE pg_catalog."default",
    fx_prc text COLLATE pg_catalog."default",
    fx_rate text COLLATE pg_catalog."default",
    gross_exposure text COLLATE pg_catalog."default",
    int_acc_dt text COLLATE pg_catalog."default",
    issue_dt2 text COLLATE pg_catalog."default",
    ivol text COLLATE pg_catalog."default",
    local_market_value text COLLATE pg_catalog."default",
    long_exposure text COLLATE pg_catalog."default",
    mark_to_market_px text COLLATE pg_catalog."default",
    mark_to_market_px_disc_dec text COLLATE pg_catalog."default",
    maturity text COLLATE pg_catalog."default",
    mkt_val_cost_cn text COLLATE pg_catalog."default",
    modelled_swap_market_value text COLLATE pg_catalog."default",
    mtd_dt_total_pl text COLLATE pg_catalog."default",
    net_cur_pos_eqy text COLLATE pg_catalog."default",
    net_exposure text COLLATE pg_catalog."default",
    net_volume text COLLATE pg_catalog."default",
    npv_net_open_position text COLLATE pg_catalog."default",
    npv_pos_cn text COLLATE pg_catalog."default",
    nxt_call_dt text COLLATE pg_catalog."default",
    nxt_call_px text COLLATE pg_catalog."default",
    portflio_val text COLLATE pg_catalog."default",
    pos_cn text COLLATE pg_catalog."default",
    pos_tw text COLLATE pg_catalog."default",
    position_gamma_dollar text COLLATE pg_catalog."default",
    position_plus_pending text COLLATE pg_catalog."default",
    position_without_pending text COLLATE pg_catalog."default",
    primebroker text COLLATE pg_catalog."default",
    ps_position_identifier text COLLATE pg_catalog."default",
    px_ask text COLLATE pg_catalog."default",
    px_bid text COLLATE pg_catalog."default",
    px_source text COLLATE pg_catalog."default",
    real_first_cpn_dt text COLLATE pg_catalog."default",
    realized_pnl_with_fx_exposure text COLLATE pg_catalog."default",
    realized_pl_incl_trans text COLLATE pg_catalog."default",
    red_pair_code text COLLATE pg_catalog."default",
    rlzd_pl text COLLATE pg_catalog."default",
    sales_associated_branch_country text COLLATE pg_catalog."default",
    sales_associated_lei text COLLATE pg_catalog."default",
    sec_id_flag text COLLATE pg_catalog."default",
    short_exposure text COLLATE pg_catalog."default",
    shortflag text COLLATE pg_catalog."default",
    strategy text COLLATE pg_catalog."default",
    sw_market_val text COLLATE pg_catalog."default",
    sw_pay_cpn text COLLATE pg_catalog."default",
    sw_rec_cpn text COLLATE pg_catalog."default",
    switch_number text COLLATE pg_catalog."default",
    tds_credit_exposure text COLLATE pg_catalog."default",
    tot_pnl_with_fx_exposure_firm text COLLATE pg_catalog."default",
    total_commis text COLLATE pg_catalog."default",
    total_pl text COLLATE pg_catalog."default",
    trader text COLLATE pg_catalog."default",
    trader_associated_branch_country text COLLATE pg_catalog."default",
    trader_associated_lei text COLLATE pg_catalog."default",
    trans_cntparty text COLLATE pg_catalog."default",
    trans_cntparty_short_name text COLLATE pg_catalog."default",
    transaction_cost_5 text COLLATE pg_catalog."default",
    trext_xtktno text COLLATE pg_catalog."default",
    ts_pricing_method text COLLATE pg_catalog."default",
    unrealized_pnl_incl_trans_costs text COLLATE pg_catalog."default",
    unrealized_pnl_with_fx_exposure text COLLATE pg_catalog."default",
    unrlzd_pl text COLLATE pg_catalog."default",
    yld_flag text COLLATE pg_catalog."default",
    ytd_total_pl text COLLATE pg_catalog."default",
    load_bb_entity_type text COLLATE pg_catalog."default",
    load_bb_uuid text COLLATE pg_catalog."default",
    msg_request_id text COLLATE pg_catalog."default",
    load_status text COLLATE pg_catalog."default",
    load_process text COLLATE pg_catalog."default",
    load_date timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    create_user text COLLATE pg_catalog."default" DEFAULT CURRENT_USER,
    last_update timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    last_update_by text COLLATE pg_catalog."default" DEFAULT CURRENT_USER,
    is_processed text COLLATE pg_catalog."default" DEFAULT 'false'::text,
    is_intraday text COLLATE pg_catalog."default" DEFAULT 'false'::text,
    record_created_by text COLLATE pg_catalog."default" DEFAULT CURRENT_USER,
    record_created_at timestamp with time zone DEFAULT now(),
    load_order integer NOT NULL GENERATED BY DEFAULT AS IDENTITY ( INCREMENT 1 START 1 MINVALUE 1 MAXVALUE 2147483647 CACHE 1 ),
    enforce_uniqueness boolean DEFAULT false,
    CONSTRAINT bbg_positions_inbound_pkey PRIMARY KEY (id)
);

ALTER TABLE IF EXISTS app_data.bbg_positions_inbound OWNER to postgres;




CREATE TABLE IF NOT EXISTS app_data.bbg_positions
(
    account_id character varying(50) COLLATE pg_catalog."default" NOT NULL,
    as_of_date date NOT NULL,
    id_bb_unique character varying(50) COLLATE pg_catalog."default",
    cusip character varying(50) COLLATE pg_catalog."default",
    figi character varying(50) COLLATE pg_catalog."default" NOT NULL,
    currency character varying(20) COLLATE pg_catalog."default",
    deal_ticket_num integer,
    load_bb_entity_name character varying(100) COLLATE pg_catalog."default",
    market_value_current numeric(30,9),
    market_value_current_pct numeric(30,9),
    market_value_initial_paint numeric(30,9),
    market_value_initial_paint_pct numeric(30,9),
    market_value_raw numeric(30,9),
    quantity_current numeric(30,9),
    quantity_current_initial_paint numeric(30,9),
    quantity_proposed numeric(30,9),
    quantity_proposed_initial_paint numeric(30,9),
    exposure_amount numeric(30,9),
    exposure_pct numeric(30,9),
    nav_poller_calculated numeric(30,9),
    bb_security_identifier_desc character varying(250) COLLATE pg_catalog."default",
    create_user character varying(100) COLLATE pg_catalog."default" DEFAULT CURRENT_USER,
    enforce_uniqueness boolean DEFAULT false,
    fut_flag boolean,
    fx_flag boolean,
    fx_rate numeric(30,9),
    inbound_row_id bigint NOT NULL DEFAULT nextval('app_data.bbg_positions_inbound_row_id_seq'::regclass),
    isin character varying(50) COLLATE pg_catalog."default",
    issue_country character varying(20) COLLATE pg_catalog."default",
    last_update timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    last_update_by character varying(100) COLLATE pg_catalog."default" DEFAULT CURRENT_USER,
    load_bb_action character varying(10) COLLATE pg_catalog."default",
    load_bb_entity_type character varying(100) COLLATE pg_catalog."default" NOT NULL,
    load_bb_uuid integer,
    load_date timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    load_order integer NOT NULL DEFAULT nextval('app_data.bbg_positions_load_order_seq'::regclass),
    load_process character varying(100) COLLATE pg_catalog."default",
    load_status character varying(10) COLLATE pg_catalog."default" DEFAULT 'New'::character varying,
    loader_derived_asset_class text COLLATE pg_catalog."default",
    loader_derived_instrument_type text COLLATE pg_catalog."default",
    loader_derived_yellow_key text COLLATE pg_catalog."default",
    msg_request_id character varying(100) COLLATE pg_catalog."default" NOT NULL,
    sedol character varying(50) COLLATE pg_catalog."default",
    short_flag boolean DEFAULT false,
    swap_flag boolean,
    ticker character varying(20) COLLATE pg_catalog."default",
    CONSTRAINT bbg_positions_pkey1 PRIMARY KEY (account_id, as_of_date, load_bb_entity_type, msg_request_id, load_order)
);

ALTER TABLE IF EXISTS app_data.bbg_positions OWNER to postgres;

CREATE UNIQUE INDEX IF NOT EXISTS bbg_positions_upsert_key
    ON app_data.bbg_positions USING btree
    (account_id COLLATE pg_catalog."default" ASC NULLS LAST, as_of_date ASC NULLS LAST, COALESCE(figi, ''::character varying) COLLATE pg_catalog."default" ASC NULLS LAST, COALESCE(cusip, ''::character varying) COLLATE pg_catalog."default" ASC NULLS LAST, load_bb_entity_type COLLATE pg_catalog."default" ASC NULLS LAST, msg_request_id COLLATE pg_catalog."default" ASC NULLS LAST)
    WITH (fillfactor=100, deduplicate_items=True)
    TABLESPACE pg_default
    WHERE enforce_uniqueness IS TRUE;

CREATE UNIQUE INDEX IF NOT EXISTS bbg_positions_upsert_fx_key
    ON app_data.bbg_positions USING btree
    (account_id COLLATE pg_catalog."default" ASC NULLS LAST, as_of_date ASC NULLS LAST, deal_ticket_num ASC NULLS LAST, currency COLLATE pg_catalog."default" ASC NULLS LAST, load_bb_entity_type COLLATE pg_catalog."default" ASC NULLS LAST, msg_request_id COLLATE pg_catalog."default" ASC NULLS LAST)
    WITH (fillfactor=100, deduplicate_items=True)
    TABLESPACE pg_default;




