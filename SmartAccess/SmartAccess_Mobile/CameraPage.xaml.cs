using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using SmartAccess.Services;

namespace SmartAccess
{
    public partial class CameraPage : ContentPage
    {
        // Client separat pentru camera (alt host decat backend-ul).
        private static readonly HttpClient _cam = new() { Timeout = TimeSpan.FromSeconds(5) };

        private bool isStreaming;
        private string? captureUrl;
        private int failCount;

        public CameraPage()
        {
            InitializeComponent();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            LoadCamera();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            isStreaming = false;
        }

        private void OnReload(object sender, EventArgs e) => LoadCamera();

        private async void LoadCamera()
        {
            isStreaming = false;      // opreste o eventuala bucla anterioara
            await Task.Delay(50);
            ShowLoading("Fetching camera URL...");

            string? url = null;
            try
            {
                var data = await ApiService.Get("/settings");
                if (data.TryGetProperty("settings", out var s) &&
                    s.TryGetProperty("camera_url", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.String)
                    url = c.GetString();
            }
            catch (ApiException ex)
            {
                ShowError("Error", ex.Message);
                return;
            }

            if (string.IsNullOrWhiteSpace(url))
            {
                ShowError("Camera not configured", "Set the camera URL in Admin - Settings.");
                return;
            }

            // WebView-ul Android nu reda stream-ul MJPEG (multipart), asa ca in loc
            // de /stream interogam periodic /capture (o poza JPEG) si o afisam intr-un Image.
            captureUrl = url.Replace("/stream", "/capture");
            if (!captureUrl.Contains("/capture"))
                captureUrl = captureUrl.TrimEnd('/') + "/capture";

            failCount = 0;
            ShowLoading("Connecting to stream...");
            isStreaming = true;
            StartPolling();
        }

        private void StartPolling()
        {
            Dispatcher.StartTimer(TimeSpan.FromMilliseconds(350), () =>
            {
                if (!isStreaming) return false;
                _ = FetchFrameAsync();
                return true;
            });
        }

        private async Task FetchFrameAsync()
        {
            if (string.IsNullOrEmpty(captureUrl)) return;

            string sep = captureUrl.Contains('?') ? "&" : "?";
            string u = $"{captureUrl}{sep}t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

            try
            {
                byte[] bytes = await _cam.GetByteArrayAsync(u);
                if (!isStreaming || bytes.Length == 0) return;

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    CamView.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
                    if (LoadingBox.IsVisible || ErrorBox.IsVisible)
                    {
                        LoadingBox.IsVisible = false;
                        ErrorBox.IsVisible = false;
                        StreamDot.Fill = new SolidColorBrush(Color.FromArgb("#DEFF9A"));
                        StreamStatus.Text = "LIVE";
                        StreamStatus.TextColor = Color.FromArgb("#DEFF9A");
                    }
                });
                failCount = 0;
            }
            catch
            {
                failCount++;
                if (failCount >= 6 && isStreaming)
                {
                    isStreaming = false;
                    MainThread.BeginInvokeOnMainThread(() =>
                        ShowError("Camera offline", "No response from the camera. Check that it is powered on and on the same network."));
                }
            }
        }

        private void ShowLoading(string text)
        {
            LoadingBox.IsVisible = true;
            LoadingText.Text = text;
            ErrorBox.IsVisible = false;
            StreamDot.Fill = new SolidColorBrush(Color.FromArgb("#555555"));
            StreamStatus.Text = text;
            StreamStatus.TextColor = Color.FromArgb("#888888");
        }

        private void ShowError(string title, string message)
        {
            isStreaming = false;
            LoadingBox.IsVisible = false;
            ErrorBox.IsVisible = true;
            ErrorTitle.Text = title;
            ErrorMsg.Text = message;
            CamView.Source = null;
            StreamDot.Fill = new SolidColorBrush(Color.FromArgb("#FF5555"));
            StreamStatus.Text = "OFFLINE";
            StreamStatus.TextColor = Color.FromArgb("#FF5555");
        }
    }
}
