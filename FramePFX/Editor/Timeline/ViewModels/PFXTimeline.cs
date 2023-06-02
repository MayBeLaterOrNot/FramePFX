﻿using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using FramePFX.Core;
using FramePFX.Core.Utils;
using FramePFX.Core.Views.Dialogs.UserInputs;
using FramePFX.Editor.Project.ViewModels;
using FramePFX.Editor.Timeline.Utils;
using FramePFX.Editor.Timeline.ViewModels.Clips;
using FramePFX.Editor.Timeline.ViewModels.Layer;

namespace FramePFX.Editor.Timeline.ViewModels {
    public class PFXTimeline : BaseViewModel {
        private readonly RapidDispatchCallback renderDispatch;
        private readonly ObservableCollection<PFXTimelineLayer> layers;
        private volatile bool ignorePlayHeadPropertyChange;
        public volatile bool isFramePropertyChangeScheduled;
        public bool ignorePropertyChangedRender;

        public PFXProject Project { get; }

        public PFXViewportPlayback PlaybackViewport => this.Project.VideoEditor.Playback;

        /// <summary>
        /// This timeline's layer collection
        /// </summary>
        public ReadOnlyObservableCollection<PFXTimelineLayer> Layers { get; }

        private PFXTimelineLayer selectedTimelineLayer;
        public PFXTimelineLayer SelectedTimelineLayer {
            get => this.selectedTimelineLayer;
            set => this.RaisePropertyChanged(ref this.selectedTimelineLayer, value);
        }

        private PFXClipViewModel mainSelectedClip;
        public PFXClipViewModel MainSelectedClip {
            get => this.mainSelectedClip;
            set => this.RaisePropertyChanged(ref this.mainSelectedClip, value);
        }

        private PFXClipViewModel clipToModify;
        public PFXClipViewModel ClipToModify {
            get => this.clipToModify;
            set => this.RaisePropertyChanged(ref this.clipToModify, value);
        }

        /// <summary>
        /// A handle to the actual timeline UI control
        /// </summary>
        public ITimelineHandle Handle { get; set; }

        /// <summary>
        /// A handle to the actual play head UI control
        /// </summary>
        public IPlayHeadHandle PlayHeadHandle { get; set; }

        private long maxDuration;
        public long MaxDuration {
            get => this.maxDuration;
            set => this.RaisePropertyChangedIfChanged(ref this.maxDuration, value);
        }

        private long playHeadFrame;
        public long PlayHeadFrame {
            get => this.playHeadFrame;
            set {
                long oldValue = this.playHeadFrame;
                if (oldValue == value) {
                    return;
                }

                if (value >= this.MaxDuration) {
                    value = this.MaxDuration - 1;
                }

                if (value < 0) {
                    value = 0;
                }

                if (this.ignorePlayHeadPropertyChange) {
                    this.playHeadFrame = value;
                }
                else {
                    this.RaisePropertyChanged(ref this.playHeadFrame, value);
                    this.OnPlayHeadMoved(oldValue, value, true);
                }
            }
        }

        public bool IsAtLastFrame => this.PlayHeadFrame >= (this.MaxDuration - 1);

        public bool IsRenderDirty { get; private set; }

        public RelayCommand<bool> DeleteSelectedClipsCommand { get; }

        public ICommand DeleteSelectedLayerCommand { get; }

        public InputValidator LayerNameValidator { get; }

        public PFXTimeline(PFXProject project) {
            this.LayerNameValidator = InputValidator.FromFunc((x) => string.IsNullOrEmpty(x) ? "Layer name cannot be empty" : null);
            this.Project = project;
            this.layers = new ObservableCollection<PFXTimelineLayer>();
            this.Layers = new ReadOnlyObservableCollection<PFXTimelineLayer>(this.layers);
            this.renderDispatch = new RapidDispatchCallback(this.RenderViewPortAndMarkNotDirty) {
                InvokeLater = true
            };
            this.DeleteSelectedClipsCommand = new RelayCommand<bool>(async (x) => await this.DeleteSelectedClipsAction(x));
            this.DeleteSelectedLayerCommand = new RelayCommand(this.DeleteSelectedLayerAction);
        }

        public void OnUpdateSelection(IEnumerable<PFXVideoClipViewModel> clips) {
            this.GenerateProperties(clips.ToList());
        }

        public void GenerateProperties(List<PFXVideoClipViewModel> clips) {
            // List<List<PropertyGroupViewModel>> groupTypes = new List<List<PropertyGroupViewModel>>();
            // for (int i = 0; i < clips.Count; i++) {
            //     ClipContainerViewModel clip = clips[i];
            //     if (clip.Content != null) {
            //         groupTypes.Add(new List<PropertyGroupViewModel>());
            //         clip.Content.AccumulatePropertyGroups(groupTypes[i]);
            //     }
            //     else {
            //         groupTypes.Add(null);
            //     }
            // }
            // foreach (List<PropertyGroupViewModel> groups in groupTypes) {
            //     if (groups == null) {
            //         continue;
            //     }
            // }
            // this.GeneratedProperties.Clear();
            // foreach (PropertyGroupViewModel entry in common) {
            //     this.GeneratedProperties.Add(entry);
            // }
        }

        public void DeleteSelectedLayerAction() {
            if (this.selectedTimelineLayer != null) {
                this.layers.Remove(this.selectedTimelineLayer);
            }
        }

        public async Task DeleteSelectedClipsAction(bool skipDialog) {
            List<PFXVideoClipViewModel> list = this.Handle.GetSelectedClips().ToList();
            switch (list.Count) {
                case 0: return;
                case 1:
                    list[0].Layer.RemoveClip(list[0]);
                    return;
                default: {
                    if (skipDialog || await IoC.MessageDialogs.ShowYesNoDialogAsync("Delete clips", "Do you want to delete these " + list.Count + " clips?")) {
                        foreach (PFXVideoClipViewModel clip in list) {
                            clip.Layer.RemoveClip(clip);
                        }
                    }

                    return;
                }
            }
        }

        public bool IsVPReadyForRender() {
            return this.Project.VideoEditor.Playback.IsReadyForRender();
        }

        public void OnPlayHeadMoved(long oldFrame, long newFrame, bool render) {
            if (oldFrame == newFrame) {
                return;
            }

            if (render && this.IsVPReadyForRender()) {
                this.RenderViewPortAndMarkNotDirty();
            }
            else {
                this.IsRenderDirty = true;
            }
        }

        public void InvalidateRenderForPropertyChanged() {
            if (!this.ignorePropertyChangedRender) {
                this.ScheduleRender(false);
            }
        }

        public void ScheduleRender(bool useCurrentThread = true) {
            if (useCurrentThread) {
                this.RenderViewPortAndMarkNotDirty();
            }
            else {
                this.renderDispatch.Invoke();
            }
        }

        private void RenderViewPortAndMarkNotDirty() {
            this.IsRenderDirty = false;
            this.Render();
        }

        public void Render() {
            this.Project.VideoEditor.Playback.RenderTimeline(this);
        }

        // TODO: Could optimise this, maybe create "chunks" of clips that span 10 frame sections across the entire timeline
        public IEnumerable<PFXClipViewModel> GetClipsAtPlayHead() {
            return this.GetClipsAtFrame(this.playHeadFrame);
        }

        public IEnumerable<PFXClipViewModel> GetClipsAtFrame(long frame) {
            return this.Layers.SelectMany(layer => layer.GetClipsAtFrame(frame));
        }

        public PFXVideoLayer CreateVideoLayer(string name = null) {
            PFXVideoLayer timelineLayer = new PFXVideoLayer(this) {
                Name = name ?? $"Layer {this.Layers.Count + 1}"
            };

            this.layers.Add(timelineLayer);
            return timelineLayer;
        }

        public PFXTimelineLayer GetPrevious(PFXTimelineLayer timelineLayer) {
            int index = this.Layers.IndexOf(timelineLayer);
            return index < 1 ? null : this.Layers[index - 1];
        }

        /// <summary>
        /// Steps the play head for the next frame (typically from the playback thread)
        /// </summary>
        public void StepFrame(long change = 1L) {
            this.ignorePlayHeadPropertyChange = true;
            long oldFrame = this.playHeadFrame;
            this.playHeadFrame = Periodic.Add(oldFrame, change, 0L, this.MaxDuration);
            this.OnPlayHeadMoved(oldFrame, this.playHeadFrame, false);
            if (!this.isFramePropertyChangeScheduled) {
                this.isFramePropertyChangeScheduled = true;
                IoC.Dispatcher.Invoke(() => {
                    this.RaisePropertyChanged(nameof(this.PlayHeadFrame));
                    this.isFramePropertyChangeScheduled = false;
                });
            }

            this.ignorePlayHeadPropertyChange = false;
        }

        public void OnPlayBegin() {
            foreach (PFXTimelineLayer layer in this.layers) {
                foreach (PFXClipViewModel clip in layer.Clips) {
                    clip.OnTimelinePlayBegin();
                }
            }
        }

        public void OnPlayEnd() {
            foreach (PFXTimelineLayer layer in this.layers) {
                foreach (PFXClipViewModel clip in layer.Clips) {
                    clip.OnTimelinePlayEnd();
                }
            }
        }
    }
}
