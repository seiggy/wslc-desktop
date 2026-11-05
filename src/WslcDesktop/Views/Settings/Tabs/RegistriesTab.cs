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
        var card = InspectUi.Card("Registries", out var rows);
        rows.Spacing = 12;
        rows.Children.Add(InspectUi.Description(
            "Sign in to private registries so images can be pulled. Credentials are handed to wslc and kept in its credential store; "
            + "this app never stores or shows them. WSLc can't list saved credentials, so the list below shows registries you signed in to from here."));
        rows.Children.Add(_list);
        RenderServers();

        var server = new TextBox { Header = "Server (blank for the default registry)", PlaceholderText = "ghcr.io" };
        var user = new TextBox { Header = "Username" };
        var password = new PasswordBox { Header = "Password or access token" };
        var signIn = new Button { Content = "Sign in", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        signIn.Click += async (_, _) =>
        {
            var host = server.Text.Trim();
            if (RegistryAccounts.Validate(user.Text, password.Password) is { } problem)
            {
                report("Missing details", problem, InfoBarSeverity.Warning);
                return;
            }

            signIn.IsEnabled = false;
            try
            {
                await _accounts.SignInAsync(host, user.Text.Trim(), password.Password);
                password.Password = string.Empty;
                RenderServers();
                report("Signed in", host.Length == 0 ? "Signed in to the default registry." : $"Signed in to {host}.", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                report("Couldn't sign in", ex.Message, InfoBarSeverity.Error);
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
            _list.Children.Add(InspectUi.Description("No registries signed in from this app."));
            return;
        }

        foreach (var server in _accounts.Servers.ToList())
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock
            {
                Text = server.Length == 0 ? "Default registry" : server,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            var signOut = new Button { Content = "Sign out" };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(signOut, $"Sign out of {server}");
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
            Report("Signed out", server.Length == 0 ? "Signed out of the default registry." : $"Signed out of {server}.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            Report("Couldn't sign out", ex.Message, InfoBarSeverity.Error);
        }
    }
}
