using Microsoft.Maui.Storage;

namespace SmartAccess.Services
{
    // Starea de autentificare (token JWT + utilizator curent), persistata in Preferences.
    // Echivalentul lui localStorage 'sa_token' / 'sa_user' din web (api.js).
    public static class Session
    {
        public static string? Token { get; private set; }
        public static int UserId { get; private set; }
        public static string FullName { get; private set; } = "";
        public static string Email { get; private set; } = "";
        public static string Role { get; private set; } = "";

        public static bool IsLoggedIn => !string.IsNullOrEmpty(Token);
        public static bool IsAdmin => Role == "admin";

        public static void Load()
        {
            Token = Preferences.Default.Get("sa_token", "");
            if (string.IsNullOrEmpty(Token)) Token = null;
            UserId = Preferences.Default.Get("sa_user_id", 0);
            FullName = Preferences.Default.Get("sa_user_name", "");
            Email = Preferences.Default.Get("sa_user_email", "");
            Role = Preferences.Default.Get("sa_user_role", "");
        }

        public static void Save(string token, int userId, string email, string fullName, string role)
        {
            Token = token;
            UserId = userId;
            Email = email;
            FullName = fullName;
            Role = role;

            Preferences.Default.Set("sa_token", token);
            Preferences.Default.Set("sa_user_id", userId);
            Preferences.Default.Set("sa_user_email", email);
            Preferences.Default.Set("sa_user_name", fullName);
            Preferences.Default.Set("sa_user_role", role);
        }

        public static void Clear()
        {
            Token = null;
            UserId = 0;
            FullName = "";
            Email = "";
            Role = "";

            Preferences.Default.Remove("sa_token");
            Preferences.Default.Remove("sa_user_id");
            Preferences.Default.Remove("sa_user_email");
            Preferences.Default.Remove("sa_user_name");
            Preferences.Default.Remove("sa_user_role");
        }
    }
}
