using Forge.Core.Enums.Accounting;

namespace Forge.Core.Models.Accounting;

/// <summary>A customer or vendor that a control-account journal line can name as its counterparty.</summary>
public sealed record JournalPartyOptionModel(SubledgerPartyType PartyType, int Id, string Name);
