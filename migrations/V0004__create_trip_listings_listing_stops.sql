-- =============================================================================
-- V0004__create_trip_listings_listing_stops.sql
-- One stop along the way of a trip listing, with its arrival time.
-- =============================================================================
-- Implements: data-model/trip-listings/listing-stop.md v0.1.0
-- Change tracking: EXCLUDED (references/audit.md, "Excluding a table from the audit").
--   Reason: Copy of Fleet and Seat Inventory events, written only by the Search workers; the producing services keep the change history.
-- Identifier: natural key (trip_id, sequence) per the entity's Identity.
-- "Arrival between the trip's departure and arrival" compares against the parent row, so the
--   worker that creates the stops enforces it.

BEGIN;

CREATE TABLE trip_listings.listing_stops (
    trip_id          uuid        NOT NULL,
    sequence         int         NOT NULL,
    city_id          uuid        NOT NULL,
    city_name        text        NOT NULL,
    arrival_at       timestamptz NOT NULL,

    -- No audit columns: this table is declared excluded below. fn_exclude_from_audit
    -- refuses a table that carries any of them - audited or excluded, never half.
    CONSTRAINT pk_listing_stops PRIMARY KEY (trip_id, sequence),
    -- Relationship: "Belongs to one TripListing."
    CONSTRAINT fk_listing_stops_trip_id FOREIGN KEY (trip_id) REFERENCES trip_listings.trip_listings (trip_id)
);

-- Declare the exclusion, with its reason, so the CI audit gate and the checkers PASS for
-- this table and the database itself says why it has no history. Idempotent.
SELECT audit.fn_exclude_from_audit('trip_listings.listing_stops', 'Copy of Fleet and Seat Inventory events, written only by the Search workers; the producing services keep the change history.');

-- Traceability: the in-database pointer back to the logical model (checked by CI).
COMMENT ON TABLE trip_listings.listing_stops IS 'data-model/trip-listings/listing-stop.md';

COMMIT;
