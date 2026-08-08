using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace NanaW;

internal static class Program
{
    internal const string AppName = "NanaW上号器";
    internal const string Website = "shop.nw-s.lat";
    internal const string Version = "v1.0.0";

    [STAThread]
    private static void Main()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var message = (e.ExceptionObject as Exception)?.Message ?? "未知错误";
            MessageBox.Show(message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        Application.ThreadException += (_, e) =>
        {
            MessageBox.Show(e.Exception.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
        };
        ApplicationConfiguration.Initialize();
        MessageBox.Show(
            $"{AppName} {Version}\n全网最小上号器，无毒无后门！\n\n官网：{Website}\n服务器：已内置",
            AppName,
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
        Application.Run(new LoginForm());
    }
}

internal sealed class LoginForm : Form
{
    private bool _isBusy;
    private readonly CancellationTokenSource _cts = new();

    private readonly TextBox _keyBox = new()
    {
        Location = new Point(24, 55),
        Size = new Size(332, 27),
        UseSystemPasswordChar = false
    };

    private readonly CheckBox _deleteHistoryCheck = new()
    {
        Location = new Point(24, 86),
        Size = new Size(332, 22),
        Text = "删除所有历史登录记录"
    };

    private readonly Button _loginButton = new()
    {
        Location = new Point(24, 112),
        Size = new Size(332, 36),
        Text = "登录 Steam"
    };

    private readonly Label _accountLabel = new()
    {
        AutoSize = true,
        Location = new Point(24, 170),
        ForeColor = SystemColors.GrayText
    };

    private readonly LinkLabel _siteLink = new()
    {
        AutoSize = true,
        Location = new Point(24, 206),
        Text = "官网：shop.nw-s.lat",
        LinkBehavior = LinkBehavior.HoverUnderline
    };

    public LoginForm()
    {
        Text = Program.AppName;
        Icon = LoadAppIcon();
        ClientSize = new Size(380, 234);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(24, 24),
            Text = "卡密"
        });
        Controls.Add(_keyBox);
        Controls.Add(_deleteHistoryCheck);
        Controls.Add(_loginButton);
        Controls.Add(_accountLabel);
        Controls.Add(_siteLink);

        AcceptButton = _loginButton;
        _loginButton.Click += LoginButton_Click;
        _siteLink.LinkClicked += (_, _) => OpenWebsite();
        FormClosing += (_, _) =>
        {
            if (_isBusy)
            {
                _cts.Cancel();
            }
        };
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            using var stream = typeof(Program).Assembly
                .GetManifestResourceStream("NanaW.icon.ico");
            if (stream != null)
            {
                return new Icon(stream);
            }
        }
        catch
        {
        }
        return Icon.ExtractAssociatedIcon(Environment.ProcessPath)!;
    }

    private static void OpenWebsite()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://" + Program.Website,
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private async void LoginButton_Click(object? sender, EventArgs e)
    {
        if (_isBusy) return;

        var licenseKey = _keyBox.Text.Trim();
        if (licenseKey.Length == 0)
        {
            MessageBox.Show(this, "请输入卡密。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _isBusy = true;
        SetBusy(true);
        var ct = _cts.Token;
        try
        {
            ct.ThrowIfCancellationRequested();
            SetStatus("正在解析卡密...");
            var account = await LicenseClient.ResolveAsync(licenseKey, ct);
            ct.ThrowIfCancellationRequested();
            _accountLabel.Text = "正在登录: " + account.User;
            var normalizedToken = NormalizeToken(account.Token);
            var token = TokenValidator.Validate(normalizedToken);

            ct.ThrowIfCancellationRequested();
            SetStatus("正在查找 Steam...");
            var paths = SteamLocator.Find();

            ct.ThrowIfCancellationRequested();
            SetStatus("正在关闭 Steam...");
            await Task.Run(() => SteamProcess.Stop(paths), ct);

            ct.ThrowIfCancellationRequested();
            SetStatus("正在写入登录信息...");
            if (_deleteHistoryCheck.Checked)
            {
                var loginUsersPath = Path.Combine(paths.ConfigPath, "loginusers.vdf");
                if (File.Exists(loginUsersPath))
                {
                    File.Delete(loginUsersPath);
                }
            }
            await Task.Run(() => SteamLogin.Apply(paths, account.User, token.SteamId, normalizedToken), ct);

            ct.ThrowIfCancellationRequested();
            SetStatus("正在启动 Steam...");
            SteamProcess.Start(paths, account.User);
            MessageBox.Show(
                this,
                $"已启动 Steam：{account.User}\n\n官网：{Program.Website}",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            _accountLabel.Text = "";
            SetStatus("已取消");
        }
        catch (Exception ex)
        {
            _accountLabel.Text = "";
            SetStatus("登录失败");
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _isBusy = false;
            SetBusy(false);
        }
    }

    private static string NormalizeToken(string token)
    {
        return token.Trim().Replace(
            "EyAidHlwIjogIkpXVCIsICJhbGciOiAiRWREU0EiIH0",
            "eyAidHlwIjogIkpXVCIsICJhbGciOiAiRWREU0EiIH0",
            StringComparison.OrdinalIgnoreCase);
    }

    private void SetBusy(bool busy)
    {
        _keyBox.Enabled = !busy;
        _loginButton.Enabled = !busy;
        if (!busy)
        {
            _loginButton.Text = "登录 Steam";
        }

        UseWaitCursor = busy;
    }

    private void SetStatus(string text)
    {
        _loginButton.Text = text;
    }
}

internal sealed record SteamAccount(string Token, string User, string SteamId);
internal sealed record SteamToken(string SteamId, DateTimeOffset ExpiresAt);
internal sealed record SteamPaths(string InstallPath, string LocalVdfPath, string ConfigPath);

internal static class LicenseClient
{
    private const string DefaultBaseUrl = "http://70.39.201.195:9099";
    private const string DefaultKeyPath = "/keygetdata?key=";
    private const string DefaultSharedSecret = "";

    private static readonly string ConfigFile = Path.Combine(
        AppContext.BaseDirectory, "server.ini");

    private static string BaseUrl => FirstNonEmpty(LoadConfig("baseUrl"), DefaultBaseUrl);
    private static string KeyPath => FirstNonEmpty(LoadConfig("keyPath"), DefaultKeyPath);
    private static string SharedSecret => FirstNonEmpty(LoadConfig("sharedSecret"), DefaultSharedSecret);
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static async Task<SteamAccount> ResolveAsync(string licenseKey, CancellationToken cancellationToken = default)
    {
        var key = licenseKey.Trim();
        var baseUrl = BaseUrl;

        var url = $"{baseUrl}{KeyPath}{Uri.EscapeDataString(key)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(SharedSecret))
        {
            request.Headers.Add("X-Lite-Sig", ComputeSignature(key));
        }
        request.Headers.UserAgent.ParseAdd("NanaW-Client/1.0");

        using var response = await HttpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            throw new InvalidOperationException("卡密无效或服务器拒绝了请求。");
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException("卡密不存在或服务器不匹配。");
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException("卡密签名校验失败，请联系客服。");
        }

        response.EnsureSuccessStatusCode();
        var raw = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (raw.Length <= 8)
        {
            throw new InvalidDataException("服务器返回的数据无效。");
        }

        using var input = new MemoryStream(raw, 8, raw.Length - 8, false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        await zlib.CopyToAsync(output, cancellationToken);

        using var document = JsonDocument.Parse(output.ToArray());
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            if (root.GetArrayLength() == 0)
            {
                throw new InvalidDataException("服务器没有返回账号。");
            }

            root = root[0];
        }

        if (!root.TryGetProperty("token", out _) ||
            !root.TryGetProperty("user", out _) ||
            !root.TryGetProperty("steamid", out _))
        {
            throw new InvalidDataException("服务器响应格式不正确。");
        }

        return new SteamAccount(
            RequiredString(root, "token"),
            RequiredString(root, "user"),
            RequiredString(root, "steamid"));
    }

    private static string RequiredString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"服务器响应缺少 {name}。");
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text)
            ? throw new InvalidDataException($"服务器响应中的 {name} 为空。")
            : text;
    }

    private static string ComputeSignature(string key)
    {
        var hmac = System.Security.Cryptography.HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(SharedSecret),
            Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hmac).ToLowerInvariant();
    }

    private static string LoadConfig(string key)
    {
        try
        {
            if (!File.Exists(ConfigFile))
            {
                return string.Empty;
            }

            foreach (var rawLine in File.ReadAllLines(ConfigFile))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
                {
                    continue;
                }

                var separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var name = line[..separator].Trim();
                var value = line[(separator + 1)..].Trim();
                if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
                {
                    return value.Trim('"', '\'');
                }
            }
        }
        catch
        {
        }

        return string.Empty;
    }

    public static bool HasServerConfig()
    {
        return !string.IsNullOrWhiteSpace(BaseUrl);
    }

    private static string FirstNonEmpty(string configured, string fallback)
    {
        return string.IsNullOrWhiteSpace(configured) ? fallback : configured.Trim();
    }
}

internal static class TokenValidator
{
    public static SteamToken Validate(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            throw new InvalidDataException("服务器返回的 Steam 令牌格式无效。");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(DecodeBase64Url(parts[1]));
        }
        catch
        {
            throw new InvalidDataException("无法解析服务器返回的 Steam 令牌。");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("iss", out var issuer) ||
                issuer.ValueKind != JsonValueKind.String ||
                issuer.GetString() != "steam" ||
                !HasClientAudience(root))
            {
                throw new InvalidDataException("服务器返回的令牌不是 Steam 客户端令牌。");
            }

            if (!root.TryGetProperty("sub", out var subject) || subject.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("Steam 令牌缺少 SteamID。");
            }

            var steamId = subject.GetString();
            if (string.IsNullOrWhiteSpace(steamId))
            {
                throw new InvalidDataException("Steam 令牌中的 SteamID 为空。");
            }

            if (!root.TryGetProperty("exp", out var expiry) ||
                expiry.ValueKind != JsonValueKind.Number ||
                !expiry.TryGetInt64(out var seconds) ||
                seconds is < -62_135_596_800 or > 253_402_300_799)
            {
                throw new InvalidDataException("Steam 令牌的有效期无效。");
            }

            var expiresAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
            if (expiresAt <= DateTimeOffset.Now)
            {
                throw new InvalidDataException("Steam 令牌已过期。");
            }

            if (root.TryGetProperty("nbf", out var notBefore) &&
                notBefore.ValueKind == JsonValueKind.Number &&
                notBefore.TryGetInt64(out var notBeforeSeconds) &&
                notBeforeSeconds is >= -62_135_596_800 and <= 253_402_300_799 &&
                DateTimeOffset.FromUnixTimeSeconds(notBeforeSeconds) > DateTimeOffset.Now)
            {
                throw new InvalidDataException("Steam 令牌尚未生效。");
            }

            return new SteamToken(steamId, expiresAt);
        }
    }

    private static bool HasClientAudience(JsonElement root)
    {
        if (!root.TryGetProperty("aud", out var audience))
        {
            return false;
        }

        if (audience.ValueKind == JsonValueKind.String)
        {
            return audience.GetString() == "client";
        }

        return audience.ValueKind == JsonValueKind.Array &&
            audience.EnumerateArray().Any(value =>
                value.ValueKind == JsonValueKind.String && value.GetString() == "client");
    }

    private static string DecodeBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        var remainder = base64.Length % 4;
        if (remainder != 0)
        {
            base64 += new string('=', 4 - remainder);
        }

        return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }
}

internal static class SteamLocator
{
    public static SteamPaths Find()
    {
        foreach (var candidate in Candidates())
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var path = candidate.Trim().Trim('"').TrimEnd('\\', '/');
            if (!File.Exists(Path.Combine(path, "steam.exe")))
            {
                continue;
            }

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                throw new DirectoryNotFoundException("无法找到 LocalAppData。 ");
            }

            return new SteamPaths(
                path,
                Path.Combine(localAppData, "Steam", "local.vdf"),
                Path.Combine(path, "config"));
        }

        throw new FileNotFoundException("未找到 Steam 安装目录或 steam.exe。");
    }

    private static IEnumerable<string?> Candidates()
    {
        yield return ReadRegistry(RegistryHive.CurrentUser, RegistryView.Default, @"Software\Valve\Steam", "SteamPath");

        var steamExe = ReadRegistry(RegistryHive.CurrentUser, RegistryView.Default, @"Software\Valve\Steam", "SteamExe");
        yield return string.IsNullOrWhiteSpace(steamExe) ? null : Path.GetDirectoryName(steamExe);

        var programFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
        yield return string.IsNullOrWhiteSpace(programFilesX86) ? null : Path.Combine(programFilesX86, "Steam");

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return string.IsNullOrWhiteSpace(programFiles) ? null : Path.Combine(programFiles, "Steam");

        yield return ReadRegistry(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
        yield return ReadRegistry(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Valve\Steam", "InstallPath");
        yield return FindFromRunningProcess();
        yield return FindFromProtocol(RegistryHive.CurrentUser);
        yield return FindFromProtocol(RegistryHive.LocalMachine);
    }

    private static string? FindFromRunningProcess()
    {
        foreach (var processName in new[] { "steam", "steamwebhelper" })
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        var fileName = process.MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(fileName))
                        {
                            return Path.GetDirectoryName(fileName);
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }

        return null;
    }

    private static string? FindFromProtocol(RegistryHive hive)
    {
        var command = ReadRegistry(
            hive,
            RegistryView.Default,
            @"Software\Classes\steam\Shell\Open\Command",
            "");
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        command = command.Trim();
        string? executable;
        if (command.StartsWith('"'))
        {
            var endQuote = command.IndexOf('"', 1);
            executable = endQuote > 1 ? command[1..endQuote] : null;
        }
        else
        {
            var end = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            executable = end >= 0 ? command[..(end + 4)] : null;
        }

        return string.IsNullOrWhiteSpace(executable) ? null : Path.GetDirectoryName(executable);
    }

    private static string? ReadRegistry(RegistryHive hive, RegistryView view, string path, string name)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(path);
            return key?.GetValue(name) as string;
        }
        catch
        {
            return null;
        }
    }
}

internal static class SteamProcess
{
    public static void Stop(SteamPaths paths)
    {
        if (!IsRunning())
        {
            return;
        }

        try
        {
            using var shutdown = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(paths.InstallPath, "steam.exe"),
                UseShellExecute = false
            }.AddArguments("-shutdown"));
            shutdown?.WaitForExit(3000);
        }
        catch
        {
        }

        for (var i = 0; i < 10; i++)
        {
            if (!IsRunning())
            {
                return;
            }

            Thread.Sleep(1000);
        }

        KillProcesses("steam");
        KillProcesses("steamwebhelper");

        for (var i = 0; i < 5; i++)
        {
            if (!IsRunning())
            {
                return;
            }

            Thread.Sleep(1000);
        }

        throw new InvalidOperationException("Steam 未能正常退出，请手动退出 Steam 后重试。");
    }

    public static void Start(SteamPaths paths, string accountName)
    {
        var steamExe = Path.Combine(paths.InstallPath, "steam.exe");
        if (!File.Exists(steamExe))
        {
            throw new InvalidOperationException($"找不到 steam.exe：\"{steamExe}\"");
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = steamExe,
                WorkingDirectory = paths.InstallPath,
                UseShellExecute = false
            }.AddArguments("-login", accountName));
            if (process is null)
            {
                throw new InvalidOperationException("Steam 启动失败。");
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"无法启动 Steam：{ex.Message}", ex);
        }
    }

    private static bool IsRunning()
    {
        return HasProcess("steam") || HasProcess("steamwebhelper");
    }

    private static bool HasProcess(string name)
    {
        var processes = Process.GetProcessesByName(name);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static void KillProcesses(string processName)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                try
                {
                    process.Kill(true);
                    process.WaitForExit(3000);
                }
                catch
                {
                }
            }
        }
    }
}

internal static class SteamLogin
{
    public static void Apply(SteamPaths paths, string accountName, string steamId, string token)
    {
        var encryptedToken = SteamCrypto.Encrypt(token, accountName);
        var accountKey = Crc32.ComputeSteamAccountKey(accountName);
        SteamConfig.Apply(paths, accountName, steamId, encryptedToken, accountKey);
    }
}

internal static class SteamCrypto
{
    private const int CryptprotectUiForbidden = 1;

    public static string Encrypt(string token, string accountName)
    {
        var data = Encoding.UTF8.GetBytes(token);
        var entropy = Encoding.UTF8.GetBytes(accountName);
        var dataBlob = CreateBlob(data);
        var entropyBlob = CreateBlob(entropy);

        try
        {
            if (!CryptProtectData(
                    ref dataBlob,
                    "BObfuscateBuffer",
                    ref entropyBlob,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptprotectUiForbidden,
                    out var protectedBlob))
            {
                throw new InvalidOperationException($"令牌加密失败，错误码：{Marshal.GetLastWin32Error()}。");
            }

            try
            {
                var output = new byte[protectedBlob.Size];
                Marshal.Copy(protectedBlob.Data, output, 0, output.Length);
                return Convert.ToHexString(output).ToLowerInvariant();
            }
            finally
            {
                LocalFree(protectedBlob.Data);
            }
        }
        finally
        {
            FreeBlob(dataBlob);
            FreeBlob(entropyBlob);
        }
    }

    private static DataBlob CreateBlob(byte[] data)
    {
        var blob = new DataBlob
        {
            Size = data.Length,
            Data = Marshal.AllocHGlobal(data.Length)
        };
        Marshal.Copy(data, 0, blob.Data, data.Length);
        return blob;
    }

    private static void FreeBlob(DataBlob blob)
    {
        if (blob.Data != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(blob.Data);
        }
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string description,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }
}

internal static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    public static string ComputeSteamAccountKey(string accountName)
    {
        var crc = 0xffffffffu;
        foreach (var value in Encoding.UTF8.GetBytes(accountName))
        {
            crc = Table[(crc ^ value) & 0xff] ^ (crc >> 8);
        }

        var hex = (crc ^ 0xffffffffu).ToString("x8").TrimStart('0');
        return $"{hex}1";
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) == 1 ? 0xedb88320u ^ (value >> 1) : value >> 1;
            }

            table[i] = value;
        }

        return table;
    }
}

internal static class SteamConfig
{
    public static void Apply(
        SteamPaths paths,
        string accountName,
        string steamId,
        string encryptedToken,
        string accountKey)
    {
        Directory.CreateDirectory(paths.ConfigPath);
        WriteConfig(Path.Combine(paths.ConfigPath, "config.vdf"), accountName, steamId);
        WriteLoginUsers(Path.Combine(paths.ConfigPath, "loginusers.vdf"), accountName, steamId);
        WriteLocal(paths.LocalVdfPath, accountKey, encryptedToken);
    }

    private static void WriteConfig(string path, string accountName, string steamId)
    {
        var root = Vdf.LoadOrEmpty(path);
        var steam = EnsurePath(root, "InstallConfigStore", "Software", "Valve", "Steam");
        SetValue(steam, "AutoUpdateWindowEnabled", "0");
        SetValue(steam, "MTBF", Random.Shared.Next(100000000, 999999999).ToString());
        EnsureObject(steam, "Accounts")[accountName] = new Dictionary<string, object>
        {
            ["SteamID"] = steamId
        };
        Vdf.Save(path, root);
    }

    private static void WriteLoginUsers(string path, string accountName, string steamId)
    {
        var root = Vdf.LoadOrEmpty(path);
        var users = EnsureObject(root, "users");
        foreach (var user in users.Values.OfType<Dictionary<string, object>>())
        {
            SetValue(user, "MostRecent", "0");
        }

        users[steamId] = new Dictionary<string, object>
        {
            ["AccountName"] = accountName,
            ["PersonaName"] = accountName,
            ["RememberPassword"] = "1",
            ["WantsOfflineMode"] = "0",
            ["SkipOfflineModeWarning"] = "0",
            ["AllowAutoLogin"] = "1",
            ["MostRecent"] = "1",
            ["Timestamp"] = DateTimeOffset.Now.ToUnixTimeSeconds().ToString()
        };
        Vdf.Save(path, root);
    }

    private static void WriteLocal(string path, string accountKey, string encryptedToken)
    {
        var root = Vdf.LoadOrEmpty(path);
        var cache = EnsurePath(root, "MachineUserConfigStore", "Software", "Valve", "Steam", "ConnectCache");
        cache[accountKey] = encryptedToken;
        Vdf.Save(path, root);
    }

    private static Dictionary<string, object> EnsurePath(Dictionary<string, object> root, params string[] keys)
    {
        var current = root;
        foreach (var key in keys)
        {
            current = EnsureObject(current, key);
        }

        return current;
    }

    private static Dictionary<string, object> EnsureObject(Dictionary<string, object> parent, string key)
    {
        var actualKey = FindKey(parent, key);
        if (parent.TryGetValue(actualKey, out var value) && value is Dictionary<string, object> existing)
        {
            return existing;
        }

        var created = new Dictionary<string, object>(StringComparer.Ordinal);
        parent[actualKey] = created;
        return created;
    }

    private static void SetValue(Dictionary<string, object> values, string key, string value)
    {
        values[FindKey(values, key)] = value;
    }

    private static string FindKey(Dictionary<string, object> values, string key)
    {
        if (values.ContainsKey(key))
        {
            return key;
        }

        foreach (var existing in values.Keys)
        {
            if (string.Equals(existing, key, StringComparison.OrdinalIgnoreCase))
            {
                return existing;
            }
        }

        return key;
    }
}

internal static class Vdf
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static Dictionary<string, object> LoadOrEmpty(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, object>(StringComparer.Ordinal);
        }

        try
        {
            return new Parser(File.ReadAllText(path, Encoding.UTF8)).Parse();
        }
        catch
        {
            return new Dictionary<string, object>(StringComparer.Ordinal);
        }
    }

    public static void Save(string path, Dictionary<string, object> document)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = path + "." + Path.GetRandomFileName() + ".tmp";
        try
        {
            var builder = new StringBuilder();
            WriteObject(builder, document, 0);
            File.WriteAllText(temporaryPath, builder.ToString(), Utf8NoBom);
            File.Move(temporaryPath, path, true);
        }
        catch
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch
            {
            }

            throw;
        }
    }

    private static void WriteObject(StringBuilder builder, Dictionary<string, object> values, int indent)
    {
        foreach (var (key, value) in values)
        {
            var prefix = new string('\t', indent);
            if (value is Dictionary<string, object> child)
            {
                builder.Append(prefix).Append('"').Append(Escape(key)).AppendLine("\"");
                builder.Append(prefix).AppendLine("{");
                WriteObject(builder, child, indent + 1);
                builder.Append(prefix).AppendLine("}");
            }
            else
            {
                builder.Append(prefix)
                    .Append('"').Append(Escape(key)).Append("\"\t\t\"")
                    .Append(Escape(value?.ToString() ?? string.Empty))
                    .AppendLine("\"");
            }
        }
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);
    }

    private sealed class Parser
    {
        private readonly List<string> _tokens;
        private int _position;

        public Parser(string text)
        {
            _tokens = Tokenize(text);
        }

        public Dictionary<string, object> Parse()
        {
            var document = ParseObject();
            if (_position != _tokens.Count)
            {
                throw new FormatException("VDF 包含多余内容。");
            }

            return document;
        }

        private Dictionary<string, object> ParseObject()
        {
            var values = new Dictionary<string, object>(StringComparer.Ordinal);
            while (_position < _tokens.Count && Peek() != "}")
            {
                var key = Read();
                if (key is "{" or "}" || _position >= _tokens.Count)
                {
                    throw new FormatException("VDF 结构无效。");
                }

                if (Peek() == "{")
                {
                    Read();
                    values[key] = ParseObject();
                    Expect("}");
                }
                else
                {
                    values[key] = Read();
                }
            }

            return values;
        }

        private string Peek() => _tokens[_position];
        private string Read() => _tokens[_position++];

        private void Expect(string expected)
        {
            if (_position >= _tokens.Count || Read() != expected)
            {
                throw new FormatException("VDF 结构无效。");
            }
        }

        private static List<string> Tokenize(string text)
        {
            var tokens = new List<string>();
            var index = 0;
            while (index < text.Length)
            {
                var current = text[index];
                if (char.IsWhiteSpace(current))
                {
                    index++;
                }
                else if (current == '/' && index + 1 < text.Length && text[index + 1] == '/')
                {
                    index += 2;
                    while (index < text.Length && text[index] is not '\r' and not '\n')
                    {
                        index++;
                    }
                }
                else if (current is '{' or '}')
                {
                    tokens.Add(current.ToString());
                    index++;
                }
                else if (current == '[')
                {
                    while (index < text.Length && text[index] != ']')
                    {
                        index++;
                    }

                    if (index < text.Length)
                    {
                        index++;
                    }
                }
                else if (current == ']')
                {
                    index++;
                }
                else if (current == '"')
                {
                    tokens.Add(ReadQuoted(text, ref index));
                }
                else
                {
                    var start = index;
                    while (index < text.Length &&
                           !char.IsWhiteSpace(text[index]) &&
                           text[index] is not '{' and not '}' and not '[' and not ']')
                    {
                        index++;
                    }

                    if (index == start)
                    {
                        index++;
                    }
                    else
                    {
                        tokens.Add(text[start..index]);
                    }
                }
            }

            return tokens;
        }

        private static string ReadQuoted(string text, ref int index)
        {
            var builder = new StringBuilder();
            index++;
            while (index < text.Length)
            {
                var current = text[index++];
                if (current == '"')
                {
                    return builder.ToString();
                }

                if (current == '\\' && index < text.Length)
                {
                    var escaped = text[index++];
                    builder.Append(escaped switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        _ => escaped
                    });
                }
                else
                {
                    builder.Append(current);
                }
            }

            throw new FormatException("VDF 字符串未结束。");
        }
    }
}

internal static class ProcessStartInfoExtensions
{
    public static ProcessStartInfo AddArguments(this ProcessStartInfo info, params string[] arguments)
    {
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }
}
