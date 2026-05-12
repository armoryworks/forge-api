using System.Linq.Expressions;

using Bogus;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Moq;
using Forge.Api.Features.Auth;
using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Tests.Handlers.Auth;

public class KioskLoginHandlerTests
{
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly Mock<ITokenService> _tokenServiceMock;
    private readonly Mock<ISessionStore> _sessionStoreMock;
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
    private readonly Mock<IRoleClaimsExpander> _roleClaimsExpanderMock;
    private readonly KioskLoginHandler _handler;
    private readonly Faker _faker = new();

    public KioskLoginHandlerTests()
    {
        _userManagerMock = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);

        _tokenServiceMock = new Mock<ITokenService>();
        _sessionStoreMock = new Mock<ISessionStore>();
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        _roleClaimsExpanderMock = new Mock<IRoleClaimsExpander>();
        // WU-06 regression: passthrough default returns empty role list. Tests
        // that exercise role expansion override this per-test.
        _roleClaimsExpanderMock
            .Setup(x => x.GetEffectiveRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        _handler = new KioskLoginHandler(
            _userManagerMock.Object, _tokenServiceMock.Object,
            _sessionStoreMock.Object, _httpContextAccessorMock.Object,
            _roleClaimsExpanderMock.Object);
    }

    [Fact]
    public async Task Handle_ValidBarcodeAndPin_ReturnsToken()
    {
        var pinHash = SetPinHandler.HashPin("5678");
        var user = new ApplicationUser
        {
            Id = 1,
            Email = _faker.Internet.Email(),
            FirstName = _faker.Name.FirstName(),
            LastName = _faker.Name.LastName(),
            EmployeeBarcode = "EMP-001",
            PinHash = pinHash,
            IsActive = true,
        };

        var users = new List<ApplicationUser> { user };
        _userManagerMock.Setup(x => x.Users)
            .Returns(new TestAsyncEnumerableQueryable<ApplicationUser>(users));

        _userManagerMock.Setup(x => x.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { "ProductionWorker" });
        _roleClaimsExpanderMock
            .Setup(x => x.GetEffectiveRolesAsync(user, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "ProductionWorker" });

        var tokenResult = new TokenResult("test-jwt-token", "test-jti", DateTimeOffset.UtcNow.AddHours(8));
        _tokenServiceMock.Setup(x => x.GenerateToken(
                user.Id, user.Email!, user.FirstName, user.LastName,
                user.Initials, user.AvatarColor,
                It.IsAny<IList<string>>(), TimeSpan.FromHours(8), null))
            .Returns(tokenResult);

        var command = new KioskLoginCommand("EMP-001", "5678");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Should().NotBeNull();
        result.Token.Should().Be("test-jwt-token");
        result.User.Id.Should().Be(user.Id);
        result.User.Email.Should().Be(user.Email);
        result.User.Roles.Should().Contain("ProductionWorker");
        result.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(8), TimeSpan.FromMinutes(1));

        _sessionStoreMock.Verify(x => x.CreateSessionAsync(
            user.Id, "test-jti", It.IsAny<DateTimeOffset>(),
            "kiosk", It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_InvalidBarcode_ThrowsInvalidOperationException()
    {
        var users = new List<ApplicationUser>();
        _userManagerMock.Setup(x => x.Users)
            .Returns(new TestAsyncEnumerableQueryable<ApplicationUser>(users));

        var command = new KioskLoginCommand("INVALID-BARCODE", "1234");

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Invalid barcode or PIN");
    }

    [Fact]
    public async Task Handle_InvalidPin_ThrowsInvalidOperationException()
    {
        var pinHash = SetPinHandler.HashPin("5678");
        var user = new ApplicationUser
        {
            Id = 2,
            Email = _faker.Internet.Email(),
            FirstName = _faker.Name.FirstName(),
            LastName = _faker.Name.LastName(),
            EmployeeBarcode = "EMP-002",
            PinHash = pinHash,
            IsActive = true,
        };

        var users = new List<ApplicationUser> { user };
        _userManagerMock.Setup(x => x.Users)
            .Returns(new TestAsyncEnumerableQueryable<ApplicationUser>(users));

        var command = new KioskLoginCommand("EMP-002", "0000");

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Invalid barcode or PIN");
    }
}

/// <summary>
/// In-memory IQueryable wrapper that supports EF Core async operations
/// (FirstOrDefaultAsync, ToListAsync, etc.) used by UserManager.Users queries.
/// </summary>
internal class TestAsyncEnumerableQueryable<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
{
    private readonly IQueryProvider _provider;

    public TestAsyncEnumerableQueryable(IEnumerable<T> enumerable) : base(enumerable)
    {
        // Access base EnumerableQuery's provider before our override takes effect
        _provider = new TestAsyncQueryProvider<T>(
            ((IQueryable)new EnumerableQuery<T>(enumerable)).Provider);
    }

    public TestAsyncEnumerableQueryable(Expression expression, IQueryProvider baseProvider) : base(expression)
    {
        _provider = new TestAsyncQueryProvider<T>(baseProvider);
    }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        return new TestAsyncEnumerator<T>(this.AsEnumerable().GetEnumerator());
    }

    IQueryProvider IQueryable.Provider => _provider;
}

internal class TestAsyncEnumerator<T> : IAsyncEnumerator<T>
{
    private readonly IEnumerator<T> _inner;

    public TestAsyncEnumerator(IEnumerator<T> inner) => _inner = inner;

    public T Current => _inner.Current;

    public ValueTask<bool> MoveNextAsync() => new(_inner.MoveNext());

    public ValueTask DisposeAsync()
    {
        _inner.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal class TestAsyncQueryProvider<T> : IAsyncQueryProvider, IQueryProvider
{
    private readonly IQueryProvider _inner;

    public TestAsyncQueryProvider(IQueryProvider inner)
    {
        _inner = inner;
    }

    public IQueryable CreateQuery(Expression expression)
    {
        return new TestAsyncEnumerableQueryable<T>(expression, _inner);
    }

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
    {
        return new TestAsyncEnumerableQueryable<TElement>(expression, _inner);
    }

    public object? Execute(Expression expression)
    {
        return _inner.Execute(expression);
    }

    public TResult Execute<TResult>(Expression expression)
    {
        return _inner.Execute<TResult>(expression);
    }

    public TResult ExecuteAsync<TResult>(Expression expression,
        CancellationToken cancellationToken = default)
    {
        var resultType = typeof(TResult).GetGenericArguments().FirstOrDefault();

        if (resultType == null)
            return Execute<TResult>(expression);

        var executeMethod = typeof(IQueryProvider)
            .GetMethods()
            .First(m => m.Name == nameof(IQueryProvider.Execute) && m.IsGenericMethod)
            .MakeGenericMethod(resultType);

        var result = executeMethod.Invoke(_inner, new object[] { expression });

        return (TResult)typeof(Task).GetMethod(nameof(Task.FromResult))!
            .MakeGenericMethod(resultType)
            .Invoke(null, new[] { result })!;
    }
}
