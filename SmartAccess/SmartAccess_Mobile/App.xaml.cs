using Microsoft.Maui.Controls;
using SmartAccess.Services;

namespace SmartAccess
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();

            // Incarcam sesiunea salvata (token JWT + user)
            Session.Load();

            // Daca backend-ul raspunde 401 oriunde in aplicatie, revenim la login.
            ApiService.SessionExpired += () =>
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Current!.MainPage = new NavigationPage(new LoginPage());
                });
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            Page root = Session.IsLoggedIn
                ? new AppShell()
                : new NavigationPage(new LoginPage());

            return new Window(root);
        }
    }
}
