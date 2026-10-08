using Forge.Core.Enums;

namespace Forge.Core.Models;

public record StartJobOperationTimerRequestModel(TimeEntryType EntryType = TimeEntryType.Run);
