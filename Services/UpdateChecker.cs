using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DeskNest
{
    // Informations sur une version publiée sur GitHub.
    // Sha256 vient du corps de la release ; ChecksumUrl pointe vers un fichier .sha256 joint.
    public sealed record UpdateInfo(
        Version Version,
        string DownloadUrl,
        long Size,
        string PageUrl,
        string? Sha256 = null,
        string? ChecksumUrl = null);

    // Vérifie les releases GitHub, télécharge l'installateur et contrôle son empreinte
    // SHA-256 publiée avec la release. La signature Authenticode reste disponible
    // derrière RequireSignature, mais n'est pas exigée : les builds sont non signés.
    public static class UpdateChecker
    {
        private const string Owner = "aziizfarhat";
        private const string Repo = "DeskNest";
        private const string AssetName = "DeskNest-Setup.exe";

        // Passe à true le jour où les installateurs seront signés par un certificat.
        public const bool RequireSignature = false;

        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("DeskNest", FormatVersion(CurrentVersion())));
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            return client;
        }

        // Version complète (4 nombres) : 0.8.3.1 ne doit jamais être affichée 0.8.3.
        public static string FormatVersion(Version version) =>
            $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}.{Math.Max(version.Revision, 0)}";

        public static Version CurrentVersion() =>
            typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0);

        // Retourne la dernière version si elle est plus récente que celle installée.
        public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
        {
            string url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
            using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var root = doc.RootElement;
            if (!root.TryGetProperty("tag_name", out var tagElement)) return null;

            string tag = (tagElement.GetString() ?? "").Trim().TrimStart('v', 'V');
            if (!Version.TryParse(tag, out var version)) return null;
            if (Normalize(version) <= Normalize(CurrentVersion())) return null;

            string page = root.TryGetProperty("html_url", out var pageElement)
                ? pageElement.GetString() ?? ""
                : "";
            string notes = root.TryGetProperty("body", out var bodyElement)
                ? bodyElement.GetString() ?? ""
                : "";
            string download = "";
            string checksumUrl = "";
            long size = 0;

            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    if (!asset.TryGetProperty("name", out var nameElement)) continue;
                    string? name = nameElement.GetString();
                    if (string.IsNullOrEmpty(name)) continue;

                    if (IsChecksumAsset(name))
                    {
                        if (string.IsNullOrEmpty(checksumUrl) &&
                            asset.TryGetProperty("browser_download_url", out var checksumElement))
                            checksumUrl = checksumElement.GetString() ?? "";
                        continue;
                    }

                    // Les releases passées utilisent « DeskNest-Setup-0.7.3.0.exe » ;
                    // on accepte donc tout installateur DeskNest-Setup*.exe.
                    bool isInstaller = name.StartsWith("DeskNest-Setup", StringComparison.OrdinalIgnoreCase) &&
                                       name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
                    if (!isInstaller) continue;
                    if (asset.TryGetProperty("browser_download_url", out var urlElement))
                        download = urlElement.GetString() ?? "";
                    if (asset.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out long n))
                        size = n;
                    break;
                }
            }

            if (string.IsNullOrEmpty(download)) return null;
            // L'empreinte peut être dans le corps de la release, sinon dans un fichier joint.
            string? sha256 = ExtractSha256(notes, "DeskNest-Setup");
            return new UpdateInfo(version, download, size, page, sha256,
                string.IsNullOrEmpty(checksumUrl) ? null : checksumUrl);
        }

        private static bool IsChecksumAsset(string name) =>
            name.Equals("SHA256SUMS", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase) ||
            (name.Contains("DeskNest-Setup", StringComparison.OrdinalIgnoreCase) &&
             (name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase) ||
              name.EndsWith(".sha256.txt", StringComparison.OrdinalIgnoreCase) ||
              name.EndsWith(".sha256sum", StringComparison.OrdinalIgnoreCase)));

        // Cherche une empreinte SHA-256 (64 caractères hexadécimaux). Si le texte en
        // contient plusieurs, on ne retient que la ligne qui cite l'installateur.
        private static string? ExtractSha256(string text, string? preferredName = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            string? only = null;
            bool multiple = false;
            foreach (var line in text.Split('\n'))
            {
                foreach (Match match in Regex.Matches(line, @"(?i)\b[0-9a-f]{64}\b"))
                {
                    string hash = match.Value.ToLowerInvariant();
                    if (preferredName != null &&
                        line.Contains(preferredName, StringComparison.OrdinalIgnoreCase))
                        return hash;

                    if (only == null) only = hash;
                    else if (!string.Equals(only, hash, StringComparison.OrdinalIgnoreCase)) multiple = true;
                }
            }

            return multiple ? null : only;
        }

        private static Version Normalize(Version v) =>
            new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

        // Télécharge l'installateur dans le dossier temporaire de l'utilisateur.
        public static async Task<string> DownloadAsync(UpdateInfo info, CancellationToken ct = default)
        {
            string dir = Path.Combine(Path.GetTempPath(), "DeskNest-Update");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, AssetName);
            string part = path + ".part";

            using (var response = await Http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct)
                       .ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var destination = new FileStream(part, FileMode.Create, FileAccess.Write,
                    FileShare.None, 81920, useAsync: true);
                await source.CopyToAsync(destination, ct).ConfigureAwait(false);
            }

            if (info.Size > 0 && new FileInfo(part).Length != info.Size)
            {
                try { File.Delete(part); } catch { }
                throw new IOException("Downloaded file size does not match the release asset.");
            }

            if (File.Exists(path)) File.Delete(path);
            File.Move(part, path);
            return path;
        }

        public enum VerificationResult
        {
            Ok,
            MissingChecksum,
            ChecksumMismatch,
            SignatureInvalid
        }

        // Vérifie l'installateur téléchargé. La signature Authenticode n'est contrôlée
        // que si RequireSignature est actif ; sinon, l'empreinte SHA-256 publiée avec
        // la release fait foi. Sans empreinte, l'installation est refusée.
        public static async Task<VerificationResult> VerifyAsync(
            UpdateInfo info, string path, CancellationToken ct = default)
        {
            if (RequireSignature && !IsSignatureValid(path))
                return VerificationResult.SignatureInvalid;

            string? expected = info.Sha256;
            if (string.IsNullOrEmpty(expected) && !string.IsNullOrEmpty(info.ChecksumUrl))
                expected = await FetchChecksumAsync(info.ChecksumUrl!, ct).ConfigureAwait(false);

            if (string.IsNullOrEmpty(expected))
                return VerificationResult.MissingChecksum;

            try
            {
                await using var stream = File.OpenRead(path);
                byte[] hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
                string actual = Convert.ToHexString(hash);
                return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
                    ? VerificationResult.Ok
                    : VerificationResult.ChecksumMismatch;
            }
            catch (Exception ex)
            {
                Diag.Log("calcul de l'empreinte SHA-256 : " + ex.Message);
                return VerificationResult.ChecksumMismatch;
            }
        }

        private static async Task<string?> FetchChecksumAsync(string url, CancellationToken ct)
        {
            try
            {
                using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                string text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return ExtractSha256(text, "DeskNest-Setup") ?? ExtractSha256(text);
            }
            catch (Exception ex)
            {
                Diag.Log("téléchargement de l'empreinte : " + ex.Message);
                return null;
            }
        }

        public static bool IsSignatureValid(string path) => VerifyAuthenticode(path) == 0;

        // Lance l'installateur Inno Setup en mode silencieux. Il ferme l'application
        // grâce au mutex partagé, puis l'instance appelante s'arrête.
        public static void Install(string path)
        {
            var psi = new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                Arguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS"
            };
            Process.Start(psi);
        }

        // ---------- Vérification de la signature Authenticode ----------
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WINTRUST_FILE_INFO
        {
            public int cbStruct;
            public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_DATA
        {
            public int cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public int dwUIChoice;
            public int fdwRevocationChecks;
            public int dwUnionChoice;
            public IntPtr pFile;
            public int dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public int dwProvFlags;
            public int dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
        private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WINTRUST_DATA pWVTData);

        private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 =
            new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        // Vérifie la signature Authenticode *intégrée* au fichier (celle produite par
        // signtool). Les binaires Windows signés uniquement par catalogue renvoient
        // TRUST_E_NOSIGNATURE : ce n'est pas un problème pour nos installateurs.
        private static int VerifyAuthenticode(string path)
        {
            IntPtr filePtr = IntPtr.Zero;
            try
            {
                var file = new WINTRUST_FILE_INFO
                {
                    cbStruct = Marshal.SizeOf<WINTRUST_FILE_INFO>(),
                    pcwszFilePath = path
                };
                filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
                Marshal.StructureToPtr(file, filePtr, false);

                var data = new WINTRUST_DATA
                {
                    cbStruct = Marshal.SizeOf<WINTRUST_DATA>(),
                    dwUIChoice = 2,          // WTD_UI_NONE
                    fdwRevocationChecks = 0, // WTD_REVOKE_NONE
                    dwUnionChoice = 1,       // WTD_CHOICE_FILE
                    pFile = filePtr,
                    dwStateAction = 1        // WTD_STATEACTION_VERIFY
                };

                var action = WINTRUST_ACTION_GENERIC_VERIFY_V2;
                int result = WinVerifyTrust(IntPtr.Zero, ref action, ref data);

                // Libère l'état alloué par WinVerifyTrust, quel que soit le résultat.
                data.dwStateAction = 2; // WTD_STATEACTION_CLOSE
                var closeAction = WINTRUST_ACTION_GENERIC_VERIFY_V2;
                WinVerifyTrust(IntPtr.Zero, ref closeAction, ref data);

                return result;
            }
            catch (Exception ex)
            {
                Diag.Log("vérification de signature : " + ex.Message);
                return -1;
            }
            finally
            {
                if (filePtr != IntPtr.Zero)
                {
                    Marshal.DestroyStructure<WINTRUST_FILE_INFO>(filePtr);
                    Marshal.FreeHGlobal(filePtr);
                }
            }
        }
    }
}
