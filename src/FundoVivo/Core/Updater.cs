using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace FundoVivo.Core;

public sealed class UpdateInfo
{
    public string Current { get; init; }
    public string Latest { get; init; }
    public bool IsNewer { get; init; }
    public string Notes { get; init; }
    public string AssetApiUrl { get; init; }
    public string AssetName { get; init; }
    public string Sha256 { get; init; }
    public string Error { get; init; }
}

/// <summary>
/// Updates from the newest GitHub release, following the Clave model: download the .zip, check SHA-256 before
/// touching anything, keep the current version, swap, and go back on its own if the new one does not start.
/// The package holds only the app folder; config, converted videos and the token live elsewhere and stay.
/// </summary>
public static class Updater
{
    static HttpClient Client(string accept)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"FundoVivo/{AppPaths.Version}");
        http.DefaultRequestHeaders.Accept.ParseAdd(accept);
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        string token = Secrets.GitHubToken;
        if (!string.IsNullOrEmpty(token)) http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    public static int[] Parse(string v)
    {
        var parts = (v ?? "0").TrimStart('v', 'V').Split('.');
        var r = new int[3];
        for (int i = 0; i < 3 && i < parts.Length; i++)
            int.TryParse(new string(parts[i].TakeWhile(char.IsDigit).ToArray()), out r[i]);
        return r;
    }

    public static bool Newer(string a, string b)
    {
        int[] x = Parse(a), y = Parse(b);
        for (int i = 0; i < 3; i++) if (x[i] != y[i]) return x[i] > y[i];
        return false;
    }

    public static async Task<UpdateInfo> CheckAsync()
    {
        string current = AppPaths.Version;
        try
        {
            using var http = Client("application/vnd.github+json");
            using var res = await http.GetAsync($"https://api.github.com/repos/{AppPaths.Repository}/releases/latest");
            if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new UpdateInfo
                {
                    Current = current,
                    Error = string.IsNullOrEmpty(Secrets.GitHubToken)
                        ? "O GitHub não mostrou nenhuma versão publicada. Se o repositório é privado, guarde um token de leitura abaixo."
                        : "Ainda não existe nenhuma versão publicada (ou o token não tem acesso ao repositório).",
                };
            if (!res.IsSuccessStatusCode) return new UpdateInfo { Current = current, Error = $"O GitHub respondeu {(int)res.StatusCode}." };
            var rel = JsonNode.Parse(await res.Content.ReadAsStringAsync());
            string latest = rel?["tag_name"]?.ToString()?.TrimStart('v', 'V') ?? "";
            JsonNode asset = (rel?["assets"] as JsonArray)?.FirstOrDefault(a => a?["name"]?.ToString()?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true);
            string digest = asset?["digest"]?.ToString();
            return new UpdateInfo
            {
                Current = current,
                Latest = latest,
                IsNewer = Newer(latest, current),
                Notes = rel?["body"]?.ToString() ?? "",
                AssetApiUrl = asset?["url"]?.ToString(),
                AssetName = asset?["name"]?.ToString(),
                Sha256 = digest != null && digest.StartsWith("sha256:") ? digest[7..].ToLowerInvariant() : null,
            };
        }
        catch (Exception e)
        {
            return new UpdateInfo { Current = current, Error = "Sem conexão com o GitHub (" + e.Message + ")." };
        }
    }

    /// <summary>Downloads, verifies and unpacks the release, then hands over to a script that swaps the folders.</summary>
    public static async Task InstallAsync(UpdateInfo info, IProgress<string> status)
    {
        if (AppPaths.IsDevelopmentCopy)
            throw new InvalidOperationException("Esta é a cópia de desenvolvimento (tem .git ao lado). Atualize com git pull e ferramentas\\compilar.ps1.");
        if (info.AssetApiUrl == null) throw new InvalidOperationException("A versão publicada não tem o pacote .zip.");
        if (info.Sha256 == null) throw new InvalidOperationException("O GitHub não informou o SHA-256 do pacote; por segurança, nada foi instalado.");

        string root = AppPaths.UpdateFolder;
        string zip = Path.Combine(root, "package.zip");
        string staged = Path.Combine(root, "new");
        if (Directory.Exists(staged)) Directory.Delete(staged, true);

        status.Report("Baixando a versão " + info.Latest + "...");
        using (var http = Client("application/octet-stream"))
        using (var stream = await http.GetStreamAsync(info.AssetApiUrl))
        using (var file = File.Create(zip))
            await stream.CopyToAsync(file);

        status.Report("Conferindo o pacote...");
        string hash;
        using (var f = File.OpenRead(zip)) hash = Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant();
        if (hash != info.Sha256)
        {
            File.Delete(zip);
            throw new InvalidOperationException("O pacote baixado não confere com o SHA-256 do GitHub. Nada foi trocado; tente de novo.");
        }

        using (var archive = ZipFile.OpenRead(zip))
        {
            string full = Path.GetFullPath(staged) + Path.DirectorySeparatorChar;
            foreach (var entry in archive.Entries)
            {
                string target = Path.GetFullPath(Path.Combine(staged, entry.FullName));
                if (!target.StartsWith(full, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Pacote com caminho inválido.");
            }
            archive.ExtractToDirectory(staged);
        }
        File.Delete(zip);
        string appDir = File.Exists(Path.Combine(staged, "FundoVivo.exe")) ? staged
            : Directory.GetDirectories(staged).FirstOrDefault(d => File.Exists(Path.Combine(d, "FundoVivo.exe")))
              ?? throw new InvalidOperationException("O pacote não tem o FundoVivo.exe.");

        status.Report("Trocando os arquivos e reabrindo...");
        string script = Path.Combine(root, "apply-update.ps1");
        File.WriteAllText(script, SwapScript, new UTF8Encoding(true));
        string args = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\" -App \"{AppPaths.AppFolder}\" -New \"{appDir}\" -Work \"{root}\" -Version \"{info.Latest}\"";
        Process.Start(new ProcessStartInfo("powershell.exe", args) { UseShellExecute = false, CreateNoWindow = true });
        Engine.EngineHost.Send("{\"cmd\":\"quit\"}");
    }

    /// <summary>Called by a freshly started version to tell the swap script it opened fine.</summary>
    public static void ConfirmStarted()
    {
        try { File.WriteAllText(Path.Combine(AppPaths.UpdateFolder, "started.txt"), AppPaths.Version); } catch { }
    }

    // Runs outside the app (the exe cannot replace itself). Waits for every FundoVivo process in the app folder to
    // close, keeps the old folder in "previous", copies the new one in and starts it; if the new engine has not
    // confirmed within 25 s, the previous folder goes back.
    const string SwapScript = """
param([string]$App, [string]$New, [string]$Work, [string]$Version)
$ErrorActionPreference = 'Stop'
$log = Join-Path $Work 'apply-update.log'
function Note($t) { Add-Content -Path $log -Value ("{0:yyyy-MM-dd HH:mm:ss} {1}" -f (Get-Date), $t) -Encoding UTF8 }
Note "atualizando para $Version"
$exe = Join-Path $App 'FundoVivo.exe'
for ($i = 0; $i -lt 60; $i++) {
    $vivos = Get-Process FundoVivo -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }
    if (-not $vivos) { break }
    Start-Sleep -Milliseconds 500
}
Get-Process FundoVivo -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } | Stop-Process -Force -ErrorAction SilentlyContinue
$prev = Join-Path $Work 'previous'
if (Test-Path $prev) { Remove-Item $prev -Recurse -Force }
$ok = Join-Path $Work 'started.txt'
if (Test-Path $ok) { Remove-Item $ok -Force }
try {
    New-Item -ItemType Directory -Path $prev | Out-Null
    Get-ChildItem -LiteralPath $App -Force | Move-Item -Destination $prev
    Get-ChildItem -LiteralPath $New -Force | Copy-Item -Destination $App -Recurse -Force
} catch {
    Note "falhou ao trocar: $_"
    Get-ChildItem -LiteralPath $App -Force | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    Get-ChildItem -LiteralPath $prev -Force | Move-Item -Destination $App
    Start-Process $exe
    exit 1
}
Start-Process $exe
for ($i = 0; $i -lt 50; $i++) {
    if (Test-Path $ok) { Note "versao $Version abriu"; Remove-Item (Join-Path $Work 'new') -Recurse -Force -ErrorAction SilentlyContinue; exit 0 }
    Start-Sleep -Milliseconds 500
}
Note "a versao nova nao confirmou; voltando para a anterior"
Get-Process FundoVivo -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 800
Get-ChildItem -LiteralPath $App -Force | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Get-ChildItem -LiteralPath $prev -Force | Move-Item -Destination $App
Start-Process $exe
exit 2
""";
}
