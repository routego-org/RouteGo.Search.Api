-- =============================================================================
-- V0003__create_trip_listings_trip_listings.sql
-- Search's own copy of one trip Fleet announced.
-- =============================================================================
-- Implements: data-model/trip-listings/trip-listing.md v0.1.0
-- Change tracking: EXCLUDED (references/audit.md, "Excluding a table from the audit").
--   Reason: Copy of Fleet and Seat Inventory events, written only by the Search workers; the producing services keep the change history.
-- Identifier: natural key trip_id, Fleet's own trip identifier (uuid, as Fleet stores it).
-- Every attribute but trip_id and is_cancelled is nullable: an early TripCancelled creates the
--   listing before its TripScheduled details arrive.
-- operator_rating is numeric(2,1), the shape Fleet stores and publishes.
-- amenities is a set of enumeration values, kept as a text[] whose CHECK allows only the listed
--   values; "each at most once" is enforced by the worker that writes it.
-- Rules that compare against the clock (searchable, departed) or against other events (latest
--   fare change wins, cancelled stays cancelled) are applied by the workers and the queries.

BEGIN;

CREATE SCHEMA IF NOT EXISTS trip_listings;

CREATE TABLE trip_listings.trip_listings (
    trip_id             uuid         NOT NULL,
    operator_name       text,
    operator_rating     numeric(2,1),
    bus_id              uuid,
    bus_name            text,
    bus_type            text,
    is_ac               boolean,
    amenities           text[],
    from_city_id        uuid,
    from_city_name      text,
    to_city_id          uuid,
    to_city_name        text,
    service_date        date,
    departure_at        timestamptz,
    arrival_at          timestamptz,
    details_received_at timestamptz,
    is_cancelled        boolean      NOT NULL,
    cancelled_at        timestamptz,
    fares_changed_at    timestamptz,

    -- No audit columns: this table is declared excluded below. fn_exclude_from_audit
    -- refuses a table that carries any of them - audited or excluded, never half.
    CONSTRAINT pk_trip_listings PRIMARY KEY (trip_id),
    -- Rule: the operator's rating is 0.0 to 5.0.
    CONSTRAINT ck_trip_listings_operator_rating CHECK (operator_rating BETWEEN 0.0 AND 5.0),
    -- Rule: bus type is Seater, Sleeper or SemiSleeper.
    CONSTRAINT ck_trip_listings_bus_type CHECK (bus_type IN ('Seater', 'Sleeper', 'SemiSleeper')),
    -- Rule: amenities are drawn only from the fixed list.
    CONSTRAINT ck_trip_listings_amenities CHECK (amenities <@ ARRAY[
        'Wifi', 'ChargingPoint', 'Blanket', 'WaterBottle',
        'ReadingLight', 'Cctv', 'GpsTracking', 'Restroom']::text[]),
    -- Rule: the last city is never the same as the first city.
    CONSTRAINT ck_trip_listings_cities CHECK (to_city_id <> from_city_id),
    -- Rule: the arrival time is after the departure time.
    CONSTRAINT ck_trip_listings_arrival_at CHECK (arrival_at > departure_at)
);

-- Trip search: first city, last city and service date of listings that can be searchable.
CREATE INDEX ix_trip_listings_from_city_id_to_city_id_service_date
    ON trip_listings.trip_listings (from_city_id, to_city_id, service_date)
    WHERE NOT is_cancelled AND details_received_at IS NOT NULL;

-- City suggestions: listings still ahead of the search cutoff.
CREATE INDEX ix_trip_listings_departure_at
    ON trip_listings.trip_listings (departure_at)
    WHERE NOT is_cancelled AND details_received_at IS NOT NULL;

-- Declare the exclusion, with its reason, so the CI audit gate and the checkers PASS for
-- this table and the database itself says why it has no history. Idempotent.
SELECT audit.fn_exclude_from_audit('trip_listings.trip_listings', 'Copy of Fleet and Seat Inventory events, written only by the Search workers; the producing services keep the change history.');

-- Traceability: the in-database pointer back to the logical model (checked by CI).
COMMENT ON TABLE trip_listings.trip_listings IS 'data-model/trip-listings/trip-listing.md';

COMMIT;
