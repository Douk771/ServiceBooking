using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.Slots;
using ServiceBooking.API.Services.Stays;

namespace ServiceBooking.API.Controllers.Baths;

/// <summary>
/// API_CONTRACT_CYCLE42.md §42.27.4 — the once-only trial of the «Бани» line. The same checks, codes and DTOs as <c>/api/stays/trial</c> (§37.27),
/// with Line = Baths: the trials of the salon, shop and «Дома» lines do not affect it.
/// </summary>
[ApiController]
[Route("api/baths/trial")]
[Authorize]
public class BathsTrialController(StaysTrialService trial, BillingAccountProvisioner accounts) : ControllerBase
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<ActionResult<StaysTrialStateDto>> Get(CancellationToken ct)
    {
        var accountId = await accounts.EnsureAccountAsync(UserId);
        return Ok(await trial.GetStateAsync(SlotVerticals.Baths, accountId, UserId, ct));
    }

    [HttpPost]
    [RequiresOwnerTerms]
    public async Task<ActionResult<StaysTrialOutcomeDto>> Activate(StaysTrialInput input, CancellationToken ct)
    {
        var accountId = await accounts.EnsureAccountAsync(UserId);
        var outcome = await trial.GrantAsync(SlotVerticals.Baths, accountId, UserId, input.TermsVersion, ct);
        return outcome.Granted ? Ok(outcome) : Conflict(outcome);
    }
}
