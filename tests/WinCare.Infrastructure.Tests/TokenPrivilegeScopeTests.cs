using WinCare.Infrastructure.Security;

namespace WinCare.Infrastructure.Tests;

public sealed class TokenPrivilegeScopeTests
{
    [Fact]
    public void Scope_accepts_known_privileges_and_disposes_cleanly()
    {
        using (var scope = new TokenPrivilegeScope(TokenPrivilegeScope.SeShutdownPrivilege))
        {
            Assert.NotNull(scope);
        }
    }

    [Fact]
    public void Scope_handles_empty_or_null_privileges_gracefully()
    {
        using (var scope = new TokenPrivilegeScope())
        {
            Assert.NotNull(scope);
        }

        using (var scopeWithNull = new TokenPrivilegeScope(null!))
        {
            Assert.NotNull(scopeWithNull);
        }
    }

    [Fact]
    public void Scope_handles_invalid_privilege_names_without_throwing()
    {
        using (var scope = new TokenPrivilegeScope("NonExistentPrivilege_XYZ_123"))
        {
            Assert.NotNull(scope);
        }
    }

    [Fact]
    public void Scope_allows_multiple_disposals_safely()
    {
        var scope = new TokenPrivilegeScope(TokenPrivilegeScope.SeShutdownPrivilege);
        scope.Dispose();
        scope.Dispose();
    }
}