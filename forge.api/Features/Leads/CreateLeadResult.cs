using Forge.Core.Models;

namespace Forge.Api.Features.Leads;

/// <summary>
/// Result of <see cref="CreateLeadCommand"/>. <see cref="Created"/> is false when
/// the request's ExternalId matched an existing live lead (idempotent replay of a
/// retried intake POST) — the controller answers 200 with that lead instead of
/// 201, so relay clients can retry safely. See docs/api-key-integrations.md §1.6.
/// </summary>
public record CreateLeadResult(LeadResponseModel Lead, bool Created);
