namespace PingRadar
{
    partial class PingRadar
    {
        private System.ComponentModel.IContainer components = null!;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                components?.Dispose();
                pingCancellation?.Cancel();
                pingCancellation?.Dispose();
            }

            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(13, 16, 22);
            ClientSize = new Size(1024, 680);
            DoubleBuffered = true;
            Font = new Font("Segoe UI", 9F);
            MinimumSize = new Size(820, 560);
            Name = "PingRadar";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "PingRadar";
            ResumeLayout(false);
        }
    }
}
