using System;
using System.Diagnostics;
using System.Threading.Tasks;
using FramePFX.Core.Automation;
using FramePFX.Core.Editor.History;
using FramePFX.Core.Editor.ResourceManaging;
using FramePFX.Core.Editor.ResourceManaging.Resources;
using FramePFX.Core.Editor.ResourceManaging.ViewModels;
using FramePFX.Core.Editor.ResourceManaging.ViewModels.Resources;
using FramePFX.Core.Editor.Timelines;
using FramePFX.Core.Editor.Timelines.Tracks;
using FramePFX.Core.Editor.Timelines.VideoClips;
using FramePFX.Core.Editor.ViewModels.Timelines.Clips;
using FramePFX.Core.Editor.ViewModels.Timelines.Removals;
using FramePFX.Core.History;
using FramePFX.Core.Utils;
using FramePFX.Core.Views.Dialogs.Message;

namespace FramePFX.Core.Editor.ViewModels.Timelines.Tracks {
    public class VideoTrackViewModel : TrackViewModel {
        public const string OpacityHistoryKey = "video-track.Opacity";

        private HistoryTrackOpacity opacityHistory;

        private static readonly MessageDialog SliceCloneTextResourceDialog;

        static VideoTrackViewModel() {
            SliceCloneTextResourceDialog = new MessageDialog("reference") {
                ShowAlwaysUseNextResultOption = true,
                Header = "Reference or copy text resource?",
                Message = "Do you want to reference the same text resource (shared text, font, etc), or clone it (creating a new resource)?"
            };
            SliceCloneTextResourceDialog.AddButton("Reference", "reference", true);
            SliceCloneTextResourceDialog.AddButton("Copy", "copy", true);
            SliceCloneTextResourceDialog.AddButton("Cancel", "cancel", true);
        }

        public new VideoTrack Model => (VideoTrack) base.Model;

        public double Opacity {
            get => this.Model.Opacity;
            set {
                if (this.IsAutomationRefreshInProgress || this.Model.IsAutomationChangeInProgress) {
                    Debugger.Break();
                    return;
                }

                if (!this.IsHistoryChanging) {
                    if (FrontEndHistoryHelper.ActiveDragId == OpacityHistoryKey) {
                        if (this.opacityHistory == null)
                            this.opacityHistory = new HistoryTrackOpacity(this);
                        this.opacityHistory.Opacity.SetCurrent(value);
                        FrontEndHistoryHelper.OnDragEnd = FrontEndHistoryHelper.OnDragEnd ?? ((s, cancel) => {
                            if (cancel) {
                                this.IsHistoryChanging = true;
                                this.Opacity = this.opacityHistory.Opacity.Original;
                                this.IsHistoryChanging = false;
                            }
                            else {
                                this.HistoryManager.AddAction(this.opacityHistory, "Edit opacity");
                            }

                            this.opacityHistory = null;
                        });
                    }
                    else {
                        HistoryTrackOpacity action = new HistoryTrackOpacity(this);
                        action.Opacity.SetCurrent(value);
                        this.HistoryManager.AddAction(action, "Edit opacity");
                    }
                }

                TimelineViewModel timeline = this.Timeline;
                if (TimelineUtilCore.CanAddKeyFrame(timeline, this, VideoTrack.OpacityKey)) {
                    this.AutomationData[VideoTrack.OpacityKey].GetActiveKeyFrameOrCreateNew(timeline.PlayHeadFrame).SetDoubleValue(value);
                }
                else {
                    this.AutomationData[VideoTrack.OpacityKey].GetOverride().SetDoubleValue(value);
                    this.AutomationData[VideoTrack.OpacityKey].RaiseOverrideValueChanged();
                }
            }
        }

        public bool IsVisible {
            get => this.Model.IsVisible;
            set {
                if (this.IsVisible == value) {
                    return;
                }

                if (this.IsAutomationRefreshInProgress || this.Model.IsAutomationChangeInProgress) {
                    Debugger.Break();
                    return;
                }

                if (!this.IsHistoryChanging) {
                    this.HistoryManager.AddAction(new HistoryTrackIsVisible(this, value), "Edit IsVisible");
                }

                TimelineViewModel timeline = this.Timeline;
                if (TimelineUtilCore.CanAddKeyFrame(timeline, this, VideoTrack.IsVisibleKey)) {
                    this.AutomationData[VideoTrack.IsVisibleKey].GetActiveKeyFrameOrCreateNew(timeline.PlayHeadFrame).SetBooleanValue(value);
                }
                else {
                    this.AutomationData[VideoTrack.IsVisibleKey].GetOverride().SetBooleanValue(value);
                    this.AutomationData[VideoTrack.IsVisibleKey].RaiseOverrideValueChanged();
                }
            }
        }

        private static readonly RefreshAutomationValueEventHandler RefreshOpacityHandler = (s, e) => {
            VideoTrackViewModel track = (VideoTrackViewModel) s.AutomationData.Owner;
            track.RaisePropertyChanged(nameof(track.Opacity));
            track.InvalidateRenderForAutomationRefresh(in e);
        };

        private static readonly RefreshAutomationValueEventHandler RefreshIsVisibleHandler = (s, e) => {
            VideoTrackViewModel track = (VideoTrackViewModel) s.AutomationData.Owner;
            track.RaisePropertyChanged(nameof(track.IsVisible));
            track.InvalidateRenderForAutomationRefresh(in e);
        };

        public VideoTrackViewModel(VideoTrack model) : base(model) {
            this.AutomationData.AssignRefreshHandler(VideoTrack.OpacityKey, RefreshOpacityHandler);
            this.AutomationData.AssignRefreshHandler(VideoTrack.IsVisibleKey, RefreshIsVisibleHandler);
        }

        public override bool CanDropResource(ResourceItemViewModel resource) {
            return resource is ResourceOldMediaViewModel || resource is ResourceColourViewModel || resource is ResourceImageViewModel || resource is ResourceTextViewModel;
        }

        public override async Task OnResourceDropped(ResourceItemViewModel resource, long frameBegin) {
            if (resource.UniqueId == ResourceManager.EmptyId || !resource.Model.IsRegistered()) {
                await IoC.MessageDialogs.ShowMessageAsync("Invalid resource", "This resource is not registered yet");
                return;
            }

            double fps = this.Timeline.Project.Settings.FrameRate.ToDouble;
            long defaultDuration = (long) (fps * 5);

            Clip newClip = null;
            if (resource.Model is ResourceOldMedia media) {
                media.OpenMediaFromFile();
                TimeSpan span = media.GetDuration();
                long dur = (long) Math.Floor(span.TotalSeconds * fps);
                if (dur < 2) {
                    // image files are 1
                    dur = defaultDuration;
                }

                if (dur > 0) {
                    long newProjectDuration = frameBegin + dur + 600;
                    if (newProjectDuration > this.Timeline.MaxDuration) {
                        this.Timeline.MaxDuration = newProjectDuration;
                    }

                    OldMediaVideoClip clip = new OldMediaVideoClip() {
                        FrameSpan = new FrameSpan(frameBegin, dur),
                        DisplayName = "Media Clip"
                    };

                    clip.SetTargetResourceId(media.UniqueId);
                    newClip = clip;
                }
                else {
                    await IoC.MessageDialogs.ShowMessageAsync("Invalid media", "This media has a duration of 0 and cannot be added to the timeline");
                    return;
                }
            }
            else {
                if (resource.Model is ResourceColour argb) {
                    ShapeVideoClip clip = new ShapeVideoClip() {
                        FrameSpan = new FrameSpan(frameBegin, defaultDuration),
                        Width = 200, Height = 200,
                        DisplayName = "Shape Clip"
                    };

                    clip.SetTargetResourceId(argb.UniqueId);
                    newClip = clip;
                }
                else if (resource.Model is ResourceImage img) {
                    ImageVideoClip clip = new ImageVideoClip() {
                        FrameSpan = new FrameSpan(frameBegin, defaultDuration),
                        DisplayName = "Image Clip"
                    };

                    clip.SetTargetResourceId(img.UniqueId);
                    newClip = clip;
                }
                else if (resource.Model is ResourceText text) {
                    TextVideoClip clip = new TextVideoClip() {
                        FrameSpan = new FrameSpan(frameBegin, defaultDuration),
                        DisplayName = "Text Clip"
                    };

                    clip.SetTargetResourceId(text.UniqueId);
                    newClip = clip;
                }
                else {
                    return;
                }
            }

            this.CreateClip(newClip);
            if (newClip is VideoClip videoClipModel) {
                videoClipModel.InvalidateRender();
            }
        }

        public VideoClipRangeRemoval GetRangeRemoval(long spanBegin, long spanDuration) {
            if (spanDuration < 0)
                throw new ArgumentOutOfRangeException(nameof(spanDuration), "Span duration cannot be negative");
            long spanEnd = spanBegin + spanDuration;
            VideoClipRangeRemoval range = new VideoClipRangeRemoval();
            foreach (ClipViewModel clipViewModel in this.Clips) {
                if (clipViewModel is VideoClipViewModel clip) {
                    long clipBegin = clip.FrameBegin;
                    long clipDuration = clip.FrameDuration;
                    long clipEnd = clipBegin + clipDuration;
                    if (clipEnd <= spanBegin && clipBegin >= spanEnd) {
                        continue; // not intersecting
                    }
                    if (spanBegin <= clipBegin) { // cut the left part away
                        if (spanEnd >= clipEnd) {
                            // remove clip entirely
                            range.AddRemovedClip(clip);
                        }
                        else if (spanEnd <= clipBegin) { // not intersecting
                            continue;
                        }
                        else {
                            range.AddSplitClip(clip, null, FrameSpan.FromIndex(spanEnd, clipEnd));
                        }
                    }
                    else if (spanEnd >= clipEnd) { // cut the right part away
                        if (spanBegin >= clipEnd) { // not intersecting
                            continue;
                        }
                        else {
                            range.AddSplitClip(clip, FrameSpan.FromIndex(clipBegin, spanBegin), null);
                        }
                    }
                    else { // fully intersecting; double split
                        range.AddSplitClip(clip, FrameSpan.FromIndex(clipBegin, spanBegin), FrameSpan.FromIndex(spanEnd, clipEnd));
                    }
                }
            }
            return range;
        }

        protected void InvalidateRenderForAutomationRefresh(in RefreshAutomationValueEventArgs e) {
            VideoEditorViewModel editor; // slight performance helper
            if (!e.IsDuringPlayback && (editor = this.Editor) != null && !editor.Playback.IsPlaying) {
                this.Timeline.DoRender(true);
            }
        }
    }
}