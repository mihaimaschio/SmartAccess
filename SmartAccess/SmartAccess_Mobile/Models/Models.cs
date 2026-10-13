using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace SmartAccess.Models
{
    // Optiuni JSON partajate: API-ul returneaza snake_case (full_name, fingerprint_slot, etc.)
    public static class Json
    {
        public static readonly JsonSerializerOptions Opts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };
    }

    // /api/members  si  /api/users
    public class Member
    {
        public int Id { get; set; }
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";
        public string Role { get; set; } = "";
        public int? FingerprintSlot { get; set; }
        public bool IsActive { get; set; }
        public bool IsHome { get; set; }
        public DateTime? LastSeenAt { get; set; }
        public DateTime? CreatedAt { get; set; }

        // Proprietati de afisare
        [JsonIgnore] public string Initial =>
            string.IsNullOrEmpty(FullName) ? "?" : FullName.Substring(0, 1).ToUpper();
        [JsonIgnore] public bool IsAdminRole => Role == "admin";
        [JsonIgnore] public bool HasFingerprint => FingerprintSlot != null;
        [JsonIgnore] public string HomeColor => IsHome ? "#DEFF9A" : "#666666";
        // Ellipse.Fill e de tip Brush, iar un string nu se converteste automat la legare,
        // asa ca punctul de prezenta primeste direct o pensula.
        [JsonIgnore] public Brush HomeBrush => new SolidColorBrush(Color.FromArgb(HomeColor));
        [JsonIgnore] public string HomeStatus
        {
            get
            {
                string baseTxt = IsHome ? "Home" : "Away";
                if (LastSeenAt == null) return baseTxt;
                string since = Helpers.Since(LastSeenAt.Value);
                return since.Length == 0 ? baseTxt : baseTxt + " for " + since;
            }
        }
        [JsonIgnore] public string FingerprintBadge =>
            HasFingerprint ? "Fingerprint enrolled" : "No fingerprint";
        [JsonIgnore] public string FingerprintBadgeColor => HasFingerprint ? "#DEFF9A" : "#777777";
        [JsonIgnore] public string StatusBadge => IsActive ? "Active" : "Inactive";
        [JsonIgnore] public string StatusBadgeColor => IsActive ? "#DEFF9A" : "#FF5555";
        [JsonIgnore] public string RoleBadge => IsAdminRole ? "Admin" : "Member";
    }

    // /api/access-log
    public class AccessLog
    {
        public int Id { get; set; }
        public int? UserId { get; set; }
        public string? UserName { get; set; }
        public string AccessType { get; set; } = "";
        public string Result { get; set; } = "";
        public string? Details { get; set; }
        public DateTime CreatedAt { get; set; }

        [JsonIgnore] public bool Granted => Result == "granted";
        [JsonIgnore] public string DisplayName => string.IsNullOrEmpty(UserName) ? "Unknown" : UserName!;
        [JsonIgnore] public string ResultLabel => Granted ? "Granted" : "Denied";
        [JsonIgnore] public string ResultColor => Granted ? "#DEFF9A" : "#FF5555";
        [JsonIgnore] public Brush ResultBrush => new SolidColorBrush(Color.FromArgb(ResultColor));
        [JsonIgnore] public string ResultBg => Granted ? "#152210" : "#2A1010";
        [JsonIgnore] public string TypeLabel => AccessType switch
        {
            "fingerprint" => "Fingerprint",
            "pin" => "PIN",
            "app_unlock" => "App",
            _ => "Unknown"
        };
        [JsonIgnore] public string DetailLine => Details ?? TypeLabel;
        [JsonIgnore] public string TimeAgo => Helpers.FormatDate(CreatedAt);
    }

    // /api/notifications
    public class NotificationItem
    {
        public int Id { get; set; }
        public string Type { get; set; } = "";
        public string Message { get; set; } = "";
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }

        [JsonIgnore] public string TimeAgo => Helpers.FormatDate(CreatedAt);
        [JsonIgnore] public string MessageColor => IsRead ? "#888888" : "#DDDDDD";
        [JsonIgnore] public string DotColor => IsRead ? "#333333" : "#DEFF9A";
        [JsonIgnore] public Brush DotBrush => new SolidColorBrush(Color.FromArgb(DotColor));
    }

    // /api/invitations
    public class Invitation
    {
        public int Id { get; set; }
        public string Email { get; set; } = "";
        public string FullName { get; set; } = "";
        public DateTime ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; }

        [JsonIgnore] public string ExpiresLine => "Expires " + ExpiresAt.ToLocalTime().ToString("dd/MM HH:mm");
        [JsonIgnore] public string Subtitle => Email + " · " + ExpiresLine;
    }

    public static class Helpers
    {
        // Echivalentul formatDate() din api.js
        public static string FormatDate(DateTime dateUtc)
        {
            var d = dateUtc.ToLocalTime();
            var diff = DateTime.Now - d;
            if (diff.TotalMinutes < 1) return "Now";
            if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes} min";
            if (diff.TotalDays < 1) return $"{(int)diff.TotalHours}h";
            return d.ToString("dd/MM/yyyy HH:mm");
        }

        // Sub un minut nu are rost sa afisam durata, asa ca returnam sir gol
        // si ramane doar "Home" / "Away".
        public static string Since(DateTime dateUtc)
        {
            var diff = DateTime.Now - dateUtc.ToLocalTime();
            if (diff.TotalMinutes < 1) return "";
            if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes} min";
            if (diff.TotalDays < 1) return $"{(int)diff.TotalHours}h";
            int days = (int)diff.TotalDays;
            return days == 1 ? "1 day" : $"{days} days";
        }
    }
}
