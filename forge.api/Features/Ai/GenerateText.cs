using FluentValidation;
using MediatR;

using Forge.Core.Interfaces;

namespace Forge.Api.Features.Ai;

public record GenerateTextCommand(string Prompt, string? SystemPrompt = null) : IRequest<GenerateTextResponse>;
public record GenerateTextResponse(string Text);

public class GenerateTextValidator : AbstractValidator<GenerateTextCommand>
{
    public GenerateTextValidator()
    {
        RuleFor(x => x.Prompt).NotEmpty().MaximumLength(4000);
    }
}

public class GenerateTextHandler(IAiService aiService) : IRequestHandler<GenerateTextCommand, GenerateTextResponse>
{
    public async Task<GenerateTextResponse> Handle(GenerateTextCommand request, CancellationToken ct)
    {
        var result = await aiService.GenerateTextAsync(request.Prompt, ct);
        return new GenerateTextResponse(result);
    }
}
