using Forge.Api.Features.Mobile;

namespace Forge.Api.Features.ShopFloor;

public static class ShopFloorNextStage
{
    public static int Require(JobStatusResponseModel status)
    {
        if (status.NextStageId is not { } nextStageId)
            throw new InvalidOperationException("This work order is already at its final status.");

        if (!status.NextStageIsShopFloor)
            throw new InvalidOperationException(
                $"The next status, {status.NextStageName}, is an office status. Move it from the board.");

        return nextStageId;
    }
}
