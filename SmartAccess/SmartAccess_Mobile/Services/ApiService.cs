using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SmartAccess.Services
{
    // Eroare de API cu mesaj prietenos (echivalentul throw new Error(data.error) din api.js)
    public class ApiException : Exception
    {
        public ApiException(string message) : base(message) { }
    }

    // Clientul HTTP catre backend-ul SmartAccess. Echivalentul lui apiFetch() din api.js:
    // ataseaza tokenul Bearer, gestioneaza 401 (sesiune expirata) si extrage .error la eroare.
    public static class ApiService
    {
        private static readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(20) };

        // Ridicat cand backend-ul raspunde 401 - App.cs il asculta si redirecteaza la login.
        public static event Action? SessionExpired;

        private static string BaseUrl => AppConfig.ServerUrl.TrimEnd('/') + "/api";

        public static async Task<JsonElement> SendAsync(HttpMethod method, string endpoint, object? body = null)
        {
            using var req = new HttpRequestMessage(method, BaseUrl + endpoint);

            if (!string.IsNullOrEmpty(Session.Token))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Session.Token);

            if (body != null)
                req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            HttpResponseMessage resp;
            try
            {
                resp = await _client.SendAsync(req);
            }
            catch (Exception ex)
            {
                throw new ApiException("Connection failed. Check the server and your network.\n(" + ex.Message + ")");
            }

            string text = await resp.Content.ReadAsStringAsync();

            JsonElement root = default;
            bool hasJson = false;
            if (!string.IsNullOrWhiteSpace(text))
            {
                try
                {
                    using var doc = JsonDocument.Parse(text);
                    root = doc.RootElement.Clone();
                    hasJson = true;
                }
                catch { /* raspuns ne-JSON */ }
            }

            if (resp.StatusCode == HttpStatusCode.Unauthorized && !endpoint.Contains("/auth/login"))
            {
                Session.Clear();
                SessionExpired?.Invoke();
                throw new ApiException("Session expired. Please sign in again.");
            }

            if (!resp.IsSuccessStatusCode)
            {
                string err = "Unknown error.";
                if (hasJson && root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String)
                    err = e.GetString() ?? err;
                throw new ApiException(err);
            }

            return root;
        }

        public static Task<JsonElement> Get(string endpoint) => SendAsync(HttpMethod.Get, endpoint);
        public static Task<JsonElement> Post(string endpoint, object? body = null) => SendAsync(HttpMethod.Post, endpoint, body);
        public static Task<JsonElement> Put(string endpoint, object? body = null) => SendAsync(HttpMethod.Put, endpoint, body);
        public static Task<JsonElement> Delete(string endpoint) => SendAsync(HttpMethod.Delete, endpoint);

        // Deserializeaza o proprietate (ex: "members", "logs") intr-o lista tipizata.
        public static System.Collections.Generic.List<T> ParseList<T>(JsonElement root, string property)
        {
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(property, out var arr))
                return JsonSerializer.Deserialize<System.Collections.Generic.List<T>>(arr.GetRawText(), Models.Json.Opts)
                       ?? new System.Collections.Generic.List<T>();
            return new System.Collections.Generic.List<T>();
        }
    }
}
