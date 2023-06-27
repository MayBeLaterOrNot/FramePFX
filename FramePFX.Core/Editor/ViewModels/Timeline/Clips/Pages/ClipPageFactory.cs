using System;
using FramePFX.Core.PropertyPages;

namespace FramePFX.Core.Editor.ViewModels.Timeline.Clips.Pages {
    public class ClipPageFactory : PropertyPageFactory<ClipViewModel, BaseClipPropertyPageViewModel> {
        public static ClipPageFactory Instance { get; } = new ClipPageFactory();

        private ClipPageFactory() {
            this.RegisterPage<VideoClipViewModel, VideoClipPageViewModel>();
            this.RegisterPage<ShapeClipViewModel, ShapeClipPageViewModel>();
            this.RegisterPage<TextClipViewModel, TextClipPageViewModel>();
            this.RegisterPage<ImageClipViewModel, ImageClipPageViewModel>();
            this.RegisterPage<MediaClipViewModel, MediaClipPageViewModel>();
        }
    }
}