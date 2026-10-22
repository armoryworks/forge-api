using FluentAssertions;
using Moq;

using Forge.Api.Features.Ai;
using Forge.Api.Features.AiAssistants;
using Forge.Api.Middleware;
using Forge.Core.Interfaces;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Ai;

public class AiChatUnavailableTests
{
    private readonly Mock<IAiService> _ai = new();
    private readonly Mock<IEmbeddingRepository> _embeddings = new();

    public AiChatUnavailableTests()
    {
        _ai.Setup(a => a.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
    }

    [Fact]
    public async Task Help_chat_refuses_with_service_unavailable_instead_of_answering()
    {
        var handler = new AiHelpChatHandler(_ai.Object, _embeddings.Object);

        var act = () => handler.Handle(new AiHelpChatCommand("How do I ship?"), CancellationToken.None);

        await act.Should().ThrowAsync<ServiceUnavailableException>();
        _ai.Verify(a => a.GenerateTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Assistant_chat_refuses_with_service_unavailable_instead_of_answering()
    {
        using var db = TestDbContextFactory.Create();
        var handler = new AssistantChatHandler(db, _ai.Object, _embeddings.Object);

        var act = () => handler.Handle(new AssistantChatCommand(1, "What is on order?"), CancellationToken.None);

        await act.Should().ThrowAsync<ServiceUnavailableException>();
        _ai.Verify(a => a.GenerateTextAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
