using System.Numerics;
using FramePFX.Core.Editor.Timeline.Clip;
using FramePFX.Core.Utils;

namespace FramePFX.Core.Editor.ViewModels.Timeline.Clips {
    public class VideoClipViewModel : ClipViewModel {
        private float bothPos;
        private float bothScale;
        private float bothScaleOrigin;

        public new VideoClipModel Model => (VideoClipModel) base.Model;

        public float MediaPositionX {
            get => this.MediaPosition.X;
            set => this.MediaPosition = new Vector2(value, this.MediaPosition.Y);
        }

        public float MediaPositionY {
            get => this.MediaPosition.Y;
            set => this.MediaPosition = new Vector2(this.MediaPosition.X, value);
        }

        /// <summary>
        /// The x and y coordinates of the video's media
        /// </summary>
        public Vector2 MediaPosition {
            get => this.Model.MediaPosition;
            set {
                this.Model.MediaPosition = value;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(this.MediaPositionX));
                this.RaisePropertyChanged(nameof(this.MediaPositionY));
                this.OnInvalidateRender();
            }
        }

        public float MediaScaleX {
            get => this.MediaScale.X;
            set => this.MediaScale = new Vector2(value, this.MediaScale.Y);
        }

        public float MediaScaleY {
            get => this.MediaScale.Y;
            set => this.MediaScale = new Vector2(this.MediaScale.X, value);
        }

        /// <summary>
        /// The x and y scale of the video's media (relative to <see cref="MediaScaleOrigin"/>)
        /// </summary>
        public Vector2 MediaScale {
            get => this.Model.MediaScale;
            set {
                this.Model.MediaScale = value;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(this.MediaScaleX));
                this.RaisePropertyChanged(nameof(this.MediaScaleY));
                this.OnInvalidateRender();
            }
        }

        public float MediaScaleOriginX {
            get => this.MediaScaleOrigin.X;
            set => this.MediaScaleOrigin = new Vector2(value, this.MediaScaleOrigin.Y);
        }

        public float MediaScaleOriginY {
            get => this.MediaScaleOrigin.Y;
            set => this.MediaScaleOrigin = new Vector2(this.MediaScaleOrigin.X, value);
        }

        /// <summary>
        /// The scaling origin point of this video's media. Default value is 0.5,0.5 (the center of the frame)
        /// </summary>
        public Vector2 MediaScaleOrigin {
            get => this.Model.MediaScaleOrigin;
            set {
                this.Model.MediaScaleOrigin = value;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(this.MediaScaleOriginX));
                this.RaisePropertyChanged(nameof(this.MediaScaleOriginY));
                this.OnInvalidateRender();
            }
        }

        /// <summary>
        /// The number of frames that are skipped relative to <see cref="ClipStart"/>. This will be positive if the
        /// left grip of the clip is dragged to the right, and will be 0 when dragged to the left
        /// <para>
        /// Alternative name: MediaBegin
        /// </para>
        /// </summary>
        public long MediaFrameOffset {
            get => this.Model.MediaFrameOffset;
            set {
                this.Model.MediaFrameOffset = value;
                this.RaisePropertyChanged();
                this.OnInvalidateRender();
            }
        }

        public long FrameBegin {
            get => this.Span.Begin;
            set => this.Span = this.Span.SetBegin(value);
        }

        public long FrameDuration {
            get => this.Span.Duration;
            set => this.Span = this.Span.SetDuration(value);
        }

        public long FrameEndIndex {
            get => this.Span.EndIndex;
            set => this.Span = this.Span.SetEndIndex(value);
        }

        public ClipSpan Span {
            get => this.Model.FrameSpan;
            set {
                this.Model.FrameSpan = value;
                this.RaisePropertyChanged(nameof(this.FrameBegin));
                this.RaisePropertyChanged(nameof(this.FrameDuration));
                this.RaisePropertyChanged(nameof(this.FrameEndIndex));
                this.OnInvalidateRender();
            }
        }

        public float BothPos {
            get => this.bothPos;
            set {
                float actualValue = value - this.bothPos;
                this.MediaPosition += new Vector2(actualValue);
                this.RaisePropertyChanged();
                this.bothPos = value;
            }
        }

        public float BothScale {
            get => this.bothScale;
            set {
                float actualValue = value - this.bothScale;
                this.MediaScale += new Vector2(actualValue);
                this.RaisePropertyChanged();
                this.bothScale = value;
            }
        }

        public float BothScaleOrigin {
            get => this.bothScaleOrigin;
            set {
                float actualValue = value - this.bothScaleOrigin;
                this.MediaScaleOrigin += new Vector2(actualValue);
                this.RaisePropertyChanged();
                this.bothScaleOrigin = value;
            }
        }

        public RelayCommand ResetTransformationCommand { get; }

        public VideoClipViewModel(VideoClipModel model) : base(model) {
            this.ResetTransformationCommand = new RelayCommand(() => {
                this.MediaPosition = new Vector2(0f, 0f);
                this.MediaScale = new Vector2(1f, 1f);
                this.MediaScaleOrigin = new Vector2(0.5f, 0.5f);
            });
        }

        public virtual void OnInvalidateRender() {
            this.Layer.Timeline.DoRender(true);
        }
    }
}