namespace ServiceBooking.Core.Enums;

/// <summary>How long a company's client-note photos are kept before the background cleanup task removes
/// them (US-24, US-21).
///
/// CYCLE5-BREAKING (ARCHITECTURE_CYCLE5.md §44.7, §59.1, Q-L6): `Forever = 2` is REMOVED — "kept forever"
/// cannot be a retention period for personal data (ч. 7 ст. 5 152-ФЗ requires a defined storage term).
/// Migration M6 (`RemovePhotoRetentionForever`) rewrites every existing `Forever` row to `TwelveMonths`
/// before the column type is otherwise unchanged; `0`/`1` keep their values, so this is a value REMOVAL,
/// not a renumbering — no other member's persisted meaning changes.</summary>
public enum PhotoRetention { SixMonths = 0, TwelveMonths = 1 }
