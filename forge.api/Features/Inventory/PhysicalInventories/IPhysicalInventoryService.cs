using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Features.Inventory.PhysicalInventories;

/// <summary>
/// The full physical inventory (wall-to-wall count): a Draft names its scope, freezing snapshots the book quantity
/// of every company-owned part row in scope onto numbered tags and blocks stock movements there, counters enter
/// counts by tag (a count off the book by more than the recount tolerance asks for a recount), the variance is
/// reviewed at current cost, and posting adjusts the bins, the valuation store and (with CAP-ACCT-FULLGL) the ledger
/// in one transaction. Cancel releases the freeze without touching stock.
/// </summary>
public interface IPhysicalInventoryService
{
    /// <summary>The physical inventories, newest first, optionally of one status.</summary>
    Task<IReadOnlyList<PhysicalInventoryResponseModel>> ListAsync(PhysicalInventoryStatus? status, CancellationToken ct = default);

    /// <summary>One physical inventory with its tags. Book quantities are left out of a blind count until review.</summary>
    Task<PhysicalInventoryDetailResponseModel> GetAsync(int id, CancellationToken ct = default);

    /// <summary>One tag by its number, for a scanner or a typed tag number.</summary>
    Task<PhysicalInventoryTagResponseModel> FindTagAsync(int id, int tagNumber, CancellationToken ct = default);

    /// <summary>Creates a Draft over a location (and everything under it), or the whole site when none is named.</summary>
    Task<PhysicalInventoryDetailResponseModel> CreateAsync(CreatePhysicalInventoryRequestModel request, int userId, CancellationToken ct = default);

    /// <summary>Snapshots the book quantities onto tags and freezes the scope.</summary>
    Task<PhysicalInventoryDetailResponseModel> FreezeAsync(int id, int userId, CancellationToken ct = default);

    /// <summary>Adds a tag for stock found in scope that had no tag (book quantity zero).</summary>
    Task<PhysicalInventoryTagResponseModel> AddTagAsync(int id, AddPhysicalInventoryTagRequestModel request, int userId, CancellationToken ct = default);

    /// <summary>Records a count, or the recount of a tag flagged for one.</summary>
    Task<PhysicalInventoryTagResponseModel> EnterCountAsync(int id, int tagId, EnterPhysicalCountRequestModel request, int userId, CancellationToken ct = default);

    /// <summary>Closes counting once every tag is counted (and recounted where asked) and opens the variance review.</summary>
    Task<PhysicalInventoryDetailResponseModel> SubmitForReviewAsync(int id, CancellationToken ct = default);

    /// <summary>Sends a reviewed inventory back to counting.</summary>
    Task<PhysicalInventoryDetailResponseModel> ReopenCountingAsync(int id, CancellationToken ct = default);

    /// <summary>The variance report: book against counted quantity per tag, valued at current cost.</summary>
    Task<PhysicalInventoryVarianceResponseModel> GetVarianceAsync(int id, CancellationToken ct = default);

    /// <summary>Posts the reviewed counts and releases the freeze.</summary>
    Task<PhysicalInventoryDetailResponseModel> PostAsync(int id, int userId, CancellationToken ct = default);

    /// <summary>Cancels the inventory and releases the freeze; stock is not changed.</summary>
    Task<PhysicalInventoryDetailResponseModel> CancelAsync(int id, int userId, CancellationToken ct = default);

    /// <summary>The count sheets as a PDF, one row per tag, with the book quantity only on a non-blind count.</summary>
    Task<(byte[] Content, string FileName)> BuildCountSheetsAsync(int id, bool? blind, string? language, CancellationToken ct = default);
}
