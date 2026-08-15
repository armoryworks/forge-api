using FluentValidation;
using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Admin;

public record ImportDatabaseCommand(
    Stream ZipStream,
    string? ExcludePatterns,
    bool PurgeSoftDeleted,
    bool AllowFkOrphans) : IRequest<DatabaseImportReportModel>;

public class ImportDatabaseValidator : AbstractValidator<ImportDatabaseCommand>
{
    public ImportDatabaseValidator()
    {
        RuleFor(c => c.ZipStream).NotNull();
        // Globs over schema.table — identifier characters plus the glob metacharacters only, so a
        // typo'd pattern fails loud here instead of silently matching nothing.
        RuleFor(c => c.ExcludePatterns)
            .Matches(@"^[\w.*?,\s-]*$")
            .When(c => !string.IsNullOrWhiteSpace(c.ExcludePatterns))
            .WithMessage("Exclude patterns may contain identifiers, '.', '*', '?', and commas only.");
    }
}

public class ImportDatabaseHandler(IDatabaseTransferService transfer)
    : IRequestHandler<ImportDatabaseCommand, DatabaseImportReportModel>
{
    public Task<DatabaseImportReportModel> Handle(ImportDatabaseCommand request, CancellationToken ct)
    {
        var patterns = (request.ExcludePatterns ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        return transfer.ImportZipAsync(
            request.ZipStream,
            new DatabaseImportOptionsModel(patterns, request.PurgeSoftDeleted, request.AllowFkOrphans),
            ct);
    }
}
