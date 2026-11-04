-- =============================================================================
-- V0005__create_trip_listings_listing_seats.sql
-- One seat of a trip listing: class, today's fare and last known seat status.
-- =============================================================================
-- Implements: data-model/trip-listings/listing-seat.md v0.1.0
-- Change tracking: EXCLUDED (references/audit.md, "Excluding a table from the audit").
--   Reason: Copy of Fleet and Seat Inventory events, written only by the Search workers; the producing services keep the change history.
-- Identifier: natural key (trip_id, seat_no) per the entity's Identity.
-- fare is money: bigint minor units (paise), as Fleet stores it. The entity allows whole rupees
--   only, so the CHECK holds the stored value to whole hundreds of paise.
-- "Latest seat event wins" and "label and class never change" compare against earlier events, so
--   the worker that applies them enforces them.

BEGIN;

CREATE TABLE trip_listings.listing_seats (
    trip_id           uuid        NOT NULL,
    seat_no           text        NOT NULL,
    seat_class        text        NOT NULL,
    fare              bigint      NOT NULL,
    -- Rule: a seat starts Available.
    status            text        NOT NULL DEFAULT 'Available',
    status_changed_at timestamptz,

    -- No audit columns: this table is declared excluded below. fn_exclude_from_audit
    -- refuses a table that carries any of them - audited or excluded, never half.
    CONSTRAINT pk_listing_seats PRIMARY KEY (trip_id, seat_no),
    -- Relationship: "Belongs to one TripListing."
    CONSTRAINT fk_listing_seats_trip_id FOREIGN KEY (trip_id) REFERENCES trip_listings.trip_listings (trip_id),
    -- Rule: fares are whole Indian rupees (stored in paise).
    CONSTRAINT ck_listing_seats_fare CHECK (fare % 100 = 0),
    -- Rule: status is Available, Held, Booked or Blocked.
    CONSTRAINT ck_listing_seats_status CHECK (status IN ('Available', 'Held', 'Booked', 'Blocked'))
);

-- Declare the exclusion, with its reason, so the CI audit gate and the checkers PASS for
-- this table and the database itself says why it has no history. Idempotent.
SELECT audit.fn_exclude_from_audit('trip_listings.listing_seats', 'Copy of Fleet and Seat Inventory events, written only by the Search workers; the producing services keep the change history.');

-- Traceability: the in-database pointer back to the logical model (checked by CI).
COMMENT ON TABLE trip_listings.listing_seats IS 'data-model/trip-listings/listing-seat.md';

COMMIT;
