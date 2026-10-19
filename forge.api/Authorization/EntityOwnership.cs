namespace Forge.Api.Authorization;

/// <summary>
/// How a record's own user is found when the caller lacks the rule's roles: the
/// person a record belongs to keeps access to it (their pay stub, expense, chat room).
/// </summary>
public enum EntityOwnership
{
    None,
    User,
    Expense,
    Employee,
    ComplianceSubmission,
    ChatRoom,
}
