using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Quality.Coa;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// CAP-QC-COA — certificates of analysis. Quality (Admin, Manager, Engineer) generates and voids them from a
/// lot; shipping (Admin, Manager, OfficeManager) attaches them to a shipment. Both may list and download.
/// </summary>
[ApiController]
[Route("api/v1/certificates-of-analysis")]
[Authorize(Roles = ReadRoles)]
[RequiresCapability("CAP-QC-COA")]
public class CertificatesOfAnalysisController(IMediator mediator) : ControllerBase
{
    private const string ReadRoles = "Admin,Manager,Engineer,OfficeManager";
    private const string QualityRoles = "Admin,Manager,Engineer";
    private const string ShipmentRoles = "Admin,Manager,OfficeManager";

    [HttpGet]
    public async Task<ActionResult<List<CertificateOfAnalysisResponseModel>>> List(
        [FromQuery] int? lotId, [FromQuery] int? shipmentId, [FromQuery] int? customerId)
    {
        var result = await mediator.Send(new GetCertificatesOfAnalysisQuery(lotId, shipmentId, customerId));
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CertificateOfAnalysisResponseModel>> Get(int id)
    {
        var result = await mediator.Send(new GetCertificateOfAnalysisQuery(id));
        return Ok(result);
    }

    [HttpGet("{id:int}/pdf")]
    public async Task<IActionResult> GetPdf(int id, [FromQuery] string? language)
    {
        var pdf = await mediator.Send(new GetCertificateOfAnalysisPdfQuery(id, language));
        return File(pdf, "application/pdf", $"certificate-of-analysis-{id}.pdf");
    }

    [HttpPost]
    [Authorize(Roles = QualityRoles)]
    public async Task<ActionResult<CertificateOfAnalysisResponseModel>> Generate(
        [FromBody] GenerateCertificateOfAnalysisRequestModel request)
    {
        var result = await mediator.Send(new GenerateCertificateOfAnalysisCommand(request));
        return Created($"/api/v1/certificates-of-analysis/{result.Id}", result);
    }

    [HttpPost("{id:int}/void")]
    [Authorize(Roles = QualityRoles)]
    public async Task<ActionResult<CertificateOfAnalysisResponseModel>> Void(int id)
    {
        var result = await mediator.Send(new VoidCertificateOfAnalysisCommand(id));
        return Ok(result);
    }

    [HttpPost("~/api/v1/shipments/{shipmentId:int}/certificates-of-analysis")]
    [Authorize(Roles = ShipmentRoles)]
    public async Task<ActionResult<ShipmentCertificatesResponseModel>> AttachToShipment(
        int shipmentId, [FromBody] AttachShipmentCertificatesRequestModel request)
    {
        var result = await mediator.Send(new AttachShipmentCertificatesCommand(shipmentId, request));
        return Ok(result);
    }
}
