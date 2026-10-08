using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Stays;

public enum HousePublishProblem { HouseArchived, NoPrice, ObjectKindRequired, RegistryNumberRequired, AttestationRequired }

/// <summary>
/// ARCHITECTURE_CYCLE37.md §37.6.6 + customer decision on ЮР-2 (08.10.2026): a house WITHOUT a registry number is PUBLISHED, of ANY kind,
/// under the owner's attestation (the responsibility is the owner's). So <see cref="HousePublishProblem.RegistryNumberRequired"/> is never produced
/// by this version of the rules (the code stays in the contract enum for compatibility). A published house needs: not archived, a price,
/// an object kind, and an accepted attestation.
/// </summary>
public static class HousePublishRules
{
    /// <summary>Problems that depend on the house alone (shown as <c>publishProblems</c>); the attestation is per request.</summary>
    public static IReadOnlyList<HousePublishProblem> HouseProblems(bool isArchived, bool hasPrice, HouseObjectKind? objectKind)
    {
        var list = new List<HousePublishProblem>();
        if (isArchived) list.Add(HousePublishProblem.HouseArchived);
        if (!hasPrice) list.Add(HousePublishProblem.NoPrice);
        if (objectKind is null) list.Add(HousePublishProblem.ObjectKindRequired);
        return list;
    }

    public static HousePublishProblem? Check(bool isArchived, bool hasPrice, HouseObjectKind? objectKind, bool attestationAccepted)
    {
        var first = HouseProblems(isArchived, hasPrice, objectKind);
        if (first.Count > 0) return first[0];
        return attestationAccepted ? null : HousePublishProblem.AttestationRequired;
    }

    public static string Text(HousePublishProblem p) => p switch
    {
        HousePublishProblem.HouseArchived => "Дом в архиве",
        HousePublishProblem.NoPrice => "Задайте цену: постоянную или хотя бы один период на будущие даты",
        HousePublishProblem.ObjectKindRequired => "Укажите вид объекта",
        HousePublishProblem.RegistryNumberRequired => "Для гостевого дома и средства размещения укажите номер в реестре",
        _ => "Подтвердите сведения о доме"
    };
}
