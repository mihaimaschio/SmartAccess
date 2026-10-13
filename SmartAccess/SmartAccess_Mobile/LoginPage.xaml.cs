using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using SmartAccess.Services;

namespace SmartAccess
{
    public partial class LoginPage : ContentPage
    {
        public LoginPage()
        {
            InitializeComponent();
            EntServer.Text = AppConfig.ServerUrl;
        }

        private async void OnLoginClicked(object sender, EventArgs e)
        {
            string server = EntServer.Text?.Trim() ?? "";
            string email = EntEmail.Text?.Trim() ?? "";
            string password = EntPassword.Text ?? "";

            if (string.IsNullOrEmpty(server))
            {
                ShowError("Enter the server address.");
                return;
            }
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                ShowError("Enter email and password.");
                return;
            }

            // Salvam adresa serverului inainte de request
            AppConfig.ServerUrl = server;

            SetBusy(true);
            ErrorBanner.IsVisible = false;

            try
            {
                var data = await ApiService.Post("/auth/login", new { email, password });

                string token = data.GetProperty("token").GetString() ?? "";
                var user = data.GetProperty("user");
                int id = user.GetProperty("id").GetInt32();
                string fullName = user.GetProperty("full_name").GetString() ?? "";
                string role = user.GetProperty("role").GetString() ?? "user";
                string userEmail = user.GetProperty("email").GetString() ?? email;

                Session.Save(token, id, userEmail, fullName, role);

                // Trecem la aplicatia principala (Shell cu tab-uri)
                Application.Current!.MainPage = new AppShell();
            }
            catch (ApiException ex)
            {
                ShowError(ex.Message);
            }
            catch (Exception ex)
            {
                ShowError("Unexpected error: " + ex.Message);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorBanner.IsVisible = true;
        }

        private void SetBusy(bool busy)
        {
            BtnLogin.IsEnabled = !busy;
            BtnLogin.Text = busy ? "SIGNING IN..." : "SIGN IN";
            LoginSpinner.IsRunning = busy;
            LoginSpinner.IsVisible = busy;
        }
    }
}
