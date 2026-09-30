namespace ServiceBooking.API.DTOs.Demo;

/// <summary>API_CONTRACT_CYCLE28.md §597 — one button of the login page: the wire name of the role and its caption.</summary>
public sealed record DemoRoleDto(string Role, string Label);

/// <summary>API_CONTRACT_CYCLE28.md §597 — <c>GET /api/demo/status</c>. <c>DemoMode</c> is always true (outside demo mode the route answers 404);
/// <c>Resetting</c> is true while the nightly or manual reset is running; <c>LastResetAtUtc</c> is null until the first reset.</summary>
public sealed record DemoStatusDto(
    bool DemoMode,
    bool Resetting,
    string ResetLocalTime,
    string TimeZoneId,
    DateTime? LastResetAtUtc,
    IReadOnlyList<DemoRoleDto> Roles);

/// <summary>API_CONTRACT_CYCLE28.md §598 — the body of <c>POST /api/demo/login</c>. <c>Role</c> is a plain string on purpose: an unknown value is a 400 with a
/// Russian sentence, not a model-binding error.</summary>
public sealed record DemoLoginRequest(string? Role);
