using WslcDesktop.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WslcDesktop.Controls;
using WslcDesktop.Settings;

namespace WslcDesktop.Views.Settings.Tabs;

/// <summary>Signing in to private registries so images can be pulled.</summary>
public sealed class RegistriesTab : SettingsTab
{
    private readonly RegistryAccounts _accounts = new(
        App.Host.Operations.Registry, App.Host.AppSession, App.Host.Settings, () => App.Host.Settings.Save());

    private readonly StackPanel _list = new() { Spacing = 4 };

    public RegistriesTab(StatusReporter report) : base(report)
    {
        var card = InspectUi.Card(UiStrings.Get("Registries"), out var rows);
        rows.Spacing = 12;
        rows.Children.Add(InspectUi.Description(
            UiStrings.Get("SignInToPrivateRegistriesSoImagesCanBe")));
        rows.Children.Add(_list);
        RenderServers();

        var server = new TextBox { Header = UiStrings.Get("ServerBlankForTheDefaultRegistry"), PlaceholderText = "ghcr.io" };
        var user = new TextBox { Header = UiStrings.Get("Username") };
        var password = new PasswordBox { Header = UiStrings.Get("PasswordOrAccessToken") };
        var signIn = new Button { Content = UiStrings.Get("ActionSignIn"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        signIn.Click += async (_, _) =>
        {
            var host = server.Text.Trim();
            if (RegistryAccounts.Validate(user.Text, password.Password) is { } problem)
            {
                report(UiStrings.Get("MissingDetails"), problem, InfoBarSeverity.Warning);
                return;
            }

            signIn.IsEnabled = false;
            try
            {
                await _accounts.SignInAsync(host, user.Text.Trim(), password.Password);
                password.Password = string.Empty;
                RenderServers();
                report(UiStrings.Get("SignedIn"), host.Length == 0 ? UiStrings.Get("SignedInToTheDefaultRegistry") : UiStrings.Format("SignedInTo", host), InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                report(UiStrings.Get("CouldnTSignIn"), ex.Message, InfoBarSeverity.Error);
            }
            finally
            {
                signIn.IsEnabled = true;
            }
        };

        rows.Children.Add(server);
        rows.Children.Add(user);
        rows.Children.Add(password);
        rows.Children.Add(signIn);
        Children.Add(card);
    }

    private void RenderServers()
    {
        _list.Children.Clear();
        if (_accounts.Servers.Count == 0)
        {
            _list.Children.Add(InspectUi.Description(UiStrings.Get("NoRegistriesSignedInFromThisApp")));
            return;
        }

        foreach (var server in _accounts.Servers.ToList())
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock
            {
                Text = server.Length == 0 ? UiStrings.Get("DefaultRegistry") : server,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            var signOut = new Button { Content = UiStrings.Get("ActionSignOut") };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(signOut, UiStrings.Format("SignOutOf", server));
            signOut.Click += async (_, _) => await SignOutAsync(server);
            Grid.SetColumn(signOut, 1);
            row.Children.Add(signOut);
            _list.Children.Add(row);
        }
    }

    private async Task SignOutAsync(string server)
    {
        try
        {
            await _accounts.SignOutAsync(server);
            RenderServers();
            Report(UiStrings.Get("SignedOut"), server.Length == 0 ? UiStrings.Get("SignedOutOfTheDefaultRegistry") : UiStrings.Format("SignedOutOf", server), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            Report(UiStrings.Get("CouldnTSignOut"), ex.Message, InfoBarSeverity.Error);
        }
    }
}
