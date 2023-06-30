using FramePFX.Core.Editor.Timelines.AudioClips;

namespace FramePFX.Core.Editor.ViewModels.Timeline.Clips {
    public class SinewaveClipViewModel : AudioClipViewModel {
        public new SinewaveClip Model => (SinewaveClip) ((ClipViewModel) this).Model;

        public SinewaveClipViewModel(SinewaveClip model) : base(model) {

        }
    }
}