using Microsoft.Maui.Storage;

namespace SmartAccess.Services
{
    // Configurare aplicatie persistata local. Inlocuieste vechiul App.IpPlacuta:
    // acum aplicatia vorbeste cu backend-ul Node.js, nu direct cu ESP-ul.
    public static class AppConfig
    {
        public const string DefaultServerUrl = "http://10.244.211.203:3000";

        public static string ServerUrl
        {
            get => Preferences.Default.Get("server_url", DefaultServerUrl);
            set => Preferences.Default.Set("server_url", value.TrimEnd('/'));
        }
    }
}
