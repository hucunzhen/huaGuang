using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HuaGuang.Monitor.Services.Watchdog;

/// <summary>
/// 从 Windows 服务（Session 0）在用户桌面会话中启动 UI 进程。
/// </summary>
static class WindowsInteractiveProcessLauncher
{
    const int SW_HIDE = 0;
    const uint TOKEN_ASSIGN_PRIMARY = 0x0001;
    const uint TOKEN_DUPLICATE = 0x0002;
    const uint TOKEN_QUERY = 0x0008;
    const uint TOKEN_ADJUST_DEFAULT = 0x0080;
    const uint TOKEN_ADJUST_SESSIONID = 0x0100;
    const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    const uint CREATE_NO_WINDOW = 0x08000000;
    const int STARTF_USESHOWWINDOW = 0x00000001;

    public static bool TryStart(string exePath, out string? error)
    {
        error = null;
        if (!OperatingSystem.IsWindows())
        {
            error = "非 Windows 平台";
            return false;
        }

        if (!File.Exists(exePath))
        {
            error = $"找不到程序: {exePath}";
            return false;
        }

        var workingDirectory = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;
        if (!TryStartInUserSession(exePath, workingDirectory, out error))
        {
            WatchdogDiagLog.Write($"用户会话启动失败: {error}");
            return false;
        }

        return true;
    }

    static bool TryStartInUserSession(string exePath, string workingDirectory, out string? error)
    {
        error = null;
        if (!TryGetUserSessionId(out var sessionId, out error))
        {
            return false;
        }

        if (!WTSQueryUserToken(sessionId, out var userToken))
        {
            error = $"WTSQueryUserToken(session={sessionId}) 失败: {new Win32Exception(Marshal.GetLastWin32Error()).Message}";
            return false;
        }

        try
        {
            if (!DuplicateTokenEx(
                    userToken,
                    TOKEN_ASSIGN_PRIMARY | TOKEN_DUPLICATE | TOKEN_QUERY | TOKEN_ADJUST_DEFAULT | TOKEN_ADJUST_SESSIONID,
                    IntPtr.Zero,
                    SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation,
                    TOKEN_TYPE.TokenPrimary,
                    out var primaryToken))
            {
                error = $"DuplicateTokenEx 失败: {new Win32Exception(Marshal.GetLastWin32Error()).Message}";
                return false;
            }

            try
            {
                if (!CreateEnvironmentBlock(out var environment, primaryToken, false))
                {
                    error = $"CreateEnvironmentBlock 失败: {new Win32Exception(Marshal.GetLastWin32Error()).Message}";
                    return false;
                }

                try
                {
                    var systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
                    var cmdExe = Path.Combine(systemDir, "cmd.exe");
                    var cmdLine =
                        $"\"{cmdExe}\" /c start \"\" /D \"{workingDirectory}\" \"{exePath}\"";

                    if (CreateProcessAsUserInSession(
                            primaryToken,
                            cmdExe,
                            cmdLine,
                            workingDirectory,
                            environment,
                            CREATE_UNICODE_ENVIRONMENT | CREATE_NO_WINDOW,
                            SW_HIDE,
                            out error))
                    {
                        WatchdogDiagLog.Write($"已通过 cmd start 在用户会话 {sessionId} 启动 UI");
                        return true;
                    }

                    var directLine = QuoteCommandLine(exePath);
                    if (CreateProcessAsUserInSession(
                            primaryToken,
                            exePath,
                            directLine,
                            workingDirectory,
                            environment,
                            CREATE_UNICODE_ENVIRONMENT,
                            5,
                            out var directError))
                    {
                        WatchdogDiagLog.Write($"已通过 CreateProcessAsUser 直接启动 UI (session={sessionId})");
                        return true;
                    }

                    error = string.IsNullOrWhiteSpace(error)
                        ? directError
                        : $"{error}; 直接启动失败: {directError}";
                    return false;
                }
                finally
                {
                    DestroyEnvironmentBlock(environment);
                }
            }
            finally
            {
                CloseHandle(primaryToken);
            }
        }
        finally
        {
            CloseHandle(userToken);
        }
    }

    static bool CreateProcessAsUserInSession(
        IntPtr primaryToken,
        string applicationName,
        string commandLine,
        string workingDirectory,
        IntPtr environment,
        uint creationFlags,
        int showWindow,
        out string? error)
    {
        error = null;
        var startupInfo = new STARTUPINFO
        {
            cb = Marshal.SizeOf<STARTUPINFO>(),
            lpDesktop = "winsta0\\default",
            dwFlags = STARTF_USESHOWWINDOW,
            wShowWindow = (short)showWindow
        };

        if (!CreateProcessAsUser(
                primaryToken,
                applicationName,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                creationFlags,
                environment,
                workingDirectory,
                ref startupInfo,
                out var processInfo))
        {
            error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            return false;
        }

        CloseHandle(processInfo.hThread);
        CloseHandle(processInfo.hProcess);
        return true;
    }

    static bool TryGetUserSessionId(out uint sessionId, out string? error)
    {
        error = null;
        var consoleSession = WTSGetActiveConsoleSessionId();
        if (consoleSession != 0xFFFFFFFF && consoleSession != 0)
        {
            sessionId = consoleSession;
            return true;
        }

        if (TryFindActiveSession(out sessionId))
        {
            return true;
        }

        error = "无活动用户会话（请先登录 Windows 桌面）";
        return false;
    }

    static bool TryFindActiveSession(out uint sessionId)
    {
        sessionId = 0;
        if (!WTSEnumerateSessions(IntPtr.Zero, 0, 1, out var memory, out var count))
        {
            return false;
        }

        try
        {
            var stride = Marshal.SizeOf<WTS_SESSION_INFO>();
            for (var i = 0; i < count; i++)
            {
                var entry = Marshal.PtrToStructure<WTS_SESSION_INFO>(memory + (i * stride));
                if (entry.State != WTS_CONNECTSTATE_CLASS.WTSActive || entry.SessionId == 0)
                {
                    continue;
                }

                sessionId = (uint)entry.SessionId;
                return true;
            }
        }
        finally
        {
            WTSFreeMemory(memory);
        }

        return false;
    }

    static string QuoteCommandLine(string path) =>
        path.Contains(' ') ? $"\"{path}\"" : path;

    enum SECURITY_IMPERSONATION_LEVEL
    {
        SecurityImpersonation = 2
    }

    enum TOKEN_TYPE
    {
        TokenPrimary = 1
    }

    enum WTS_CONNECTSTATE_CLASS
    {
        WTSActive,
        WTSConnected,
        WTSConnectQuery,
        WTSShadow,
        WTSDisconnected,
        WTSIdle,
        WTSListen,
        WTSReset,
        WTSDown,
        WTSInit
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WTS_SESSION_INFO
    {
        public int SessionId;
        public IntPtr pWinStationName;
        public WTS_CONNECTSTATE_CLASS State;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    static extern bool WTSEnumerateSessions(
        IntPtr hServer,
        int reserved,
        int version,
        out IntPtr sessionInfo,
        out int count);

    [DllImport("wtsapi32.dll")]
    static extern void WTSFreeMemory(IntPtr memory);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcessAsUser(
        IntPtr hToken,
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool DuplicateTokenEx(
        IntPtr hExistingToken,
        uint dwDesiredAccess,
        IntPtr lpTokenAttributes,
        SECURITY_IMPERSONATION_LEVEL impersonationLevel,
        TOKEN_TYPE tokenType,
        out IntPtr phNewToken);

    [DllImport("userenv.dll", SetLastError = true)]
    static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr hObject);
}
