using System.Security.Cryptography;

namespace ManInBlack.Dashboard.Auth;

/// <summary>Dashboard 配置节(对应 settings.json 的 Dashboard:*)。</summary>
public sealed class DashboardOptions
{
    public string? Password { get; set; }
}

/// <summary>密码校验与启动期 fail-closed 检查(纯静态,便于测试)。</summary>
public static class AuthService
{
    /// <summary>固定时长比对,防计时侧信道。长度不同直接返回 false。</summary>
    public static bool VerifyPassword(string? stored, string? supplied)
    {
        if (string.IsNullOrEmpty(stored) || string.IsNullOrEmpty(supplied)) return false;
        var a = System.Text.Encoding.UTF8.GetBytes(stored);
        var b = System.Text.Encoding.UTF8.GetBytes(supplied);
        if (a.Length != b.Length) return false;
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>Fail-closed:未配置密码直接抛异常,拒绝启动。</summary>
    public static void EnsureConfigured(DashboardOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Password))
            throw new InvalidOperationException("settings.json 缺少 Dashboard:Password,Dashboard 拒绝启动(fail-closed)。");
    }
}
