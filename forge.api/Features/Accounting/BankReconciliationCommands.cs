using MediatR;

using Forge.Api.Capabilities;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;

namespace Forge.Api.Features.Accounting;

/// <summary>Phase-3 — start a bank reconciliation (Draft) for a cash account + statement. CAP-ACCT-FULLGL gated.</summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record StartBankReconciliationCommand(int BookId, int CashGlAccountId, DateOnly StatementDate, decimal StatementEndingBalance)
    : IRequest<BankReconciliationWorksheet>;

public class StartBankReconciliationHandler(IBankReconciliationService service)
    : IRequestHandler<StartBankReconciliationCommand, BankReconciliationWorksheet>
{
    public Task<BankReconciliationWorksheet> Handle(StartBankReconciliationCommand request, CancellationToken ct)
        => service.StartAsync(request.BookId, request.CashGlAccountId, request.StatementDate, request.StatementEndingBalance, ct);
}

/// <summary>Phase-3 — fetch a reconciliation worksheet. CAP-ACCT-FULLGL gated.</summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record GetBankReconciliationQuery(int ReconciliationId) : IRequest<BankReconciliationWorksheet>;

public class GetBankReconciliationHandler(IBankReconciliationService service)
    : IRequestHandler<GetBankReconciliationQuery, BankReconciliationWorksheet>
{
    public Task<BankReconciliationWorksheet> Handle(GetBankReconciliationQuery request, CancellationToken ct)
        => service.GetWorksheetAsync(request.ReconciliationId, ct);
}

/// <summary>Phase-3 — toggle a cash line's cleared flag on a Draft reconciliation. CAP-ACCT-FULLGL gated.</summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record SetBankReconciliationItemClearedCommand(int ReconciliationId, long JournalLineId, bool IsCleared)
    : IRequest<BankReconciliationWorksheet>;

public class SetBankReconciliationItemClearedHandler(IBankReconciliationService service)
    : IRequestHandler<SetBankReconciliationItemClearedCommand, BankReconciliationWorksheet>
{
    public Task<BankReconciliationWorksheet> Handle(SetBankReconciliationItemClearedCommand request, CancellationToken ct)
        => service.SetClearedAsync(request.ReconciliationId, request.JournalLineId, request.IsCleared, ct);
}

/// <summary>Phase-3 — finalize a reconciliation (requires it to be in balance). CAP-ACCT-FULLGL gated.</summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record FinalizeBankReconciliationCommand(int ReconciliationId) : IRequest<BankReconciliationWorksheet>;

public class FinalizeBankReconciliationHandler(IBankReconciliationService service)
    : IRequestHandler<FinalizeBankReconciliationCommand, BankReconciliationWorksheet>
{
    public Task<BankReconciliationWorksheet> Handle(FinalizeBankReconciliationCommand request, CancellationToken ct)
        => service.FinalizeAsync(request.ReconciliationId, ct);
}
