using System.Drawing.Drawing2D;
using System.ComponentModel;
using System.Globalization;
using System.Xml.Linq;

namespace PingRadar
{
    internal sealed class NavButton : Control
    {
        private bool isHovered;
        private bool selected;

        public NavButton(string assetName)
        {
            AssetName = assetName;
            BackColor = Theme.Sidebar;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
        }

        public string AssetName { get; }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Selected
        {
            get => selected;
            set
            {
                selected = value;
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            isHovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            isHovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = ClientRectangle;
            bounds.Inflate(-1, -1);

            if (Selected || isHovered)
            {
                using var background = new SolidBrush(
                    Selected ? Theme.SurfaceRaised : Color.FromArgb(25, 30, 40));
                using var path = RoundedRectangle(bounds, 7);
                e.Graphics.FillPath(background, path);
            }

            if (Selected)
            {
                using var accent = new SolidBrush(Theme.Accent);
                e.Graphics.FillRectangle(accent, 0, 14, 3, Height - 28);
            }

            using var icon = SvgAsset.Render(
                AssetName,
                24,
                Selected ? Theme.TextPrimary : Theme.TextSecondary);
            e.Graphics.DrawImage(icon, (Width - icon.Width) / 2, (Height - icon.Height) / 2);
        }

        private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            var diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class SvgTextButton : Button
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string AssetName { get; set; } = string.Empty;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (string.IsNullOrEmpty(AssetName))
            {
                return;
            }

            using var icon = SvgAsset.Render(AssetName, 16, Enabled ? ForeColor : Theme.TextMuted);
            e.Graphics.DrawImage(icon, 14, (Height - icon.Height) / 2);
        }
    }

    internal sealed class SvgIconControl : Control
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string AssetName { get; set; } = string.Empty;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color IconColor { get; set; } = Theme.TextSecondary;

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var size = Math.Max(1, Math.Min(Width, Height));
            using var icon = SvgAsset.Render(AssetName, size, IconColor);
            e.Graphics.DrawImage(icon, (Width - size) / 2, (Height - size) / 2);
        }
    }

    internal static class SvgAsset
    {
        public static Bitmap Render(string assetName, int size, Color color)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", assetName);
            var result = new Bitmap(size, size);
            using var graphics = Graphics.FromImage(result);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var document = XDocument.Load(path);
            var root = document.Root ?? throw new InvalidDataException($"Invalid SVG: {assetName}");
            var viewBox = ParseNumbers(root.Attribute("viewBox")?.Value ?? "0 0 24 24");
            var scale = size / viewBox[2];
            graphics.ScaleTransform(scale, scale);
            graphics.TranslateTransform(-viewBox[0], -viewBox[1]);

            foreach (var element in root.Elements())
            {
                var strokeWidth = Number(element, "stroke-width", Number(root, "stroke-width", 2));
                using var pen = new Pen(color, strokeWidth)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                    LineJoin = LineJoin.Round
                };
                using var brush = new SolidBrush(color);
                var fill = element.Attribute("fill")?.Value ?? root.Attribute("fill")?.Value;

                switch (element.Name.LocalName)
                {
                    case "line":
                        graphics.DrawLine(
                            pen,
                            Number(element, "x1"),
                            Number(element, "y1"),
                            Number(element, "x2"),
                            Number(element, "y2"));
                        break;
                    case "circle":
                        var cx = Number(element, "cx");
                        var cy = Number(element, "cy");
                        var radius = Number(element, "r");
                        DrawShape(
                            graphics,
                            pen,
                            brush,
                            fill,
                            new RectangleF(cx - radius, cy - radius, radius * 2, radius * 2),
                            true);
                        break;
                    case "polyline":
                    case "polygon":
                        var points = ParseNumbers(element.Attribute("points")?.Value ?? string.Empty);
                        var drawingPoints = Enumerable.Range(0, points.Length / 2)
                            .Select(index => new PointF(points[index * 2], points[index * 2 + 1]))
                            .ToArray();
                        if (drawingPoints.Length > 1)
                        {
                            if (element.Name.LocalName == "polygon")
                            {
                                if (!string.Equals(fill, "none", StringComparison.OrdinalIgnoreCase))
                                {
                                    graphics.FillPolygon(brush, drawingPoints);
                                }
                                graphics.DrawPolygon(pen, drawingPoints);
                            }
                            else
                            {
                                graphics.DrawLines(pen, drawingPoints);
                            }
                        }
                        break;
                }
            }

            return result;
        }

        private static void DrawShape(
            Graphics graphics,
            Pen pen,
            Brush brush,
            string? fill,
            RectangleF bounds,
            bool ellipse)
        {
            if (!string.Equals(fill, "none", StringComparison.OrdinalIgnoreCase))
            {
                if (ellipse)
                {
                    graphics.FillEllipse(brush, bounds);
                }
            }

            if (ellipse)
            {
                graphics.DrawEllipse(pen, bounds);
            }
        }

        private static float Number(XElement element, string name, float fallback = 0)
        {
            return float.TryParse(
                element.Attribute(name)?.Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value)
                ? value
                : fallback;
        }

        private static float[] ParseNumbers(string value)
        {
            return value.Replace(',', ' ')
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => float.Parse(part, CultureInfo.InvariantCulture))
                .ToArray();
        }
    }

    internal sealed class LogoControl : Control
    {
        public LogoControl()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint,
                true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var center = new PointF(Width / 2F, Height / 2F);

            using var outerPen = new Pen(Theme.Accent, 2F);
            using var innerPen = new Pen(Color.FromArgb(130, Theme.Accent), 1.5F);
            using var dotBrush = new SolidBrush(Theme.Accent);
            e.Graphics.DrawEllipse(outerPen, center.X - 15, center.Y - 15, 30, 30);
            e.Graphics.DrawArc(innerPen, center.X - 9, center.Y - 9, 18, 18, 205, 250);
            e.Graphics.FillEllipse(dotBrush, center.X - 3, center.Y - 3, 6, 6);
        }
    }

    internal sealed class StatusDot : Control
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color DotColor { get; set; } = Theme.TextMuted;

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(DotColor);
            e.Graphics.FillEllipse(brush, ClientRectangle);
        }
    }

    internal sealed class ServerCard : Control
    {
        private const int MaximumHistorySamples = 30;
        private readonly ServerDefinition server;
        private readonly List<long?> pingHistory = [];
        private long? latency;
        private bool checking = true;

        public ServerCard(ServerDefinition server)
        {
            this.server = server;
            BackColor = Theme.Surface;
            MinimumSize = new Size(220, 210);
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
        }

        public void SetChecking()
        {
            checking = true;
            Invalidate();
        }

        public void SetResult(long? milliseconds)
        {
            checking = false;
            latency = milliseconds;
            pingHistory.Add(milliseconds);
            if (pingHistory.Count > MaximumHistorySamples)
            {
                pingHistory.RemoveAt(0);
            }

            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var bounds = ClientRectangle;
            bounds.Width -= 1;
            bounds.Height -= 1;
            using var surface = new SolidBrush(Theme.Surface);
            using var border = new Pen(Theme.Border);
            e.Graphics.FillRectangle(surface, bounds);
            e.Graphics.DrawRectangle(border, bounds);

            using var codeBackground = new SolidBrush(Theme.SurfaceRaised);
            e.Graphics.FillRectangle(codeBackground, 18, 16, 45, 23);

            using var codeFont = new Font("Segoe UI Semibold", 8F);
            using var nameFont = new Font("Segoe UI Semibold", 13F);
            using var cityFont = new Font("Segoe UI", 9F);
            using var pingFont = new Font("Segoe UI Semibold", 27F);
            using var unitFont = new Font("Segoe UI Semibold", 10F);
            using var statusFont = new Font("Segoe UI", 8.5F);
            using var primaryBrush = new SolidBrush(Theme.TextPrimary);
            using var secondaryBrush = new SolidBrush(Theme.TextSecondary);
            using var mutedBrush = new SolidBrush(Theme.TextMuted);

            e.Graphics.DrawString(server.Code, codeFont, secondaryBrush, new PointF(27, 20));
            e.Graphics.DrawString(server.Name, nameFont, primaryBrush, new PointF(18, 47));
            e.Graphics.DrawString(server.City, cityFont, mutedBrush, new PointF(19, 74));

            var statusColor = GetStatusColor();
            using var statusBrush = new SolidBrush(statusColor);
            e.Graphics.FillEllipse(statusBrush, Width - 29, 23, 8, 8);

            var pingText = checking ? "—" : latency?.ToString() ?? "—";
            var pingY = 91F;
            e.Graphics.DrawString(pingText, pingFont, primaryBrush, new PointF(17, pingY));
            var pingWidth = e.Graphics.MeasureString(pingText, pingFont).Width;
            e.Graphics.DrawString("ms", unitFont, secondaryBrush, new PointF(18 + pingWidth, pingY + 18));

            var statusText = checking
                ? "Checking"
                : latency is null
                    ? "Unreachable"
                    : latency < 70
                        ? "Excellent"
                        : latency < 95
                            ? "Playable"
                            : "High latency";
            var statusSize = e.Graphics.MeasureString(statusText, statusFont);
            e.Graphics.DrawString(
                statusText,
                statusFont,
                statusBrush,
                new PointF(Width - statusSize.Width - 18, pingY + 24));

            DrawHistoryGraph(e.Graphics, statusColor);
        }

        private void DrawHistoryGraph(Graphics graphics, Color lineColor)
        {
            var graphBounds = new RectangleF(18, 147, Width - 36, Height - 163);
            if (graphBounds.Width < 40 || graphBounds.Height < 24)
            {
                return;
            }

            using var gridPen = new Pen(Color.FromArgb(38, 47, 60), 1F);
            for (var row = 0; row <= 2; row++)
            {
                var y = graphBounds.Top + graphBounds.Height * row / 2F;
                graphics.DrawLine(gridPen, graphBounds.Left, y, graphBounds.Right, y);
            }

            if (pingHistory.Count < 2)
            {
                using var waitingFont = new Font("Segoe UI", 8F);
                using var waitingBrush = new SolidBrush(Theme.TextMuted);
                graphics.DrawString(
                    "Collecting history...",
                    waitingFont,
                    waitingBrush,
                    graphBounds.Left,
                    graphBounds.Top + graphBounds.Height / 2F - 7);
                return;
            }

            var successfulSamples = pingHistory.Where(sample => sample.HasValue)
                .Select(sample => sample!.Value)
                .ToArray();
            if (successfulSamples.Length == 0)
            {
                return;
            }

            var minimum = Math.Max(0, successfulSamples.Min() - 10);
            var maximum = Math.Max(minimum + 30, successfulSamples.Max() + 10);
            var stepX = graphBounds.Width / Math.Max(1, MaximumHistorySamples - 1);
            var startX = graphBounds.Right - stepX * (pingHistory.Count - 1);
            var segments = new List<List<PointF>>();
            var currentSegment = new List<PointF>();

            for (var index = 0; index < pingHistory.Count; index++)
            {
                var sample = pingHistory[index];
                if (!sample.HasValue)
                {
                    if (currentSegment.Count > 0)
                    {
                        segments.Add(currentSegment);
                        currentSegment = [];
                    }

                    continue;
                }

                var normalized = (sample.Value - minimum) / (float)(maximum - minimum);
                currentSegment.Add(new PointF(
                    startX + index * stepX,
                    graphBounds.Bottom - normalized * graphBounds.Height));
            }

            if (currentSegment.Count > 0)
            {
                segments.Add(currentSegment);
            }

            using var linePen = new Pen(lineColor, 2F)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            foreach (var segment in segments)
            {
                if (segment.Count >= 2)
                {
                    graphics.DrawLines(linePen, segment.ToArray());
                }
                else
                {
                    using var pointBrush = new SolidBrush(lineColor);
                    graphics.FillEllipse(
                        pointBrush,
                        segment[0].X - 2,
                        segment[0].Y - 2,
                        4,
                        4);
                }
            }
        }

        private Color GetStatusColor()
        {
            if (checking)
            {
                return Theme.TextMuted;
            }

            return latency switch
            {
                null => Theme.Bad,
                < 60 => Theme.Good,
                < 120 => Theme.Medium,
                _ => Theme.Bad
            };
        }
    }
}
