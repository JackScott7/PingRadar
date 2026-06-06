using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;

namespace PingRadar
{
    internal sealed class AboutForm : Form
    {
        private const string GitHubProfile = "https://github.com/JackScott7";
        private const string TelegramProfile = "https://t.me/JackScott";
        private const string XProfile = "https://x.com/jack_scott87";
        private const string Repository = "https://github.com/JackScott7/PingRadar/";
        private static readonly TimeSpan AvatarCacheLifetime = TimeSpan.FromDays(1);
        private static readonly HttpClient ImageClient = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        private static readonly string AvatarCachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PingRadar",
            "Cache",
            "JackScott7-avatar.png");

        private readonly CircularAvatar avatar = new();

        public AboutForm()
        {
            Text = "About PingRadar";
            ClientSize = new Size(430, 510);
            BackColor = Theme.Background;
            ForeColor = Theme.TextPrimary;
            Font = new Font("Segoe UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent;

            BuildInterface();
            Shown += async (_, _) =>
            {
                EnableDarkTitleBar();
                await LoadAvatarAsync();
            };
        }

        private void BuildInterface()
        {
            avatar.Size = new Size(112, 112);
            avatar.Location = new Point((ClientSize.Width - avatar.Width) / 2, 32);
            avatar.BackColor = Theme.SurfaceRaised;

            var name = new LinkLabel
            {
                Text = "Jack Scott",
                LinkColor = Theme.TextPrimary,
                ActiveLinkColor = Theme.Accent,
                VisitedLinkColor = Theme.TextPrimary,
                Font = new Font("Segoe UI Semibold", 18F),
                AutoSize = true,
                Location = new Point(0, 158),
                Cursor = Cursors.Hand
            };
            name.Left = (ClientSize.Width - name.PreferredWidth) / 2;
            name.LinkClicked += (_, _) => OpenLink(GitHubProfile);

            var subtitle = new Label
            {
                Text = $"PingRadar  v{GetAppVersion()}",
                ForeColor = Theme.TextMuted,
                AutoSize = true,
                Location = new Point(0, 196)
            };
            subtitle.Left = (ClientSize.Width - subtitle.PreferredWidth) / 2;

            Controls.Add(avatar);
            Controls.Add(name);
            Controls.Add(subtitle);
            Controls.Add(CreateSocialRow("telegram.svg", "JackScott", TelegramProfile, 242));
            Controls.Add(CreateSocialRow("x.svg", "@jack_scott87", XProfile, 298));

            var separator = new Panel
            {
                BackColor = Theme.Border,
                Bounds = new Rectangle(38, 365, ClientSize.Width - 76, 1)
            };
            Controls.Add(separator);

            var repoLabel = new Label
            {
                Text = "Repository",
                ForeColor = Theme.TextMuted,
                AutoSize = true,
                Location = new Point(38, 389)
            };
            var repoLink = CreateLink(
                "github.com/JackScott7/PingRadar",
                Repository,
                new Point(38, 415));
            Controls.Add(repoLabel);
            Controls.Add(repoLink);
        }

        private static string GetAppVersion()
        {
            return Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion.Split('+')[0]
                ?? Application.ProductVersion;
        }

        private Control CreateSocialRow(string asset, string text, string url, int top)
        {
            var panel = new Panel
            {
                BackColor = Theme.Surface,
                Bounds = new Rectangle(38, top, ClientSize.Width - 76, 42)
            };
            var icon = new SvgIconControl
            {
                AssetName = asset,
                IconColor = Theme.TextSecondary,
                Bounds = new Rectangle(14, 11, 20, 20)
            };
            var link = CreateLink(text, url, new Point(48, 10));
            panel.Controls.Add(icon);
            panel.Controls.Add(link);
            return panel;
        }

        private static LinkLabel CreateLink(string text, string url, Point location)
        {
            var link = new LinkLabel
            {
                Text = text,
                LinkColor = Theme.TextPrimary,
                ActiveLinkColor = Theme.Accent,
                VisitedLinkColor = Theme.TextPrimary,
                AutoSize = true,
                Location = location,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI Semibold", 10F)
            };
            link.LinkClicked += (_, _) => OpenLink(url);
            return link;
        }

        private async Task LoadAvatarAsync()
        {
            try
            {
                var bytes = await GetAvatarBytesAsync();
                using var stream = new MemoryStream(bytes);
                using var image = Image.FromStream(stream);
                avatar.Avatar = new Bitmap(image);
            }
            catch (HttpRequestException)
            {
                avatar.Invalidate();
            }
            catch (OperationCanceledException)
            {
                avatar.Invalidate();
            }
        }

        private static async Task<byte[]> GetAvatarBytesAsync()
        {
            if (File.Exists(AvatarCachePath) &&
                DateTime.UtcNow - File.GetLastWriteTimeUtc(AvatarCachePath) <
                AvatarCacheLifetime)
            {
                return await File.ReadAllBytesAsync(AvatarCachePath);
            }

            try
            {
                var bytes = await ImageClient.GetByteArrayAsync(
                    "https://github.com/JackScott7.png?size=224");
                var cacheDirectory = Path.GetDirectoryName(AvatarCachePath)!;
                Directory.CreateDirectory(cacheDirectory);
                await File.WriteAllBytesAsync(AvatarCachePath, bytes);
                return bytes;
            }
            catch when (File.Exists(AvatarCachePath))
            {
                // An expired avatar is preferable to an empty dialog while offline.
                return await File.ReadAllBytesAsync(AvatarCachePath);
            }
        }

        private static void OpenLink(string url)
        {
            Process.Start(new ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
        }

        private void EnableDarkTitleBar()
        {
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

    internal sealed class CircularAvatar : Control
    {
        private Image? avatar;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Image? Avatar
        {
            get => avatar;
            set
            {
                avatar?.Dispose();
                avatar = value;
                Invalidate();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                avatar?.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var clip = new GraphicsPath();
            clip.AddEllipse(ClientRectangle);
            e.Graphics.SetClip(clip);

            if (avatar is not null)
            {
                e.Graphics.DrawImage(avatar, ClientRectangle);
            }
            else
            {
                using var background = new SolidBrush(Theme.SurfaceRaised);
                e.Graphics.FillEllipse(background, ClientRectangle);
                using var icon = SvgAsset.Render(
                    "circle-user-round.svg",
                    Math.Min(Width, Height) / 2,
                    Theme.TextMuted);
                e.Graphics.DrawImage(
                    icon,
                    (Width - icon.Width) / 2,
                    (Height - icon.Height) / 2);
            }

            e.Graphics.ResetClip();
            using var border = new Pen(Theme.Border, 2F);
            e.Graphics.DrawEllipse(border, 1, 1, Width - 3, Height - 3);
        }
    }
}
