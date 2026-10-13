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
    public partial class FamilyPage : ContentPage
    {
        private readonly ObservableCollection<Member> members = new();
        private bool isPollingEnroll = false;
        private string enrolledMemberName = "";
        private string baseLog = "";
        private int enrollMemberId = 0;
        private int? enrollSlot = null;

        // Folosit din XAML ca sa afisam meniul cu 3 puncte doar pentru admini.
        public bool IsAdminUser => Session.IsAdmin;

        private enum EnrollState { Initiated, WaitingFinger, Success, Error }
        private EnrollState currentEnrollState = EnrollState.Initiated;

        public FamilyPage()
        {
            InitializeComponent();
            MembersList.ItemsSource = members;
            Subtitle.Text = Session.IsAdmin
                ? "Household access management"
                : "Authorized household members";
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            LoadMembers();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            isPollingEnroll = false;
        }

        // Incarcare date
        private async void OnRefreshing(object sender, EventArgs e)
        {
            await LoadMembersAsync();
            Refresher.IsRefreshing = false;
        }

        private async void LoadMembers() => await LoadMembersAsync();

        private async Task LoadMembersAsync()
        {
            try
            {
                var data = await ApiService.Get("/members");
                var list = ApiService.ParseList<Member>(data, "members");
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    members.Clear();
                    foreach (var m in list) members.Add(m);
                });
            }
            catch (ApiException ex)
            {
                await DisplayAlert("Error", ex.Message, "OK");
            }
        }

        // Meniu de actiuni (admin)
        private async void OnMemberOptions(object sender, EventArgs e)
        {
            if (sender is not Button btn || btn.BindingContext is not Member member) return;

            string enrollAction = member.HasFingerprint ? "Delete fingerprint" : "Enroll fingerprint";
            string presenceAction = member.IsHome ? "Mark away" : "Mark home";
            string action = await DisplayActionSheet(
                member.FullName, "Cancel", null, enrollAction, presenceAction, "Delete account");

            if (action == enrollAction)
            {
                if (member.HasFingerprint)
                    await DeleteFingerprint(member);
                else
                    await StartEnroll(member);
            }
            else if (action == presenceAction)
            {
                await SetPresence(member, !member.IsHome);
            }
            else if (action == "Delete account")
            {
                await DeleteAccount(member);
            }
        }

        private async Task SetPresence(Member member, bool isHome)
        {
            try
            {
                await ApiService.Put($"/members/{member.Id}/presence", new { is_home = isHome });
                LoadMembers();
            }
            catch (ApiException ex)
            {
                await DisplayAlert("Error", ex.Message, "OK");
            }
        }

        private async Task DeleteFingerprint(Member member)
        {
            bool ok = await DisplayAlert("Confirm",
                $"Delete only the fingerprint for {member.FullName}? (account stays active)", "YES", "NO");
            if (!ok) return;

            try
            {
                await ApiService.Delete($"/members/{member.Id}/fingerprint");
                await DisplayAlert("Success", $"Fingerprint for {member.FullName} has been deleted.", "OK");
                LoadMembers();
            }
            catch (ApiException ex)
            {
                await DisplayAlert("Error", ex.Message, "OK");
            }
        }

        private async Task DeleteAccount(Member member)
        {
            bool ok = await DisplayAlert("Confirm",
                $"Completely delete the account and fingerprint for {member.FullName}?\nThis action is irreversible!", "YES, DELETE", "NO");
            if (!ok) return;

            try
            {
                await ApiService.Delete($"/users/{member.Id}");
                await DisplayAlert("Success", $"Account for {member.FullName} has been deleted.", "OK");
                LoadMembers();
            }
            catch (ApiException ex)
            {
                await DisplayAlert("Error", ex.Message, "OK");
            }
        }

        // Inrolare
        private async Task StartEnroll(Member member)
        {
            ShowEnrollTerminal(member.FullName);
            enrollMemberId = member.Id;
            enrollSlot = null;

            try
            {
                // Retinem logul curent al ESP-ului, ca sa afisam doar liniile noi
                await CaptureBaseLog();
                var resp = await ApiService.Post($"/members/{member.Id}/enroll");
                enrollSlot = resp.TryGetProperty("slot", out var sl) && sl.ValueKind == JsonValueKind.Number
                    ? sl.GetInt32() : (int?)null;

                Dispatcher.StartTimer(TimeSpan.FromMilliseconds(1500), () =>
                {
                    if (!isPollingEnroll) return false;
                    MainThread.BeginInvokeOnMainThread(TransitionToWaitingFinger);
                    return false;
                });

                StartEnrollPolling();
            }
            catch (ApiException ex)
            {
                TransitionToError(ex.Message);
            }
        }

        private async Task CaptureBaseLog()
        {
            try
            {
                var data = await ApiService.Get("/esp/data");
                baseLog = data.TryGetProperty("log", out var l) ? (l.GetString() ?? "") : "";
            }
            catch { baseLog = ""; }
        }

        private void ShowEnrollTerminal(string name)
        {
            enrolledMemberName = name;
            currentEnrollState = EnrollState.Initiated;

            TxtEnrollTerminal.Text = $"[ENROLL] Enrollment of {name} started...\n";

            Step1Icon.BackgroundColor = Color.FromArgb("#1E3A10");
            Step1Icon.Stroke = new SolidColorBrush(Color.FromArgb("#1E3A10"));
            Step1Title.TextColor = Color.FromArgb("#DEFF9A");
            Step1Subtitle.Text = $"Command sent for {name}";

            ResetStep2Inactive();
            Step2Title.Text = "Place your finger";
            Step2Subtitle.Text = "Place finger on the sensor";
            ResetStep3Inactive();

            CardAnimationBox.IsVisible = false;
            ResultBanner.IsVisible = false;
            LogDetailBox.IsVisible = false;
            BtnToggleLog.Text = "▶ Technical details";

            EnrollSpinner.IsRunning = true;
            EnrollSpinner.IsVisible = true;

            BtnCloseEnroll.BackgroundColor = Color.FromArgb("#2A2A2A");
            BtnCloseEnroll.TextColor = Color.FromArgb("#AAAAAA");
            BtnCloseEnroll.Text = "CLOSE";

            ModalOverlay.IsVisible = true;
            isPollingEnroll = true;
        }

        private void TransitionToWaitingFinger()
        {
            if (currentEnrollState >= EnrollState.WaitingFinger) return;
            currentEnrollState = EnrollState.WaitingFinger;

            Step2Icon.BackgroundColor = Color.FromArgb("#3A2A10");
            Step2Icon.Stroke = new SolidColorBrush(Color.FromArgb("#FFAA33"));
            Step2IconLabel.Text = "→";
            Step2IconLabel.TextColor = Color.FromArgb("#FFAA33");
            Step2Title.TextColor = Color.FromArgb("#FFAA33");
            Step2Title.Text = "Place your finger on the sensor";
            Step2Subtitle.TextColor = Color.FromArgb("#888888");
            Step2Subtitle.Text = "Place finger on the sensor";

            CardWaitingText.Text = "Waiting for fingerprint...";
            CardAnimationBox.IsVisible = true;
            StartPulseAnimation();
        }

        private void UpdateFingerStatus(string statusText)
        {
            Step2Title.Text = statusText;
            Step2Subtitle.Text = "Follow the instructions";
            CardWaitingText.Text = statusText;
        }

        private async void TransitionToSuccess()
        {
            if (currentEnrollState == EnrollState.Success) return;
            currentEnrollState = EnrollState.Success;
            isPollingEnroll = false;

            // Faza 2: slotul se persista in baza de date doar dupa confirmarea
            // reala a succesului (nu la pornirea inrolarii).
            try
            {
                await ApiService.Post($"/members/{enrollMemberId}/enroll-confirm", new { slot = enrollSlot });
            }
            catch (ApiException ex)
            {
                TransitionToError(ex.Message);
                return;
            }

            EnrollSpinner.IsRunning = false;
            EnrollSpinner.IsVisible = false;
            CardAnimationBox.IsVisible = false;

            Step2Icon.BackgroundColor = Color.FromArgb("#1E3A10");
            Step2Icon.Stroke = new SolidColorBrush(Color.FromArgb("#1E3A10"));
            Step2IconLabel.Text = "✓";
            Step2IconLabel.TextColor = Color.FromArgb("#DEFF9A");
            Step2Title.TextColor = Color.FromArgb("#DEFF9A");
            Step2Title.Text = "Fingerprint recorded";
            Step2Subtitle.TextColor = Color.FromArgb("#666666");
            Step2Subtitle.Text = "Fingerprint saved successfully";

            Step3Icon.BackgroundColor = Color.FromArgb("#1E3A10");
            Step3Icon.Stroke = new SolidColorBrush(Color.FromArgb("#1E3A10"));
            Step3IconLabel.Text = "✓";
            Step3IconLabel.TextColor = Color.FromArgb("#DEFF9A");
            Step3Title.TextColor = Color.FromArgb("#DEFF9A");
            Step3Title.Text = "Enrollment complete";
            Step3Subtitle.TextColor = Color.FromArgb("#666666");
            Step3Subtitle.Text = $"{enrolledMemberName} has been added successfully";

            ResultBanner.IsVisible = true;
            ResultBanner.BackgroundColor = Color.FromArgb("#1A3A10");
            ResultIcon.Text = "✓";
            ResultIcon.TextColor = Color.FromArgb("#DEFF9A");
            ResultText.Text = $"{enrolledMemberName} enrolled successfully";
            ResultText.TextColor = Color.FromArgb("#DEFF9A");

            BtnCloseEnroll.BackgroundColor = Color.FromArgb("#DEFF9A");
            BtnCloseEnroll.TextColor = Color.FromArgb("#121212");
            BtnCloseEnroll.Text = "DONE";

            isPollingEnroll = false;
            LoadMembers();
        }

        private void TransitionToError(string errorMsg)
        {
            if (currentEnrollState == EnrollState.Error) return;
            currentEnrollState = EnrollState.Error;

            EnrollSpinner.IsRunning = false;
            EnrollSpinner.IsVisible = false;
            CardAnimationBox.IsVisible = false;

            Step3Icon.BackgroundColor = Color.FromArgb("#3A1010");
            Step3Icon.Stroke = new SolidColorBrush(Color.FromArgb("#FF5555"));
            Step3IconLabel.Text = "✕";
            Step3IconLabel.TextColor = Color.FromArgb("#FF5555");
            Step3Title.TextColor = Color.FromArgb("#FF5555");
            Step3Title.Text = "Enrollment failed";
            Step3Subtitle.TextColor = Color.FromArgb("#AA5555");
            Step3Subtitle.Text = errorMsg;

            ResultBanner.IsVisible = true;
            ResultBanner.BackgroundColor = Color.FromArgb("#3A1010");
            ResultIcon.Text = "✕";
            ResultIcon.TextColor = Color.FromArgb("#FF5555");
            ResultText.Text = "Enrollment failed";
            ResultText.TextColor = Color.FromArgb("#FF5555");

            BtnCloseEnroll.BackgroundColor = Color.FromArgb("#2A2A2A");
            BtnCloseEnroll.TextColor = Color.FromArgb("#AAAAAA");
            BtnCloseEnroll.Text = "CLOSE";

            isPollingEnroll = false;
        }

        private void ResetStep2Inactive()
        {
            Step2Icon.BackgroundColor = Color.FromArgb("#1A1A1A");
            Step2Icon.Stroke = new SolidColorBrush(Color.FromArgb("#2A2A2A"));
            Step2IconLabel.Text = "2";
            Step2IconLabel.TextColor = Color.FromArgb("#555555");
            Step2Title.TextColor = Color.FromArgb("#555555");
            Step2Subtitle.TextColor = Color.FromArgb("#444444");
        }

        private void ResetStep3Inactive()
        {
            Step3Icon.BackgroundColor = Color.FromArgb("#1A1A1A");
            Step3Icon.Stroke = new SolidColorBrush(Color.FromArgb("#2A2A2A"));
            Step3IconLabel.Text = "3";
            Step3IconLabel.TextColor = Color.FromArgb("#555555");
            Step3Title.TextColor = Color.FromArgb("#555555");
            Step3Title.Text = "Finalizing";
            Step3Subtitle.TextColor = Color.FromArgb("#444444");
            Step3Subtitle.Text = "Waiting...";
        }

        private void StartPulseAnimation()
        {
            Dispatcher.StartTimer(TimeSpan.FromMilliseconds(800), () =>
            {
                if (!isPollingEnroll || currentEnrollState != EnrollState.WaitingFinger)
                    return false;

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    if (CardAnimationBox.IsVisible)
                    {
                        await CardAnimationBox.ScaleTo(1.05, 400, Easing.SinInOut);
                        await CardAnimationBox.ScaleTo(1.0, 400, Easing.SinInOut);
                    }
                });
                return true;
            });
        }

        private void OnToggleLogClicked(object sender, EventArgs e)
        {
            LogDetailBox.IsVisible = !LogDetailBox.IsVisible;
            BtnToggleLog.Text = LogDetailBox.IsVisible ? "▼ Technical details" : "▶ Technical details";
        }

        private void StartEnrollPolling()
        {
            Dispatcher.StartTimer(TimeSpan.FromMilliseconds(1000), () =>
            {
                if (!isPollingEnroll) return false;

                Task.Run(async () =>
                {
                    try
                    {
                        var data = await ApiService.Get("/members/enroll-status");
                        string fullLog = data.TryGetProperty("log", out var l) ? (l.GetString() ?? "") : "";
                        string status = data.TryGetProperty("status", out var s) ? (s.GetString() ?? "") : "";

                        string newLines = fullLog;
                        if (!string.IsNullOrEmpty(baseLog) && fullLog.StartsWith(baseLog))
                            newLines = fullLog.Substring(baseLog.Length).TrimStart('\n', '\r');

                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            string header = $"[ENROLL] Enrollment of {enrolledMemberName} started...\n";
                            if (!string.IsNullOrEmpty(newLines))
                                TxtEnrollTerminal.Text = header + newLines + $"\n[STATUS] {status}";

                            string statusLower = status.ToLower();
                            string logLower = newLines.ToLower();

                            // Firmware-ul ESP raporteaza starea in romana, deci cautam acele cuvinte.
                            if (statusLower.Contains("amprenta salvata") ||
                                (statusLower.Contains("salvat") && statusLower.Contains("succes")))
                            {
                                TransitionToSuccess();
                                return;
                            }

                            if (statusLower.Contains("eroare") || statusLower.Contains("error") || statusLower.Contains("timeout"))
                            {
                                TransitionToError(status);
                                return;
                            }

                            if (statusLower.Contains("pune degetul") && currentEnrollState == EnrollState.Initiated)
                                TransitionToWaitingFinger();

                            if (currentEnrollState == EnrollState.WaitingFinger)
                            {
                                if (statusLower.Contains("ridica degetul"))
                                    UpdateFingerStatus("Lift your finger...");
                                else if (statusLower.Contains("acelasi deget"))
                                    UpdateFingerStatus("Place the same finger again");
                                else if (statusLower.Contains("pune degetul"))
                                    UpdateFingerStatus("Place your finger on the sensor");
                            }

                            if (currentEnrollState == EnrollState.Initiated &&
                                (logLower.Contains("inceputa") || logLower.Contains("începută") || logLower.Contains("started")))
                                TransitionToWaitingFinger();
                        });
                    }
                    catch { }
                });
                return true;
            });
        }

        private void OnCloseOverlay(object sender, EventArgs e)
        {
            ModalOverlay.IsVisible = false;
            isPollingEnroll = false;
        }
    }
}
