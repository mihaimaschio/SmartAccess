using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using SmartAccess.Models;
using SmartAccess.Services;

namespace SmartAccess
{
    public partial class HistoryPage : ContentPage
    {
        private readonly ObservableCollection<AccessLog> logs = new();
        private const int PageSize = 20;
        private int offset = 0;
        private int total = 0;
        private string filter = "";       // "", "granted", "denied"
        private bool isLoading = false;

        public HistoryPage()
        {
            InitializeComponent();
            HistoryList.ItemsSource = logs;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            Reload();
        }

        // Filtre
        private void OnFilterAll(object sender, EventArgs e) => SetFilter("");
        private void OnFilterGranted(object sender, EventArgs e) => SetFilter("granted");
        private void OnFilterDenied(object sender, EventArgs e) => SetFilter("denied");

        private void SetFilter(string f)
        {
            filter = f;
            // Stil pills
            StyleFilter(FilterAll, f == "");
            StyleFilter(FilterGranted, f == "granted");
            StyleFilter(FilterDenied, f == "denied");
            Reload();
        }

        private void StyleFilter(Button b, bool active)
        {
            b.BackgroundColor = active ? Color.FromArgb("#DEFF9A") : Color.FromArgb("#1A1A1A");
            b.TextColor = active ? Color.FromArgb("#121212") : Color.FromArgb("#888888");
            b.FontAttributes = active ? FontAttributes.Bold : FontAttributes.None;
        }

        private async void OnRefreshing(object sender, EventArgs e)
        {
            await ReloadAsync();
            Refresher.IsRefreshing = false;
        }

        private async void Reload() => await ReloadAsync();

        private async Task ReloadAsync()
        {
            offset = 0;
            logs.Clear();
            await LoadPage();
        }

        // Paginare
        private async void OnLoadMore(object sender, EventArgs e)
        {
            if (isLoading) return;
            if (logs.Count >= total && total > 0) return;  // tot incarcat
            await LoadPage();
        }

        private async Task LoadPage()
        {
            if (isLoading) return;
            isLoading = true;
            if (offset > 0)
            {
                LoadMoreSpinner.IsVisible = true;
                LoadMoreSpinner.IsRunning = true;
            }

            try
            {
                string url = $"/access-log?limit={PageSize}&offset={offset}";
                if (!string.IsNullOrEmpty(filter)) url += $"&result={filter}";

                var data = await ApiService.Get(url);
                total = data.TryGetProperty("total", out var t) ? t.GetInt32() : 0;
                var page = ApiService.ParseList<AccessLog>(data, "logs");

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    foreach (var l in page) logs.Add(l);
                    EmptyLabel.Text = string.IsNullOrEmpty(filter)
                        ? "No records"
                        : "No records for this filter";
                });

                offset += page.Count;
            }
            catch (ApiException ex)
            {
                await DisplayAlert("Error", ex.Message, "OK");
            }
            finally
            {
                isLoading = false;
                LoadMoreSpinner.IsVisible = false;
                LoadMoreSpinner.IsRunning = false;
            }
        }
    }
}
