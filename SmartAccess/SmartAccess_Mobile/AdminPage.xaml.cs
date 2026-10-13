using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls;
using SmartAccess.Models;
using SmartAccess.Services;

namespace SmartAccess
{
    public partial class AdminPage : ContentPage
    {
        private readonly ObservableCollection<Member> users = new();
        private readonly ObservableCollection<Invitation> invitations = new();

        public AdminPage()
        {
            InitializeComponent();
            BindableLayout.SetItemsSource(UsersContainer, users);
            BindableLayout.SetItemsSource(InvitationsContainer, invitations);
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            LoadAll();
        }

        private async void OnRefreshing(object sender, EventArgs e)
        {
            await LoadAllAsync();
            Refresher.IsRefreshing = false;
        }

        private async void LoadAll() => await LoadAllAsync();

        private async Task LoadAllAsync()
        {
            await Task.WhenAll(LoadUsers(), LoadInvitations());
        }

        // Utilizatori
        private async Task LoadUsers()
        {
            try
            {
                var data = await ApiService.Get("/users");
                var list = ApiService.ParseList<Member>(data, "users");
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    users.Clear();
                    foreach (var u in list) users.Add(u);
                });
            }
            catch (ApiException ex)
            {
                await DisplayAlert("Error", ex.Message, "OK");
            }
        }

        private async void OnUserOptions(object sender, EventArgs e)
        {
            if (sender is not Button btn || btn.BindingContext is not Member user) return;

            bool isSelf = user.Id == Session.UserId;
            string presenceAction = user.IsHome ? "Mark away" : "Mark home";
            string toggleAction = user.IsActive ? "Deactivate" : "Activate";

            // Propriul cont nu poate fi dezactivat sau sters (protejat si pe server)
            string action = isSelf
                ? await DisplayActionSheet(user.FullName, "Cancel", null, presenceAction)
                : await DisplayActionSheet(user.FullName, "Cancel", null, presenceAction, toggleAction, "Delete account");

            if (action == presenceAction)
            {
                try
                {
                    await ApiService.Put($"/members/{user.Id}/presence", new { is_home = !user.IsHome });
                    LoadUsers();
                }
                catch (ApiException ex) { await DisplayAlert("Error", ex.Message, "OK"); }
            }
            else if (action == toggleAction)
            {
                try
                {
                    await ApiService.Put($"/users/{user.Id}", new { is_active = !user.IsActive });
                    LoadUsers();
                }
                catch (ApiException ex) { await DisplayAlert("Error", ex.Message, "OK"); }
            }
            else if (action == "Delete account")
            {
                bool ok = await DisplayAlert("Confirm", $"Delete {user.FullName}'s account?", "YES", "NO");
                if (!ok) return;
                try
                {
                    await ApiService.Delete($"/users/{user.Id}");
                    await DisplayAlert("Success", $"{user.FullName}'s account has been deleted.", "OK");
                    LoadUsers();
                }
                catch (ApiException ex) { await DisplayAlert("Error", ex.Message, "OK"); }
            }
        }

        // Invitatii
        private async Task LoadInvitations()
        {
            try
            {
                var data = await ApiService.Get("/invitations");
                var list = ApiService.ParseList<Invitation>(data, "invitations");
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    invitations.Clear();
                    foreach (var i in list) invitations.Add(i);
                });
            }
            catch { /* ignoram eroarea */ }
        }

        private async void OnInvite(object sender, EventArgs e)
        {
            string name = await DisplayPromptAsync("Invite member", "Full name:", "Continue", "Cancel", "e.g. Jane Smith");
            if (string.IsNullOrWhiteSpace(name)) return;

            string email = await DisplayPromptAsync("Invite member", "Email:", "Generate link", "Cancel", "jane@email.com", keyboard: Keyboard.Email);
            if (string.IsNullOrWhiteSpace(email)) return;

            try
            {
                var data = await ApiService.Post("/invitations", new { full_name = name.Trim(), email = email.Trim() });
                string token = data.TryGetProperty("token", out var t) ? (t.GetString() ?? "") : "";
                bool emailSent = data.TryGetProperty("email_sent", out var es) && es.GetBoolean();
                string link = $"{AppConfig.ServerUrl}/register.html?token={token}";

                string msg = emailSent
                    ? $"Invitation email sent to {email}.\n\nLink (valid for 48h):\n{link}"
                    : $"Link generated (email not configured). Send it manually:\n\n{link}";

                bool copy = await DisplayAlert("Invitation created", msg, "Copy link", "Close");
                if (copy)
                {
                    await Clipboard.Default.SetTextAsync(link);
                    await DisplayAlert("Copied", "Link copied to clipboard.", "OK");
                }
                LoadInvitations();
            }
            catch (ApiException ex)
            {
                await DisplayAlert("Error", ex.Message, "OK");
            }
        }

        private async void OnRevokeInvite(object sender, EventArgs e)
        {
            if (sender is not Button btn || btn.BindingContext is not Invitation inv) return;
            bool ok = await DisplayAlert("Confirm", $"Revoke the invitation for {inv.FullName}?", "YES", "NO");
            if (!ok) return;
            try
            {
                await ApiService.Delete($"/invitations/{inv.Id}");
                LoadInvitations();
            }
            catch (ApiException ex) { await DisplayAlert("Error", ex.Message, "OK"); }
        }
    }
}
