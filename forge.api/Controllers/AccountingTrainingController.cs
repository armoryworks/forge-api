using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting.Training;

namespace Forge.Api.Controllers;

/// <summary>
/// §5A.4 GL training system HTTP surface. Same gating as the rest of the GL area
/// (Controller role + CAP-ACCT-FULLGL): learners practice with the REAL editor/ledger/reverse
/// surfaces against the isolated TRAINING book; scenarios are validated by ledger END-STATE.
/// </summary>
[ApiController]
[Route("api/v1/accounting/training")]
[Authorize(Roles = "Controller")]
[RequiresCapability("CAP-ACCT-FULLGL")]
public class AccountingTrainingController(
    ITrainingSandboxService sandbox,
    ITrainingScenarioProvider scenarios,
    ILedgerScenarioRunner runner) : ControllerBase
{
    private int ActorId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");

    /// <summary>Sandbox state; seeds it on first touch so the learner always lands on a ready book.</summary>
    [HttpGet("state")]
    public async Task<ActionResult<TrainingSandboxState>> GetState(CancellationToken ct)
        => Ok(await sandbox.EnsureSeededAsync(ActorId, ct));

    /// <summary>Wipe + reseed the sandbox (permitted by the TRAINING-book immutability carve-out).</summary>
    [HttpPost("reset")]
    public async Task<ActionResult<TrainingSandboxState>> Reset(CancellationToken ct)
        => Ok(await sandbox.ResetAsync(ActorId, ct));

    /// <summary>The shipped scenario catalog (JSON assets, decision D3).</summary>
    [HttpGet("scenarios")]
    public ActionResult<IReadOnlyList<TrainingScenario>> GetScenarios()
        => Ok(scenarios.All.OrderBy(s => s.Order).ToList());

    /// <summary>Validate one scenario against the sandbox ledger's current end-state.</summary>
    [HttpPost("scenarios/{id}/check")]
    public async Task<ActionResult<ScenarioCheckResult>> Check(string id, CancellationToken ct)
    {
        var scenario = scenarios.All.FirstOrDefault(s => s.Id == id)
            ?? throw new KeyNotFoundException($"Training scenario '{id}' not found.");
        return Ok(await runner.CheckAsync(scenario, ct));
    }
}
