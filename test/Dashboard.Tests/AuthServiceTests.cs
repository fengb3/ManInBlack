using ManInBlack.Dashboard.Auth;
using Xunit;

namespace Dashboard.Tests;

public class AuthServiceTests
{
    [Fact]
    public void VerifyPassword_Correct_ReturnsTrue() =>
        Assert.True(AuthService.VerifyPassword("s3cret", "s3cret"));

    [Fact]
    public void VerifyPassword_Wrong_ReturnsFalse() =>
        Assert.False(AuthService.VerifyPassword("s3cret", "wrong"));

    [Fact]
    public void VerifyPassword_Empty_ReturnsFalse() =>
        Assert.False(AuthService.VerifyPassword("", "x"));

    [Fact]
    public void EnsureConfigured_Empty_Throws() =>
        Assert.Throws<InvalidOperationException>(() => AuthService.EnsureConfigured(new DashboardOptions()));

    [Fact]
    public void EnsureConfigured_Set_DoesNotThrow() =>
        AuthService.EnsureConfigured(new DashboardOptions { Password = "x" });
}
