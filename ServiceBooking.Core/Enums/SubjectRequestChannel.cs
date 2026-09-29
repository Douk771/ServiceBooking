namespace ServiceBooking.Core.Enums;

/// <summary>
/// US-20-09 (ARCHITECTURE_CYCLE20.md §410, Т20-13) — how a <see cref="Entities.SubjectRequest"/> reached
/// the operator. <c>WebForm</c> (0) is the existing public form's implicit value, kept first and default
/// so every pre-cycle-20 row reads as what it actually was, with no migration needed. Append-only.
/// </summary>
public enum SubjectRequestChannel
{
    WebForm = 0,
    Email = 1,
    PostalMail = 2,
}
