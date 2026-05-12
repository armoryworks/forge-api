using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Features.Admin;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/admin")]
[Authorize(Roles = "Admin")]
public class LanguagesController(IMediator mediator) : ControllerBase
{
    [HttpGet("languages")]
    public async Task<ActionResult<List<SupportedLanguageResponseModel>>> GetLanguages()
    {
        var result = await mediator.Send(new GetSupportedLanguagesQuery());
        return Ok(result);
    }

    [HttpGet("translations/{languageCode}")]
    public async Task<ActionResult<List<TranslationEntryResponseModel>>> GetTranslations(string languageCode)
    {
        var result = await mediator.Send(new GetTranslationsQuery(languageCode));
        return Ok(result);
    }

    [HttpPut("translations/{languageCode}/{key}")]
    public async Task<IActionResult> UpdateTranslation(string languageCode, string key, [FromBody] UpdateTranslationRequestModel request)
    {
        await mediator.Send(new UpdateTranslationCommand(languageCode, key, request));
        return NoContent();
    }

    [HttpPost("translations/{languageCode}/import")]
    public async Task<ActionResult<object>> ImportTranslations(string languageCode, [FromBody] ImportTranslationsRequestModel request)
    {
        var count = await mediator.Send(new ImportTranslationsCommand(languageCode, request));
        return Ok(new { imported = count });
    }

    [HttpGet("translations/{languageCode}/export")]
    public async Task<ActionResult<Dictionary<string, string>>> ExportTranslations(string languageCode)
    {
        var result = await mediator.Send(new ExportTranslationsQuery(languageCode));
        return Ok(result);
    }
}
