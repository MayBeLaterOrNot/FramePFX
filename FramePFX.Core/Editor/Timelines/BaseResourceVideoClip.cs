using System;
using FramePFX.Core.Editor.ResourceManaging;
using FramePFX.Core.Editor.ResourceManaging.Events;
using FramePFX.Core.Editor.Timelines.VideoClips;
using FramePFX.Core.RBC;
using FramePFX.Core.Utils;

namespace FramePFX.Core.Editor.Timelines {
    /// <summary>
    /// A base video clip that references a single resource
    /// </summary>
    /// <typeparam name="T">The resource item type</typeparam>
    public abstract class BaseResourceVideoClip<T> : VideoClip where T : ResourceItem {
        public delegate void ClipResourceModifiedEventHandler(T resource, string property);
        public delegate void ClipResourceChangedEventHandler(T oldItem, T newItem);

        private readonly ResourcePath<T>.ResourceChangedEventHandler resourceChangedHandler;
        private readonly ResourceModifiedEventHandler dataModifiedHandler;
        private readonly ResourceItemEventHandler onlineStateChangedHandler;

        public ResourcePath<T> ResourcePath { get; private set; }

        public event ClipResourceChangedEventHandler ClipResourceChanged;
        public event ClipResourceModifiedEventHandler ClipResourceDataModified;
        public event ResourceItemEventHandler OnlineStateChanged;

        protected BaseResourceVideoClip() {
            this.resourceChangedHandler = this.OnResourceChangedInternal;
            this.dataModifiedHandler = this.OnResourceDataModifiedInternal;
            this.onlineStateChangedHandler = this.OnOnlineStateChangedInternal;
        }

        private void DisposePath() {
            ResourcePath<T> path = this.ResourcePath;
            this.ResourcePath = null; // just in case the code below throws, don't reference a disposed instance
            if (path != null) {
                try {
                    path.Dispose();
                }
                finally {
                    path.ResourceChanged -= this.resourceChangedHandler;
                }
            }
        }

        public override void OnTrackChanged(Track oldTrack, Track track) {
            base.OnTrackChanged(oldTrack, track);
            if (this.ResourcePath == null)
                return;
            ResourceManager manager = track?.Timeline?.Project?.ResourceManager;
            if (manager != this.ResourcePath.Manager) {
                this.ResourcePath.SetManager(manager);
            }
        }

        public override void OnTrackTimelineChanged(Timeline oldTimeline, Timeline timeline) {
            base.OnTrackTimelineChanged(oldTimeline, timeline);
            if (this.ResourcePath == null)
                return;
            ResourceManager manager = timeline?.Project?.ResourceManager;
            if (manager != this.ResourcePath.Manager) {
                this.ResourcePath.SetManager(manager);
            }
        }

        /// <summary>
        /// Sets this <see cref="BaseResourceVideoClip{T}"/>'s target resource ID. The previous <see cref="ResourcePath{T}"/> is
        /// disposed and replace with a new instance using the same <see cref="ResourceManager"/>
        /// </summary>
        /// <param name="id">The target resource ID. Cannot be null, empty, or consist of only whitespaces</param>
        public void SetTargetResourceId(ulong id) {
            this.DisposePath();
            this.ResourcePath = new ResourcePath<T>(this.ResourceManager, id);
            this.ResourcePath.ResourceChanged += this.resourceChangedHandler;
        }

        private void OnResourceChangedInternal(T oldItem, T newItem) {
            if (oldItem != null) {
                oldItem.OnlineStateChanged -= this.onlineStateChangedHandler;
                oldItem.DataModified -= this.dataModifiedHandler;
            }

            if (newItem != null) {
                newItem.OnlineStateChanged += this.onlineStateChangedHandler;
                newItem.DataModified += this.dataModifiedHandler;
            }

            this.OnResourceChanged(oldItem, newItem);
            this.ClipResourceChanged?.Invoke(oldItem, newItem);
        }

        private void OnResourceDataModifiedInternal(ResourceItem sender, string property) {
            if (this.ResourcePath == null)
                throw new InvalidOperationException("Expected resource path to be non-null");
            if (!this.ResourcePath.IsCachedItemEqualTo(sender))
                throw new InvalidOperationException("Received data modified event for a resource that does not equal the resource path's item");
            this.OnResourceDataModified(property);
            this.ClipResourceDataModified?.Invoke((T) sender, property);
        }

        private void OnOnlineStateChangedInternal(ResourceManager manager, ResourceItem item) {
            if (!this.ResourcePath.IsCachedItemEqualTo(item))
                throw new InvalidOperationException("Received data modified event for a resource that does not equal the resource path's item");
            this.OnOnlineStateChanged((T) item);
            this.OnlineStateChanged?.Invoke(manager, item);
        }

        protected virtual void OnOnlineStateChanged(T item) {
            this.InvalidateRender();
        }

        protected virtual void OnResourceChanged(T oldItem, T newItem) {
            this.InvalidateRender();
        }

        protected virtual void OnResourceDataModified(string property) {
            this.InvalidateRender();
        }

        public override void WriteToRBE(RBEDictionary data) {
            base.WriteToRBE(data);
            if (this.ResourcePath != null)
                ResourcePath<T>.WriteToRBE(this.ResourcePath, data.CreateDictionary(nameof(this.ResourcePath)));
        }

        public override void ReadFromRBE(RBEDictionary data) {
            base.ReadFromRBE(data);
            if (data.TryGetElement(nameof(this.ResourcePath), out RBEDictionary resource))
                this.ResourcePath = ResourcePath<T>.ReadFromRBE(this.Track?.Timeline.Project.ResourceManager, resource);
        }

        public bool TryGetResource(out T resource) {
            if (this.ResourcePath != null) {
                return this.ResourcePath.TryGetResource(out resource);
            }

            resource = null;
            return false;
        }

        protected override void DisposeCore(ExceptionStack stack) {
            base.DisposeCore(stack);
            if (this.ResourcePath != null && !this.ResourcePath.IsDisposed) {
                try {
                    this.DisposePath();
                }
                catch (Exception e) {
                    stack.Add(e);
                }
            }
        }

        protected override void LoadDataIntoClone(Clip clone) {
            base.LoadDataIntoClone(clone);
            if (this.ResourcePath != null) {
                ((BaseResourceVideoClip<T>) clone).SetTargetResourceId(this.ResourcePath.ResourceId);
            }
        }
    }
}