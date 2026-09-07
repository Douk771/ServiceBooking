namespace ServiceBooking.Core.Enums;

/// <summary>How long a company's client-note photos are kept before the background cleanup task removes
/// them (US-24, US-21). Exactly the three values the customer named (SPEC Q7) — no arbitrary day count,
/// which would immediately need its own UI validation and a "what does 0 mean" answer.</summary>
public enum PhotoRetention { SixMonths = 0, TwelveMonths = 1, Forever = 2 }
