using System.Diagnostics;
using System.Drawing.Drawing2D;
using League.Models;
using League.Parsers;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Controls;

public partial class PlayerCardControl
{
    private readonly SummonerAvatar _summonerAvatar = new();

    private async Task OpenProfileAsync()
    {
        if (CurrentSummonerId <= 0 || IsDisposed)
        {
            return;
        }

        if (SummaryPanel.FindForm() is FormMain main)
        {
            await main.OpenPlayerProfileAsync(CurrentSummonerId.ToString());
        }
    }

    public async Task LoadSummonerProfileAsync()
    {
        long summonerId = CurrentSummonerId;
        if (summonerId <= 0)
        {
            return;
        }

        try
        {
            var profile = await new PlayerMatchDataFetcher().GetGameNameBySummonerIdAsync(summonerId.ToString());
            if (IsDisposed || CurrentSummonerId != summonerId || profile == null)
            {
                return;
            }

            int level = profile["summonerLevel"]?.Value<int>() ?? 0;
            _summonerAvatar.SetProfile(null, level);
            int iconId = profile["profileIconId"]?.Value<int>() ?? 0;
            using var image = await Profileicon.GetProfileIconAsync(iconId);
            if (IsDisposed || CurrentSummonerId != summonerId)
            {
                return;
            }

            _summonerAvatar.SetProfile(image, level);
        }
        catch (Exception ex)
        {
            AppLog.Shared.Warn(LogCategory.Ui, "Room profile", ex);
        }
    }

    private sealed class SummonerAvatar : Control
    {
        private Image? _image;
        private int _level;

        public SummonerAvatar()
        {
            DoubleBuffered = true;
            Size = new Size(64, 68);
            AccessibleName = "召唤师头像";
        }

        public void SetProfile(Image? image, int level)
        {
            var previous = _image;
            // Keep an owned copy; profile loaders dispose the image they pass to this control.
            _image = image == null ? null : new Bitmap(image);
            previous?.Dispose();
            _level = level;
            AccessibleDescription = level > 0 ? $"等级 {level}" : "等级未知";
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var circle = new Rectangle(6, 4, Width - 12, Width - 12);
            using var ring = new Pen(Theme.Accent, 3);
            g.DrawEllipse(ring, circle);
            circle.Inflate(-4, -4);
            using var clip = new GraphicsPath();
            clip.AddEllipse(circle);
            var state = g.Save();
            g.SetClip(clip);
            if (_image != null)
            {
                g.DrawImage(_image, circle);
            }
            else
            {
                using var background = new SolidBrush(Color.FromArgb(216, 227, 237));
                using var silhouette = new SolidBrush(Color.FromArgb(117, 140, 160));
                g.FillEllipse(background, circle);
                g.FillEllipse(silhouette, Width / 2 - 7, 16, 14, 14);
                g.FillEllipse(silhouette, Width / 2 - 15, 32, 30, 27);
            }
            g.Restore(state);
            if (_level > 0)
            {
                TextRenderer.DrawText(
                    g,
                    _level.ToString(),
                    Font,
                    new Rectangle(0, Height - 16, Width, 16),
                    Theme.Accent,
                    BackColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                );
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _image?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
