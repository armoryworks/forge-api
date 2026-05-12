using FluentAssertions;

using Microsoft.Extensions.Caching.Memory;

using Moq;

using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Tests.Services;

public class ClockEventTypeServiceTests
{
    private readonly Mock<IReferenceDataRepository> _referenceDataRepo = new();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly ClockEventTypeService _service;

    public ClockEventTypeServiceTests()
    {
        _service = new ClockEventTypeService(_referenceDataRepo.Object, _cache);
    }

    private static List<ReferenceDataResponseModel> BuildTestReferenceData()
    {
        return
        [
            new(1, "clock_in", "Clock In", 1, true, true, null, null,
                """{"statusMapping":"In","oppositeCode":"clock_out","category":"work","countsAsActive":true,"isMismatchable":true,"icon":"login","color":"#22c55e"}"""),
            new(2, "clock_out", "Clock Out", 2, true, true, null, null,
                """{"statusMapping":"Out","oppositeCode":"clock_in","category":"work","countsAsActive":false,"isMismatchable":true,"icon":"logout","color":"#ef4444"}"""),
            new(3, "break_start", "Break Start", 3, true, true, null, null,
                """{"statusMapping":"On Break","oppositeCode":"break_end","category":"break","countsAsActive":true,"isMismatchable":false,"icon":"free_breakfast","color":"#f59e0b"}"""),
            new(4, "break_end", "Break End", 4, true, true, null, null,
                """{"statusMapping":"In","oppositeCode":"break_start","category":"break","countsAsActive":true,"isMismatchable":false,"icon":"free_breakfast","color":"#22c55e"}"""),
        ];
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllClockEventTypes()
    {
        // Arrange
        var testData = BuildTestReferenceData();
        _referenceDataRepo.Setup(r => r.GetByGroupAsync("clock_event_type", It.IsAny<CancellationToken>()))
            .ReturnsAsync(testData);

        // Act
        var result = await _service.GetAllAsync();

        // Assert
        result.Should().HaveCount(4);
        result.Select(d => d.Code).Should().Contain(new[] { "clock_in", "clock_out", "break_start", "break_end" });
    }

    [Fact]
    public async Task GetByCodeAsync_ValidCode_ReturnsDefinition()
    {
        // Arrange
        var testData = BuildTestReferenceData();
        _referenceDataRepo.Setup(r => r.GetByGroupAsync("clock_event_type", It.IsAny<CancellationToken>()))
            .ReturnsAsync(testData);

        // Act
        var result = await _service.GetByCodeAsync("clock_in");

        // Assert
        result.Should().NotBeNull();
        result!.Code.Should().Be("clock_in");
        result.Label.Should().Be("Clock In");
        result.StatusMapping.Should().Be("In");
        result.OppositeCode.Should().Be("clock_out");
        result.Category.Should().Be("work");
        result.CountsAsActive.Should().BeTrue();
        result.IsMismatchable.Should().BeTrue();
        result.Icon.Should().Be("login");
        result.Color.Should().Be("#22c55e");
    }

    [Fact]
    public async Task GetByCodeAsync_InvalidCode_ReturnsNull()
    {
        // Arrange
        var testData = BuildTestReferenceData();
        _referenceDataRepo.Setup(r => r.GetByGroupAsync("clock_event_type", It.IsAny<CancellationToken>()))
            .ReturnsAsync(testData);

        // Act
        var result = await _service.GetByCodeAsync("nonexistent_code");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetStatusMappingsAsync_ReturnsCorrectMappings()
    {
        // Arrange
        var testData = BuildTestReferenceData();
        _referenceDataRepo.Setup(r => r.GetByGroupAsync("clock_event_type", It.IsAny<CancellationToken>()))
            .ReturnsAsync(testData);

        // Act
        var result = await _service.GetAllAsync();

        // Assert
        var mappings = result.ToDictionary(d => d.Code, d => d.StatusMapping);
        mappings["clock_in"].Should().Be("In");
        mappings["clock_out"].Should().Be("Out");
        mappings["break_start"].Should().Be("On Break");
        mappings["break_end"].Should().Be("In");
    }

    [Fact]
    public async Task GetAllAsync_CachesResults_SecondCallDoesNotHitRepo()
    {
        // Arrange
        var testData = BuildTestReferenceData();
        _referenceDataRepo.Setup(r => r.GetByGroupAsync("clock_event_type", It.IsAny<CancellationToken>()))
            .ReturnsAsync(testData);

        // Act
        var first = await _service.GetAllAsync();
        var second = await _service.GetAllAsync();

        // Assert
        first.Should().HaveCount(4);
        second.Should().HaveCount(4);
        _referenceDataRepo.Verify(r => r.GetByGroupAsync("clock_event_type", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvalidateCache_ClearsCache_NextCallHitsRepo()
    {
        // Arrange
        var testData = BuildTestReferenceData();
        _referenceDataRepo.Setup(r => r.GetByGroupAsync("clock_event_type", It.IsAny<CancellationToken>()))
            .ReturnsAsync(testData);

        // Pre-populate cache
        await _service.GetAllAsync();

        // Act
        _service.InvalidateCache();
        await _service.GetAllAsync();

        // Assert
        _referenceDataRepo.Verify(r => r.GetByGroupAsync("clock_event_type", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetAllAsync_NullMetadata_ReturnsDefaults()
    {
        // Arrange
        var testData = new List<ReferenceDataResponseModel>
        {
            new(1, "custom_event", "Custom Event", 1, true, false, null, null, null),
        };
        _referenceDataRepo.Setup(r => r.GetByGroupAsync("clock_event_type", It.IsAny<CancellationToken>()))
            .ReturnsAsync(testData);

        // Act
        var result = await _service.GetAllAsync();

        // Assert
        result.Should().HaveCount(1);
        var def = result[0];
        def.Code.Should().Be("custom_event");
        def.StatusMapping.Should().Be("Out");
        def.OppositeCode.Should().BeNull();
        def.Category.Should().Be("work");
        def.CountsAsActive.Should().BeFalse();
        def.IsMismatchable.Should().BeFalse();
        def.Icon.Should().Be("schedule");
        def.Color.Should().Be("#94a3b8");
    }
}
