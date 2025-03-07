using FramePFX.Core.RBC;
using SkiaSharp;

namespace FramePFX.Core.Editor.ResourceManaging.Resources {
    public class ResourceText : ResourceItem {
        public string Text { get; set; }

        public double FontSize { get; set; }

        public double SkewX { get; set; }

        public string FontFamily { get; set; }

        public SKColor Foreground { get; set; }

        public SKColor Border { get; set; }

        public double BorderThickness { get; set; }

        public bool IsAntiAliased { get; set; }

        public ResourceText() {
            this.FontSize = 40;
            this.FontFamily = "Consolas";
            this.Text = "Text Here";

            this.Foreground = SKColors.White;
            this.Border = SKColors.DarkGray;
            this.BorderThickness = 5d;
            this.IsAntiAliased = true;
        }

        public override void WriteToRBE(RBEDictionary data) {
            base.WriteToRBE(data);
            data.SetString(nameof(this.Text), this.Text);
            data.SetDouble(nameof(this.FontSize), this.FontSize);
            data.SetDouble(nameof(this.SkewX), this.SkewX);
            data.SetString(nameof(this.FontFamily), this.FontFamily);
            data.SetUInt(nameof(this.Foreground), (uint) this.Foreground);
            data.SetUInt(nameof(this.Border), (uint) this.Border);
            data.SetDouble(nameof(this.BorderThickness), this.BorderThickness);
            data.SetBool(nameof(this.IsAntiAliased), this.IsAntiAliased);
        }

        public override void ReadFromRBE(RBEDictionary data) {
            base.ReadFromRBE(data);
            this.Text = data.GetString(nameof(this.Text), null);
            this.FontSize = data.GetDouble(nameof(this.FontSize));
            this.SkewX = data.GetDouble(nameof(this.SkewX));
            this.FontFamily = data.GetString(nameof(this.FontFamily), null);
            this.Foreground = data.GetUInt(nameof(this.Foreground));
            this.Border = data.GetUInt(nameof(this.Border));
            this.BorderThickness = data.GetDouble(nameof(this.BorderThickness));
            this.IsAntiAliased = data.GetBool(nameof(this.IsAntiAliased));
        }
    }
}