using System;
using Microsoft.Maui.Controls;
using SmartAccess.Services;

namespace SmartAccess
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            // Afisam tab-ul Admin doar pentru utilizatorii cu rol de admin (ca pe web).
            AdminItem.IsVisible = Session.IsAdmin;

            HeaderUser.Text = string.IsNullOrEmpty(Session.FullName)
                ? "Secure IoT"
                : $"{Session.FullName} · {(Session.IsAdmin ? "Admin" : "Member")}";
        }

        private async void OnLogoutClicked(object sender, EventArgs e)
        {
            bool ok = await DisplayAlert("Sign Out", "Are you sure you want to sign out?", "YES", "NO");
            if (!ok) return;

            Session.Clear();
            Application.Current!.MainPage = new NavigationPage(new LoginPage());
        }
    }
}
