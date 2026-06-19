namespace Forge.Core.Models;

/// <summary>
/// Normalized inbound carrier tracking webhook. A provider-specific mapping (e.g. EasyPost's tracker
/// payload) translates to this shape before reaching the ingest handler, keeping the domain provider-agnostic.
/// </summary>
public record TrackingWebhookRequestModel(string TrackingNumber, string Status);
