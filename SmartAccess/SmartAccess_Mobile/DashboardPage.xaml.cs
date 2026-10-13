using System;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using SmartAccess.Models;
using SmartAccess.Services;

namespace SmartAccess
{
    public partial class DashboardPage : ContentPage
    {
        private readonly ObservableCollection<NotificationItem> notifications = new();
        private bool isTimerRunning = false;
        private bool isUnlocking = false;
        private bool myIsHome = true;

        public DashboardPage()
        {
            InitializeComponent();
            BindableLayout.SetItemsSource(NotifContainer, notifications);
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            isTimerRunning = true;
            CheckStatus();
            LoadNotifications();
            LoadPresence();
            StartStatusTimer();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            isTimerRunning = false;
        }

        private void StartStatusTimer()
        {
            Dispatcher.StartTimer(TimeSpan.FromSeconds(5), () =>
            {
                if (!isTimerRunning) return false;
                CheckStatus();
                return true;
            });
        }

        private async void OnRefreshing(object sender, EventArgs e)
        {
            await Task.WhenAll(CheckStatusAsync(), LoadNotificationsAsync(), LoadPresenceAsync());
            Refresher.IsRefreshing = false;
        }

        // Stare
        private async void CheckStatus() => await CheckStatusAsync();

        private async Task CheckStatusAsync()
        {
            try
            {
                var data = await ApiService.Get("/esp/status");
                bool online = data.TryGetProperty("online", out var o) && o.GetBoolean();
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (online)
                    {
                        StatusDot.Fill = new SolidColorBrush(Color.FromArgb("#DEFF9A"));
                        LblSystemStatus.Text = "SYSTEM ACTIVE";
                        LblSystemStatus.TextColor = Color.FromArgb("#DEFF9A");
                    }
                    else
                    {
                        StatusDot.Fill = new SolidColorBrush(Color.FromArgb("#FF5555"));
                        LblSystemStatus.Text = "SYSTEM INACTIVE";
                        LblSystemStatus.TextColor = Color.FromArgb("#FF5555");
                    }
                });
            }
            catch
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    StatusDot.Fill = new SolidColorBrush(Color.FromArgb("#FF5555"));
                    LblSystemStatus.Text = "CONNECTION ERROR";
                    LblSystemStatus.TextColor = Color.FromArgb("#FF5555");
                });
            }
        }

        // Notificari
        private async void LoadNotifications() => await LoadNotificationsAsync();

        private async Task LoadNotificationsAsync()
        {
            try
            {
                var data = await ApiService.Get("/notifications");
                int unread = data.TryGetProperty("unread_count", out var u) ? u.GetInt32() : 0;
                var list = ApiService.ParseList<NotificationItem>(data, "notifications");
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    LblNotifCount.Text = unread.ToString();
                    notifications.Clear();
                    foreach (var n in list) notifications.Add(n);
                });
            }
            catch { /* pastram afisajul curent */ }
        }

        private void OnBellTapped(object sender, TappedEventArgs e)
        {
            NotifOverlay.IsVisible = true;
            LoadNotifications();
        }

        private void OnCloseNotif(object sender, EventArgs e)
        {
            NotifOverlay.IsVisible = false;
        }

        private async void OnMarkAllRead(object sender, EventArgs e)
        {
            try
            {
                await ApiService.Put("/notifications/read-all");
                LoadNotifications();
            }
            catch (ApiException ex) { await DisplayAlert("Error", ex.Message, "OK"); }
        }

        private async void OnClearNotif(object sender, EventArgs e)
        {
            bool ok = await DisplayAlert("Confirm", "Delete all notifications? This action is irreversible.", "YES", "NO");
            if (!ok) return;
            try
            {
                await ApiService.Delete("/notifications");
                LoadNotifications();
            }
            catch (ApiException ex) { await DisplayAlert("Error", ex.Message, "OK"); }
        }

        // Prezenta
        private async void LoadPresence() => await LoadPresenceAsync();

        private async Task LoadPresenceAsync()
        {
            try
            {
                var data = await ApiService.Get("/members");
                var list = ApiService.ParseList<Member>(data, "members");
                var self = list.Find(m => m.Id == Session.UserId);
                if (self == null) return;
                myIsHome = self.IsHome;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    BtnPresence.Text = myIsHome ? "I'm leaving" : "I'm home";
                    BtnPresence.IsVisible = true;
                });
            }
            catch { }
        }

        private async void OnPresenceToggle(object sender, EventArgs e)
        {
            BtnPresence.IsEnabled = false;
            try
            {
                bool newState = !myIsHome;
                var data = await ApiService.Put($"/members/{Session.UserId}/presence", new { is_home = newState });
                if (data.TryGetProperty("member", out var mem) && mem.TryGetProperty("is_home", out var h))
                    myIsHome = h.GetBoolean();
                else
                    myIsHome = newState;
                BtnPresence.Text = myIsHome ? "I'm leaving" : "I'm home";
            }
            catch (ApiException ex) { await DisplayAlert("Error", ex.Message, "OK"); }
            finally { BtnPresence.IsEnabled = true; }
        }

        // Deblocare
        private async void OnUnlockTapped(object sender, TappedEventArgs e)
        {
            if (isUnlocking) return;
            isUnlocking = true;

            try
            {
                BtnUnlock.BackgroundColor = Color.FromArgb("#FFAA33");
                UnlockIcon.IsVisible = false;
                UnlockSpinner.IsVisible = true;
                UnlockSpinner.IsRunning = true;

                await ApiService.Post("/esp/unlock");

                UnlockSpinner.IsRunning = false;
                UnlockSpinner.IsVisible = false;
                BtnUnlock.BackgroundColor = Color.FromArgb("#DEFF9A");
                UnlockIcon.Source = "icon_check_dark.png";
                UnlockIcon.IsVisible = true;
                LblUnlock.Text = "DOOR UNLOCKED";

                Dispatcher.StartTimer(TimeSpan.FromSeconds(4), () =>
                {
                    ResetUnlockButton();
                    return false;
                });
            }
            catch (ApiException ex)
            {
                UnlockSpinner.IsRunning = false;
                UnlockSpinner.IsVisible = false;
                BtnUnlock.BackgroundColor = Color.FromArgb("#FF5555");
                UnlockIcon.Source = "icon_x_dark.png";
                UnlockIcon.IsVisible = true;
                await DisplayAlert("Error", ex.Message, "OK");
                Dispatcher.StartTimer(TimeSpan.FromSeconds(2), () =>
                {
                    ResetUnlockButton();
                    return false;
                });
            }
        }

        private void ResetUnlockButton()
        {
            BtnUnlock.BackgroundColor = Color.FromArgb("#DEFF9A");
            UnlockIcon.Source = "icon_unlock_dark.png";
            UnlockIcon.IsVisible = true;
            UnlockSpinner.IsVisible = false;
            LblUnlock.Text = "UNLOCK";
            isUnlocking = false;
        }
    }
}
