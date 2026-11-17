using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DeskNest
{
    // Version publiée sur GitHub et page de la release à ouvrir dans le navigateur.
    public sealed record UpdateInfo(Version Version, string PageUrl);

    // Compare uniquement la version installée avec la dernière release GitHub.
    // Aucun téléchargement, aucune installation : l'utilisateur ouvre la page de
    // la release s'il le souhaite.
    public static class UpdateChecker
    {
        private const string Owner = "aziizfarhat";
        private const string Repo = "DeskNest";

        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            client.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("DeskNest", FormatVersion(CurrentVersion())));
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            return client;
        }

        // Version complète (4 nombres) : 0.8.3 ne doit jamais être affichée 0.8.3.
        public static string FormatVersion(Version version) =>
            $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}.{Math.Max(version.Revision, 0)}";

        // Version installée, lue dans l'assembly de l'application.
        public static Version CurrentVersion() =>
            typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0);

        // Retourne la dernière version si elle est plus récente que celle installée,
        // sinon null. Toute erreur réseau, limite de l'API ou réponse invalide est
        // laissée remonter à l'appelant (ignorée au démarrage).
        public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
        {
            string url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
            using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var root = doc.RootElement;

            if (!root.TryGetProperty("tag_name", out var tagElement))
                throw new InvalidDataException("The release has no tag_name.");

            string tag = (tagElement.GetString() ?? "").Trim();
            if (tag.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                tag = tag.Substring(1);

            if (!Version.TryParse(tag, out var latest))
                throw new InvalidDataException("The release tag is not a valid version.");

            if (Normalize(latest) <= Normalize(CurrentVersion()))
                return null;

            string page = root.TryGetProperty("html_url", out var pageElement)
                ? pageElement.GetString() ?? ""
                : "";

            if (string.IsNullOrEmpty(page))
                throw new InvalidDataException("The release has no html_url.");

            return new UpdateInfo(latest, page);
        }

        // 0.8.3 et 0.8.3.0 doivent être considérés comme la même version.
        private static Version Normalize(Version version) =>
            new(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));
    }
}
