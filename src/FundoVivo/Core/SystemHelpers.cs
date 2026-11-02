using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace FundoVivo.Core;

/// <summary>Memory of the engine plus the Edge processes it started for web wallpapers.</summary>
public static class ProcessTree
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PROCESSENTRY32
    {
        public int dwSize; public int cntUsage; public int th32ProcessID; public IntPtr th32DefaultHeapID; public int th32ModuleID;
        public int cntThreads; public int th32ParentProcessID; public int pcPriClassBase; public int dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32FirstW(IntPtr snap, ref PROCESSENTRY32 e);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32NextW(IntPtr snap, ref PROCESSENTRY32 e);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    public static (long privateBytes, int processes) Measure(int rootPid)
    {
        var children = new Dictionary<int, List<int>>();
        IntPtr snap = CreateToolhelp32Snapshot(2, 0);
        if (snap == new IntPtr(-1)) return (0, 0);
        try
        {
            var e = new PROCESSENTRY32 { dwSize = Marshal.SizeOf<PROCESSENTRY32>() };
            for (bool ok = Process32FirstW(snap, ref e); ok; ok = Process32NextW(snap, ref e))
            {
                if (!children.TryGetValue(e.th32ParentProcessID, out var list)) children[e.th32ParentProcessID] = list = new List<int>();
                list.Add(e.th32ProcessID);
            }
        }
        finally { CloseHandle(snap); }

        long total = 0;
        int count = 0;
        var stack = new Stack<int>();
        stack.Push(rootPid);
        var seen = new HashSet<int>();
        while (stack.Count > 0)
        {
            int pid = stack.Pop();
            if (!seen.Add(pid)) continue;
            try
            {
                using var p = Process.GetProcessById(pid);
                total += p.PrivateMemorySize64;
                count++;
            }
            catch { continue; }
            if (children.TryGetValue(pid, out var kids)) foreach (int k in kids) if (k != pid) stack.Push(k);
        }
        return (total, count);
    }
}

public static class StartupEntry
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "FundoVivo";

    public static bool Enabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key);
            return k?.GetValue(Name) is string v && v.Contains(AppPaths.ExePath, StringComparison.OrdinalIgnoreCase);
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(Key);
            if (value) k.SetValue(Name, $"\"{AppPaths.ExePath}\" --engine");
            else k.DeleteValue(Name, false);
        }
    }
}

/// <summary>The GitHub token, encrypted with Windows DPAPI for this user, never in the config or the release.</summary>
public static class Secrets
{
    [StructLayout(LayoutKind.Sequential)] struct DATA_BLOB { public int cbData; public IntPtr pbData; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptProtectData(ref DATA_BLOB input, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DATA_BLOB output);
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptUnprotectData(ref DATA_BLOB input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DATA_BLOB output);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr mem);

    public static string GitHubToken
    {
        get
        {
            try
            {
                if (!File.Exists(AppPaths.SecretsFile)) return null;
                byte[] plain = Transform(File.ReadAllBytes(AppPaths.SecretsFile), protect: false);
                return plain == null ? null : Encoding.UTF8.GetString(plain);
            }
            catch { return null; }
        }
        set
        {
            if (string.IsNullOrWhiteSpace(value)) { File.Delete(AppPaths.SecretsFile); return; }
            File.WriteAllBytes(AppPaths.SecretsFile, Transform(Encoding.UTF8.GetBytes(value.Trim()), protect: true));
        }
    }

    static byte[] Transform(byte[] data, bool protect)
    {
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            var input = new DATA_BLOB { cbData = data.Length, pbData = handle.AddrOfPinnedObject() };
            const int UI_FORBIDDEN = 1;
            bool ok = protect
                ? CryptProtectData(ref input, "Fundo Vivo", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UI_FORBIDDEN, out var output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UI_FORBIDDEN, out output);
            if (!ok) return null;
            var result = new byte[output.cbData];
            Marshal.Copy(output.pbData, result, 0, output.cbData);
            LocalFree(output.pbData);
            return result;
        }
        finally { handle.Free(); }
    }
}
