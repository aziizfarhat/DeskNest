using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DeskNest
{
    // Informations sur une version publiée sur GitHub.
    public sealed record UpdateInfo(Version Version, string DownloadUrl, long Size, string PageUrl);

    // Vérifie les releases GitHub, télécharge l'installateur et contrôle sa signature
    // Authenticode avant de le lancer. Aucun certificat n'est embarqué dans l'application :
    // la confiance vient de la chaîne de certification Windows.
    public static class UpdateChecker
    {
        private const string Owner = "aziizfarhat";
        private const string Repo = "DeskNest";
        private const string AssetName = "DeskNest-Setup.exe";

        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("DeskNest", CurrentVersion().ToString(3)));
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            return client;
        }

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
            string download = "";
            long size = 0;

            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    if (!asset.TryGetProperty("name", out var nameElement)) continue;
                    string? name = nameElement.GetString();
                    if (string.IsNullOrEmpty(name)) continue;
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
            return new UpdateInfo(version, download, size, page);
        }

        private static Version Normalize(Version v) =>
            new(v.Major, v.Minor, Math.Max(v.Build, 0));

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
