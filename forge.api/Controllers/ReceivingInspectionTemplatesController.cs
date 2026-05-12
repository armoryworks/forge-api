using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Quality;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// Surfaces <see cref="Forge.Core.Entities.QcChecklistTemplate"/> rows
/// at <c>/api/v1/receiving-inspection-templates</c> so the
/// <c>&lt;app-entity-picker entityType="receiving-inspection-templates"&gt;</c>
/// component can resolve the FK on the Part Quality cluster.
/// </summary>
[ApiController]
[Route("api/v1/receiving-inspection-templates")]
[Authorize]
[RequiresCapability("CAP-QC-INSPECTION")]
public class ReceivingInspectionTemplatesController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Paged list contract: <c>?search=&amp;page=&amp;pageSize=</c>.
    /// Default page size 20, max 100. Searches Name + Description (case
    /// insensitive). Active templates only.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResponse<ReceivingInspectionTemplateListItemModel>>> GetTemplates(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(
            new GetReceivingInspectionTemplatesQuery(search, page, pageSize), ct);
        return Ok(result);
    }
}
