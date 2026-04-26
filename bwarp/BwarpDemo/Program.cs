using Bwarp;
using Bwarp.Execution;

Console.WriteLine("=== Bwarp Sandbox Demo ===\n");

// 1. Basic sandbox with full isolation
Console.WriteLine("--- Test 1: Fully isolated sandbox ---");
try
{
    var result = await Sandbox.Run("/bin/sh", "-c", "echo 'Hello from sandbox!'; id; hostname; ls /tmp")
        .UnshareAll()
        .ClearEnvironment()
        .SetEnv("PATH", "/usr/bin:/bin")
        .SetEnv("HOME", "/tmp")
        .MountProc()
        .MountDev()
        .MountTmpfs("/tmp")
        .BindReadOnly("/usr", "/usr")
        .TryBindReadOnly("/lib", "/lib")
        .TryBindReadOnly("/lib64", "/lib64")
        .BindReadOnly("/bin", "/bin")
        .WithHostname("sandbox-host")
        .DieWithParent()
        .NewSession()
        .ExecuteAsync();

    Console.WriteLine($"Exit code: {result.ExitCode}");
    Console.WriteLine($"Stdout:\n{result.StandardOutput}");
    if (!string.IsNullOrEmpty(result.StandardError))
        Console.WriteLine($"Stderr:\n{result.StandardError}");
    Console.WriteLine($"Runtime: {result.RunTime.TotalMilliseconds:F0}ms\n");
}
catch (SandboxProcessException ex)
{
    Console.WriteLine($"Error: {ex.Message}\n");
}

// 2. Using Minimal preset
Console.WriteLine("--- Test 2: Minimal preset ---");
try
{
    var result = await SandboxPresets.Minimal("/bin/sh", "-c", "echo 'Minimal sandbox'; cat /etc/hostname 2>/dev/null || echo 'no hostname file'")
        .SetEnv("HOME", "/tmp")
        .ExecuteAsync();

    Console.WriteLine($"Exit code: {result.ExitCode}");
    Console.WriteLine($"Stdout:\n{result.StandardOutput}\n");
}
catch (SandboxProcessException ex)
{
    Console.WriteLine($"Error: {ex.Message}\n");
}

// 3. Network isolation test
Console.WriteLine("--- Test 3: Network isolated (no network) ---");
try
{
    var result = await Sandbox.Run("/bin/sh", "-c", "ip link show 2>&1; ping -c 1 -W 1 127.0.0.1 2>&1 || true")
        .Unshare(Namespaces.User | Namespaces.Pid | Namespaces.Network | Namespaces.Uts)
        .MountProc()
        .MountDev()
        .BindReadOnly("/usr", "/usr")
        .TryBindReadOnly("/lib", "/lib")
        .TryBindReadOnly("/lib64", "/lib64")
        .BindReadOnly("/bin", "/bin")
        .DieWithParent()
        .NewSession()
        .ExecuteAsync();

    Console.WriteLine($"Exit code: {result.ExitCode}");
    Console.WriteLine($"Stdout:\n{result.StandardOutput}\n");
}
catch (SandboxProcessException ex)
{
    Console.WriteLine($"Error: {ex.Message}\n");
}

// 4. Cancellation / timeout demo
Console.WriteLine("--- Test 4: Timeout cancellation ---");
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
try
{
    var result = await Sandbox.Run("/bin/sleep", "60")
        .UnshareAll()
        .MountProc()
        .BindReadOnly("/usr", "/usr")
        .DieWithParent()
        .ExecuteAsync(cts.Token);

    Console.WriteLine($"Exit code: {result.ExitCode}\n");
}
catch (OperationCanceledException)
{
    Console.WriteLine("Sandbox timed out after 3 seconds (expected)\n");
}

// 5. Real-time event stream
Console.WriteLine("--- Test 5: Real-time event stream ---");
try
{
    await foreach (var evt in Sandbox.Run("/bin/sh", "-c", "echo line1; echo line2; echo line3 >&2")
        .Unshare(Namespaces.User | Namespaces.Pid)
        .MountProc()
        .BindReadOnly("/usr", "/usr")
        .TryBindReadOnly("/lib", "/lib")
        .TryBindReadOnly("/lib64", "/lib64")
        .BindReadOnly("/bin", "/bin")
        .DieWithParent()
        .NewSession()
        .ListenAsync())
    {
        switch (evt)
        {
            case SandboxEvent.Started s:
                Console.WriteLine($"  [PID: {s.ProcessId}]");
                break;
            case SandboxEvent.StandardOutputReceived o:
                Console.WriteLine($"  [OUT] {o.Text}");
                break;
            case SandboxEvent.StandardErrorReceived e:
                Console.WriteLine($"  [ERR] {e.Text}");
                break;
            case SandboxEvent.Exited e:
                Console.WriteLine($"  [Exit: {e.ExitCode}]");
                break;
        }
    }
}
catch (SandboxProcessException ex)
{
    Console.WriteLine($"Error: {ex.Message}\n");
}

// 6. Confine sandbox - test language runtimes
Console.WriteLine("--- Test 6: Confine sandbox (language runtimes) ---");
try
{
    var runtimes = new[] { "python3 --version", "node --version", "go version", "gcc --version | head -1", "java -version 2>&1 | head -1" };
    foreach (var cmd in runtimes)
    {
        var result = await Sandbox.Confine("/tmp", cmd).ExecuteAsync();
        var output = string.IsNullOrEmpty(result.StandardOutput.Trim())
            ? result.StandardError.Trim()
            : result.StandardOutput.Trim();
        Console.WriteLine($"  {cmd.Split(' ')[0]}: {output} (exit={result.ExitCode})");
    }
}
catch (SandboxProcessException ex)
{
    Console.WriteLine($"Error: {ex.Message}\n");
}


// 7. Confine sandbox - test ls
Console.WriteLine("--- Test 7: Confine sandbox (let me check) ---");
try
{
    string[] commands = ["ls -la", "pwd"];
    foreach (var cmd in commands)
    {
        var result = await Sandbox.Confine("/tmp", cmd).ExecuteAsync();
        var output = string.IsNullOrEmpty(result.StandardOutput.Trim())
            ? result.StandardError.Trim()
            : result.StandardOutput.Trim();
        Console.WriteLine($"  {cmd}: {output} (exit={result.ExitCode})");
    }
}
catch (SandboxProcessException ex)
{
    Console.WriteLine($"Error: {ex.Message}\n");
}

Console.WriteLine("=== Demo complete ===");

