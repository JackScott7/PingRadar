using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace PingRadar
{
    public partial class PingRadar : Form
    {
        private static readonly RelayRegion[] Regions =
        [
            new("UAE", "Dubai", "DXB", "dxb"),
            new("Stockholm", "Sweden", "STO", "sto"),
            new("Helsinki", "Finland", "HEL", "hel"),
            new("Frankfurt", "Germany", "FRA", "fra"),
            new("Vienna", "Austria", "VIE", "vie"),
            new("Amsterdam", "Netherlands", "AMS", "ams"),
            new("Warsaw", "Poland", "WAW", "waw")
        ];

        private static readonly HttpClient SteamClient = new()
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        private const string SteamConfigUrl =
            "https://api.steampowered.com/ISteamApps/GetSDRConfig/v1/?appid=730";
        private static readonly TimeSpan ServerAddressRefreshInterval = TimeSpan.FromMinutes(1);
        private readonly Dictionary<string, ServerCard> serverCards = [];
        private readonly List<ServerDefinition> serverCache = [];
        private readonly System.Windows.Forms.Timer refreshTimer = new();
        private readonly Panel contentPanel = new();
        private readonly Panel homePage = new();
        private readonly Panel settingsPage = new();
        private readonly Label pageTitle = new();
        private readonly Label pageSubtitle = new();
        private readonly NavButton homeButton = new("home.svg");
        private readonly NavButton settingsButton = new("settings.svg");
        private readonly NavButton aboutButton = new("circle-user-round.svg");
        private readonly SvgTextButton refreshButton = new();
        private readonly Label lastUpdatedLabel = new();
        private readonly CheckBox autoRefreshCheckBox = new();
        private readonly NumericUpDown intervalInput = new();
        private readonly FlowLayoutPanel serverGrid = new();
        private readonly Label emptyStateLabel = new();
        private CancellationTokenSource? pingCancellation;
        private DateTimeOffset lastServerAddressRefresh = DateTimeOffset.MinValue;
        private bool refreshInProgress;

        public PingRadar()
        {
            InitializeComponent();
            BuildInterface();

            refreshTimer.Interval = 30_000;
            refreshTimer.Tick += async (_, _) => await RefreshAllAsync();
            Shown += async (_, _) =>
            {
                EnableDarkTitleBar();
                refreshTimer.Start();
                await RefreshAllAsync();
            };
        }

        private void BuildInterface()
        {
            var sidebar = BuildSidebar();
            Controls.Add(contentPanel);
            Controls.Add(sidebar);

            contentPanel.Dock = DockStyle.Fill;
            contentPanel.Padding = new Padding(42, 34, 42, 32);
            contentPanel.BackColor = Theme.Background;

            BuildHomePage();
            BuildSettingsPage();

            contentPanel.Controls.Add(homePage);
            contentPanel.Controls.Add(settingsPage);
            ShowPage(homePage);
        }

        private Control BuildSidebar()
        {
            var sidebar = new Panel
            {
                Dock = DockStyle.Left,
                Width = 88,
                BackColor = Theme.Sidebar,
                Padding = new Padding(14, 24, 14, 20)
            };

            var logo = new LogoControl
            {
                Dock = DockStyle.Top,
                Height = 48
            };

            var nav = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 150,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(0, 20, 0, 0),
                BackColor = Color.Transparent
            };

            ConfigureNavButton(homeButton, "Home", (_, _) => ShowPage(homePage));
            ConfigureNavButton(settingsButton, "Settings", (_, _) => ShowPage(settingsPage));
            ConfigureNavButton(aboutButton, "About", (_, _) =>
            {
                using var about = new AboutForm();
                about.ShowDialog(this);
            });
            nav.Controls.Add(homeButton);
            nav.Controls.Add(settingsButton);

            var bottomNav = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 62,
                BackColor = Color.Transparent
            };
            aboutButton.Location = new Point(0, 5);
            bottomNav.Controls.Add(aboutButton);

            var version = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 24,
                Text = "v1.0",
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Theme.TextMuted,
                Font = new Font("Segoe UI", 8F)
            };

            sidebar.Controls.Add(nav);
            sidebar.Controls.Add(logo);
            sidebar.Controls.Add(bottomNav);
            sidebar.Controls.Add(version);
            return sidebar;
        }

        private static void ConfigureNavButton(NavButton button, string tooltip, EventHandler onClick)
        {
            button.Size = new Size(60, 52);
            button.Margin = new Padding(0, 0, 0, 10);
            button.Cursor = Cursors.Hand;
            button.Click += onClick;
            new ToolTip
            {
                InitialDelay = 300,
                ReshowDelay = 100,
                AutoPopDelay = 4_000
            }.SetToolTip(button, tooltip);
        }

        private void BuildHomePage()
        {
            homePage.Dock = DockStyle.Fill;
            homePage.BackColor = Theme.Background;

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 96,
                BackColor = Color.Transparent
            };

            pageTitle.Text = "Valve Relay Latency";
            pageTitle.Font = new Font("Segoe UI Semibold", 22F);
            pageTitle.ForeColor = Theme.TextPrimary;
            pageTitle.AutoSize = true;
            pageTitle.Location = new Point(0, 0);

            pageSubtitle.Text = "Counter Strike 2 Relay Latency, Sorted from low to high ping";
            pageSubtitle.Font = new Font("Segoe UI", 10F);
            pageSubtitle.ForeColor = Theme.TextSecondary;
            pageSubtitle.AutoSize = true;
            pageSubtitle.Location = new Point(2, 43);

            refreshButton.Text = "      Refresh";
            refreshButton.AssetName = "refresh-cw.svg";
            refreshButton.Size = new Size(112, 40);
            refreshButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            refreshButton.Location = new Point(header.Width - refreshButton.Width, 4);
            refreshButton.FlatStyle = FlatStyle.Flat;
            refreshButton.FlatAppearance.BorderSize = 1;
            refreshButton.FlatAppearance.BorderColor = Theme.Border;
            refreshButton.BackColor = Theme.Surface;
            refreshButton.ForeColor = Theme.TextPrimary;
            refreshButton.Font = new Font("Segoe UI Semibold", 9F);
            refreshButton.Cursor = Cursors.Hand;
            refreshButton.Click += async (_, _) => await RefreshAllAsync();
            header.Resize += (_, _) =>
                refreshButton.Left = Math.Max(0, header.ClientSize.Width - refreshButton.Width);

            header.Controls.Add(pageTitle);
            header.Controls.Add(pageSubtitle);
            header.Controls.Add(refreshButton);

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 38,
                BackColor = Color.Transparent
            };

            var liveIndicator = new StatusDot
            {
                Location = new Point(1, 13),
                Size = new Size(8, 8),
                DotColor = Theme.Good
            };

            lastUpdatedLabel.Text = "Waiting for first check";
            lastUpdatedLabel.ForeColor = Theme.TextMuted;
            lastUpdatedLabel.Font = new Font("Segoe UI", 8.5F);
            lastUpdatedLabel.AutoSize = true;
            lastUpdatedLabel.Location = new Point(18, 8);
            footer.Controls.Add(liveIndicator);
            footer.Controls.Add(lastUpdatedLabel);

            serverGrid.Dock = DockStyle.Fill;
            serverGrid.AutoScroll = true;
            serverGrid.WrapContents = true;
            serverGrid.FlowDirection = FlowDirection.LeftToRight;
            serverGrid.BackColor = Color.Transparent;
            serverGrid.Padding = new Padding(0, 0, 8, 12);
            serverGrid.Visible = false;
            serverGrid.Resize += (_, _) => ResizeServerCards();

            emptyStateLabel.Text = "Checking nearby Steam relays...";
            emptyStateLabel.ForeColor = Theme.TextMuted;
            emptyStateLabel.Font = new Font("Segoe UI", 10F);
            emptyStateLabel.TextAlign = ContentAlignment.MiddleCenter;
            emptyStateLabel.Dock = DockStyle.Fill;

            homePage.Controls.Add(serverGrid);
            homePage.Controls.Add(emptyStateLabel);
            homePage.Controls.Add(footer);
            homePage.Controls.Add(header);
        }

        private void ResizeServerCards()
        {
            const int gap = 18;
            const int preferredCardWidth = 260;
            var availableWidth = serverGrid.ClientSize.Width -
                serverGrid.Padding.Horizontal -
                SystemInformation.VerticalScrollBarWidth;
            var columnCount = Math.Clamp(
                (availableWidth + gap) / (preferredCardWidth + gap),
                2,
                4);
            var cardWidth = Math.Max(
                220,
                (availableWidth - gap * columnCount) / columnCount);

            foreach (Control card in serverGrid.Controls)
            {
                card.Width = cardWidth;
            }
        }

        private void BuildSettingsPage()
        {
            settingsPage.Dock = DockStyle.Fill;
            settingsPage.BackColor = Theme.Background;

            var title = new Label
            {
                Dock = DockStyle.Top,
                Height = 54,
                Text = "Settings",
                Font = new Font("Segoe UI Semibold", 22F),
                ForeColor = Theme.TextPrimary
            };

            var subtitle = new Label
            {
                Dock = DockStyle.Top,
                Height = 52,
                Text = "Choose how often server latency is checked.",
                Font = new Font("Segoe UI", 10F),
                ForeColor = Theme.TextSecondary
            };

            var settingsSurface = new Panel
            {
                Dock = DockStyle.Top,
                Height = 178,
                BackColor = Theme.Surface,
                Padding = new Padding(24)
            };
            settingsSurface.Paint += (_, e) =>
            {
                using var pen = new Pen(Theme.Border);
                e.Graphics.DrawRectangle(
                    pen,
                    0,
                    0,
                    settingsSurface.ClientSize.Width - 1,
                    settingsSurface.ClientSize.Height - 1);
            };

            var autoLabel = new Label
            {
                Text = "Automatic refresh",
                ForeColor = Theme.TextPrimary,
                Font = new Font("Segoe UI Semibold", 10F),
                AutoSize = true,
                Location = new Point(24, 24)
            };
            var autoHint = new Label
            {
                Text = "Keep latency readings up to date in the background",
                ForeColor = Theme.TextMuted,
                AutoSize = true,
                Location = new Point(24, 50)
            };

            autoRefreshCheckBox.Appearance = Appearance.Button;
            autoRefreshCheckBox.Checked = true;
            autoRefreshCheckBox.Text = "ON";
            autoRefreshCheckBox.TextAlign = ContentAlignment.MiddleCenter;
            autoRefreshCheckBox.FlatStyle = FlatStyle.Flat;
            autoRefreshCheckBox.FlatAppearance.BorderSize = 0;
            autoRefreshCheckBox.BackColor = Theme.Accent;
            autoRefreshCheckBox.ForeColor = Color.White;
            autoRefreshCheckBox.Size = new Size(54, 28);
            autoRefreshCheckBox.Location = new Point(395, 27);
            autoRefreshCheckBox.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            autoRefreshCheckBox.CheckedChanged += (_, _) =>
            {
                autoRefreshCheckBox.Text = autoRefreshCheckBox.Checked ? "ON" : "OFF";
                autoRefreshCheckBox.BackColor =
                    autoRefreshCheckBox.Checked ? Theme.Accent : Theme.SurfaceRaised;
                refreshTimer.Enabled = autoRefreshCheckBox.Checked;
            };

            var intervalLabel = new Label
            {
                Text = "Refresh interval",
                ForeColor = Theme.TextPrimary,
                Font = new Font("Segoe UI Semibold", 10F),
                AutoSize = true,
                Location = new Point(24, 104)
            };
            var secondsLabel = new Label
            {
                Text = "seconds",
                ForeColor = Theme.TextSecondary,
                AutoSize = true,
                Location = new Point(386, 111),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            intervalInput.Minimum = 2;
            intervalInput.Maximum = 60;
            intervalInput.Value = 30;
            intervalInput.Size = new Size(64, 30);
            intervalInput.Location = new Point(314, 105);
            intervalInput.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            intervalInput.BackColor = Theme.SurfaceRaised;
            intervalInput.ForeColor = Theme.TextPrimary;
            intervalInput.BorderStyle = BorderStyle.FixedSingle;
            intervalInput.ValueChanged += (_, _) =>
                refreshTimer.Interval = (int)intervalInput.Value * 1_000;

            settingsSurface.Resize += (_, _) =>
            {
                autoRefreshCheckBox.Left =
                    settingsSurface.ClientSize.Width - autoRefreshCheckBox.Width - 24;
                secondsLabel.Left =
                    settingsSurface.ClientSize.Width - secondsLabel.Width - 24;
                intervalInput.Left = secondsLabel.Left - intervalInput.Width - 8;
            };

            settingsSurface.Controls.Add(autoLabel);
            settingsSurface.Controls.Add(autoHint);
            settingsSurface.Controls.Add(autoRefreshCheckBox);
            settingsSurface.Controls.Add(intervalLabel);
            settingsSurface.Controls.Add(intervalInput);
            settingsSurface.Controls.Add(secondsLabel);

            settingsPage.Controls.Add(settingsSurface);
            settingsPage.Controls.Add(subtitle);
            settingsPage.Controls.Add(title);
        }

        private void ShowPage(Control page)
        {
            homePage.Visible = page == homePage;
            settingsPage.Visible = page == settingsPage;
            homeButton.Selected = page == homePage;
            settingsButton.Selected = page == settingsPage;
            page.BringToFront();
        }

        private async Task RefreshAllAsync()
        {
            if (refreshInProgress)
            {
                return;
            }

            refreshInProgress = true;
            refreshButton.Enabled = false;
                refreshButton.Text = "      Checking...";

            pingCancellation?.Cancel();
            pingCancellation?.Dispose();
            pingCancellation = new CancellationTokenSource();

            foreach (ServerCard card in serverGrid.Controls)
            {
                card.SetChecking();
            }

            try
            {
                var servers = await GetServersAsync(pingCancellation.Token);
                var results = await Task.WhenAll(
                    servers.Select(server => PingServerAsync(server, pingCancellation.Token)));
                ShowServers(results);
                lastUpdatedLabel.Text =
                    $"Updated {DateTime.Now:h:mm:ss tt}  ·  Next check in {(int)intervalInput.Value}s";
            }
            finally
            {
                refreshInProgress = false;
                refreshButton.Enabled = true;
                refreshButton.Text = "      Refresh";
            }
        }

        private async Task<IReadOnlyList<ServerDefinition>> GetServersAsync(
            CancellationToken token)
        {
            if (serverCache.Count > 0 &&
                DateTimeOffset.UtcNow - lastServerAddressRefresh <
                ServerAddressRefreshInterval)
            {
                return serverCache.ToArray();
            }

            lastServerAddressRefresh = DateTimeOffset.UtcNow;
            try
            {
                using var response = await SteamClient.GetAsync(SteamConfigUrl, token);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(token);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);

                var pops = document.RootElement.GetProperty("pops");
                var fetchedServers = new List<ServerDefinition>();
                foreach (var region in Regions)
                {
                    if (!pops.TryGetProperty(region.RelayCode, out var pop) ||
                        !pop.TryGetProperty("relays", out var relays))
                    {
                        continue;
                    }

                    string? address = null;
                    foreach (var relay in relays.EnumerateArray())
                    {
                        if (relay.TryGetProperty("ipv4", out var ipv4) &&
                            ipv4.GetString() is { Length: > 0 } relayAddress)
                        {
                            address = relayAddress;
                            break;
                        }
                    }

                    if (address is null)
                    {
                        continue;
                    }

                    fetchedServers.Add(new ServerDefinition(
                        region.Name,
                        region.City,
                        region.Code,
                        region.RelayCode,
                        address));
                }

                if (fetchedServers.Count > 0)
                {
                    serverCache.Clear();
                    serverCache.AddRange(fetchedServers);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                // Keep using the most recent addresses successfully returned by Steam.
            }
            catch (JsonException)
            {
                // A malformed response must not replace the last valid relay data.
            }

            return serverCache.ToArray();
        }

        private async Task<ServerPingResult> PingServerAsync(
            ServerDefinition server,
            CancellationToken token)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(server.Address, 2_000).WaitAsync(token);
                return new ServerPingResult(
                    server,
                    reply.Status == IPStatus.Success ? reply.RoundtripTime : null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (PingException)
            {
                return new ServerPingResult(server, null);
            }
        }

        private void ShowServers(IEnumerable<ServerPingResult> results)
        {
            var serverResults = results
                .OrderBy(result => result.Latency ?? long.MaxValue)
                .ThenBy(result => result.Server.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            serverGrid.SuspendLayout();
            serverGrid.Controls.Clear();

            foreach (var result in serverResults)
            {
                if (!serverCards.TryGetValue(result.Server.RelayCode, out var card))
                {
                    card = new ServerCard(result.Server)
                    {
                        Height = 220,
                        Margin = new Padding(0, 0, 18, 18)
                    };
                    serverCards[result.Server.RelayCode] = card;
                }

                card.SetResult(result.Latency);
                serverGrid.Controls.Add(card);
            }

            emptyStateLabel.Text = "Steam relay data is currently unavailable.";
            emptyStateLabel.Visible = serverResults.Length == 0;
            serverGrid.Visible = serverResults.Length > 0;
            ResizeServerCards();
            serverGrid.ResumeLayout();
        }

        private void EnableDarkTitleBar()
        {
            if (Environment.OSVersion.Version.Major < 10)
            {
                return;
            }

            var enabled = 1;
            _ = DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int));
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr windowHandle,
            int attribute,
            ref int attributeValue,
            int attributeSize);
    }

    internal sealed record ServerDefinition(
        string Name,
        string City,
        string Code,
        string RelayCode,
        string Address);

    internal sealed record RelayRegion(
        string Name,
        string City,
        string Code,
        string RelayCode);

    internal sealed record ServerPingResult(ServerDefinition Server, long? Latency);

    internal static class Theme
    {
        public static readonly Color Background = Color.FromArgb(13, 16, 22);
        public static readonly Color Sidebar = Color.FromArgb(17, 21, 29);
        public static readonly Color Surface = Color.FromArgb(22, 27, 36);
        public static readonly Color SurfaceRaised = Color.FromArgb(31, 37, 48);
        public static readonly Color Border = Color.FromArgb(48, 56, 70);
        public static readonly Color Accent = Color.FromArgb(72, 122, 255);
        public static readonly Color TextPrimary = Color.FromArgb(241, 244, 249);
        public static readonly Color TextSecondary = Color.FromArgb(163, 174, 193);
        public static readonly Color TextMuted = Color.FromArgb(103, 114, 133);
        public static readonly Color Good = Color.FromArgb(57, 210, 139);
        public static readonly Color Medium = Color.FromArgb(255, 184, 77);
        public static readonly Color Bad = Color.FromArgb(255, 91, 110);
    }
}
