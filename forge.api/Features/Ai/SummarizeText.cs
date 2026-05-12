using FluentValidation;
using MediatR;

using Forge.Core.Interfaces;

namespace Forge.Api.Features.Ai;

public record SummarizeTextCommand(string Text) : IRequest<SummarizeTextResponse>;
public record SummarizeTextResponse(string Summary);

public class SummarizeTextValidator : AbstractValidator<SummarizeTextCommand>
{
    public SummarizeTextValidator()
    {
        RuleFor(x => x.Text).NotEmpty().MaximumLength(10000);
    }
}

public class SummarizeTextHandler(IAiService aiService) : IRequestHandler<SummarizeTextCommand, SummarizeTextResponse>
{
    public async Task<SummarizeTextResponse> Handle(SummarizeTextCommand request, CancellationToken ct)
    {
        var result = await aiService.SummarizeAsync(request.Text, ct);
        return new SummarizeTextResponse(result);
    }
}
