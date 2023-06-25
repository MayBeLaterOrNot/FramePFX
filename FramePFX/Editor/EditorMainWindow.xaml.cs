﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FramePFX.Core;
using FramePFX.Core.Editor;
using FramePFX.Core.Editor.Timeline;
using FramePFX.Core.Editor.ViewModels;
using FramePFX.Core.Editor.ViewModels.Timeline;
using FramePFX.Core.Notifications;
using FramePFX.Core.Notifications.Types;
using FramePFX.Core.Rendering;
using FramePFX.Core.Utils;
using FramePFX.Editor.Properties.Pages;
using FramePFX.Notifications;
using FramePFX.Themes;
using FramePFX.Utils;
using FramePFX.Views;
using SkiaSharp;
using Time = FramePFX.Core.Utils.Time;

namespace FramePFX.Editor {
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class EditorMainWindow : WindowEx, IVideoEditor, INotificationHandler {
        public VideoEditorViewModel Editor => (VideoEditorViewModel) this.DataContext;

        private readonly Action renderCallback;

        private const long RefreshInterval = 5000;
        private long lastRefreshTime;
        private bool isRefreshing;
        private volatile int isRenderScheduled;
        private bool isPrintingErrors;

        public NotificationPanelViewModel NotificationPanel { get; }

        public EditorMainWindow() {
            BindingErrorListener.Listen();
            this.InitializeComponent();
            // this.oglPort = new OGLMainViewPortImpl(this.GLViewport);
            IoC.BroadcastShortcutActivity = (x) => {
                this.NotificationBarTextBlock.Text = x;
            };

            this.NotificationPanel = new NotificationPanelViewModel(this);

            this.DataContext = new VideoEditorViewModel(this);
            this.renderCallback = this.DoRenderCore;

            this.lastRefreshTime = Time.GetSystemMillis();

            this.NotificationPanelPopup.StaysOpen = true;
            this.NotificationPanelPopup.Placement = PlacementMode.Absolute;
            this.NotificationPanelPopup.PlacementTarget = this;
            this.NotificationPanelPopup.PlacementRectangle = System.Windows.Rect.Empty;
            this.NotificationPanelPopup.DataContext = this.NotificationPanel;
            this.RefreshPopupLocation();

            this.Width = 1257;
        }

        public void OnNotificationPushed(NotificationViewModel notification) {
            if (!this.NotificationPanelPopup.IsOpen)
                this.NotificationPanelPopup.IsOpen = true;
            this.Dispatcher.InvokeAsync(this.RefreshPopupLocation, DispatcherPriority.Render);
        }

        public void OnNotificationRemoved(NotificationViewModel notification) {
            if (this.NotificationPanel.Notifications.Count < 1) {
                this.NotificationPanelPopup.IsOpen = false;
            }

            this.Dispatcher.InvokeAsync(this.RefreshPopupLocation, DispatcherPriority.Loaded);
        }

        public void BeginNotificationFadeOutAnimation(NotificationViewModel notification, Action<NotificationViewModel, bool> onCompleteCallback = null) {
            NotificationList list = VisualTreeUtils.FindVisualChild<NotificationList>(this.NotificationPanelPopup.Child);
            if (list == null) {
                return;
            }

            int index = (notification.Panel ?? this.NotificationPanel).Notifications.IndexOf(notification);
            if (index == -1) {
                throw new Exception("Item not present in panel");
            }

            if (!(list.ItemContainerGenerator.ContainerFromIndex(index) is NotificationControl control)) {
                return;
            }

            DoubleAnimation animation = new DoubleAnimation(1d, 0d, TimeSpan.FromSeconds(2), FillBehavior.Stop);
            animation.Completed += (sender, args) => {
                onCompleteCallback?.Invoke(notification, BaseViewModel.GetInternalData<bool>(notification, "IsCancelled"));
            };

            control.BeginAnimation(OpacityProperty, animation);
        }

        public void CancelNotificationFadeOutAnimation(NotificationViewModel notification) {
            if (BaseViewModel.GetInternalData<bool>(notification, "IsCancelled")) {
                return;
            }

            NotificationList list = VisualTreeUtils.FindVisualChild<NotificationList>(this.NotificationPanelPopup.Child);
            if (list == null) {
                return;
            }

            int index = (notification.Panel ?? this.NotificationPanel).Notifications.IndexOf(notification);
            if (index == -1) {
                throw new Exception("Item not present in panel");
            }

            if (!(list.ItemContainerGenerator.ContainerFromIndex(index) is NotificationControl control)) {
                return;
            }

            BaseViewModel.SetInternalData(notification, "IsCancelled", BoolBox.True);
            control.BeginAnimation(OpacityProperty, null);
        }

        protected override void OnLocationChanged(EventArgs e) {
            base.OnLocationChanged(e);
            this.RefreshPopupLocation();
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo) {
            base.OnRenderSizeChanged(sizeInfo);
            if (this.WindowState == WindowState.Maximized) {
                this.Dispatcher.InvokeAsync(this.RefreshPopupLocation, DispatcherPriority.ApplicationIdle);
            }
            else {
                this.RefreshPopupLocation();
            }
        }

        private static readonly FieldInfo actualTopField = typeof(Window).GetField("_actualTop", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.GetField);
        private static readonly FieldInfo actualLeftField = typeof(Window).GetField("_actualLeft", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.GetField);

        public void RefreshPopupLocation() {
            Popup popup = this.NotificationPanelPopup;
            if (popup == null || !popup.IsOpen) {
                return;
            }

            if (!(popup.Child is FrameworkElement element)) {
                return;
            }

            // winpos = X 1663
            // popup pos = X 1620
            // popup wid = 300

            switch (this.WindowState) {
                case WindowState.Normal: {
                    popup.Visibility = Visibility.Visible;
                    popup.VerticalOffset = this.Top + this.ActualHeight - element.ActualHeight;
                    popup.HorizontalOffset = this.Left + this.ActualWidth - element.ActualWidth;
                    break;
                }
                case WindowState.Minimized: {
                    popup.Visibility = Visibility.Collapsed;
                    break;
                }
                case WindowState.Maximized: {
                    popup.Visibility = Visibility.Visible;
                    Thickness thicc = this.BorderThickness;
                    double top = (double) actualTopField.GetValue(this) + thicc.Top;
                    double left = (double) actualLeftField.GetValue(this) + thicc.Left;
                    popup.VerticalOffset = top - (thicc.Top + thicc.Bottom) + this.ActualHeight - element.ActualHeight;
                    popup.HorizontalOffset = left - (thicc.Left + thicc.Right) + this.ActualWidth - element.ActualWidth;
                    break;
                }
                default: throw new ArgumentOutOfRangeException();
            }
        }

        // protected override void OnActivated(EventArgs e) {
        //     base.OnActivated(e);
        //     if (this.isRefreshing || this.Editor.IsClosingProject) {
        //         return;
        //     }
        //     long time = Time.GetSystemMillis();
        //     if ((time - this.lastRefreshTime) >= RefreshInterval) {
        //         this.isRefreshing = true;
        //         this.RefreshAction();
        //     }
        // }

        // private async void RefreshAction() {
        //     if (this.Editor.ActiveProject is ProjectViewModel vm) {
        //         await ResourceCheckerViewModel.LoadProjectResources(vm, false);
        //     }
        //     this.isRefreshing = false;
        //     this.lastRefreshTime = Time.GetSystemMillis();
        // }

        public void UpdateSelectionPropertyPages() {
            this.ClipPropertyPanelList.Items.Clear();
            if (this.Editor.ActiveProject is ProjectViewModel project) {
                List<ClipViewModel> list = project.Timeline.Tracks.SelectMany(x => x.SelectedClips).ToList();
                if (list.Count != 1) {
                    return;
                }

                this.GeneratePropertyPages(list[0]);
            }
        }

        public void PushNotificationMessage(string message) {
            this.NotificationBarTextBlock.Text = message;
        }

        public void GeneratePropertyPages(ClipViewModel clip) {
            Type root = typeof(ClipViewModel);
            List<Type> types = new List<Type>();
            for (Type type = clip.GetType(); type != null && root.IsAssignableFrom(type); type = type.BaseType) {
                types.Add(type);
            }

            if (types.Count > 0) {
                types.Reverse();
                this.GeneratePropertyPages(types, clip);
            }
        }

        public void GeneratePropertyPages(List<Type> types, ClipViewModel clip) {
            List<FrameworkElement> controls = new List<FrameworkElement>(types.Count);
            foreach (Type type in types) {
                if (PropertyPageRegistry.Instance.GenerateControl(type, clip, out FrameworkElement control)) {
                    control.DataContext = clip;
                    controls.Add(control);
                }
            }

            if (controls.Count < 1) {
                return;
            }

            // this.ClipPropertyPanelList.Items.Add(controls[0]);
            foreach (FrameworkElement t in controls) {
                this.ClipPropertyPanelList.Items.Add(t);
            }
        }

        public void RenderViewPort(bool scheduleRender) {
            if (Interlocked.CompareExchange(ref this.isRenderScheduled, 1, 0) != 0) {
                return;
            }

            if (scheduleRender) {
                this.Dispatcher.InvokeAsync(this.renderCallback);
            }
            else {
                this.Dispatcher.Invoke(this.renderCallback);
                this.isRenderScheduled = 0;
            }
        }

        private void DoRenderCore() {
            // this.ViewPortElement.InvalidateVisual();
            VideoEditorViewModel editor = this.Editor;
            ProjectViewModel project = editor.ActiveProject;
            if (project == null || project.Model.IsSaving) {
                this.isRenderScheduled = 0;
                return;
            }

            if (this.ViewPortElement.BeginRender(out SKSurface surface)) {
                long frame = project.Timeline.PlayHeadFrame;

                RenderContext context = new RenderContext(surface, surface.Canvas, this.ViewPortElement.FrameInfo);
                context.Canvas.Clear(SKColors.Black);
                // project.Model.AutomationEngine.TickProjectAtFrame(frame);
                project.Model.Timeline.Render(context, frame);

                Dictionary<ClipModel, Exception> dictionary = project.Model.Timeline.ExceptionsLastRender;
                if (dictionary.Count > 0) {
                    this.PrintRenderErrors(dictionary);
                    dictionary.Clear();
                }

                this.ViewPortElement.EndRender();
            }

            this.isRenderScheduled = 0;
        }

        private async void PrintRenderErrors(Dictionary<ClipModel, Exception> dictionary) {
            if (this.isPrintingErrors) {
                return;
            }

            this.isPrintingErrors = true;
            StringBuilder sb = new StringBuilder(2048);
            foreach (KeyValuePair<ClipModel,Exception> entry in dictionary) {
                sb.Append($"{entry.Key.DisplayName ?? entry.Key.ToString()}: {entry.Value.GetToString()}\n");
            }

            await IoC.MessageDialogs.ShowMessageExAsync("Render error", $"An exception updating {dictionary.Count} clips", sb.ToString());
            this.isPrintingErrors = false;
        }

        protected override async Task<bool> OnClosingAsync() {
            try {
                if (!await this.Editor.PromptSaveAndCloseProjectAction()) {
                    return false;
                }
            }
            catch (Exception e) {
                await IoC.MessageDialogs.ShowMessageExAsync("Failed to close project", "Exception while closing project", e.GetToString());
            }

            try {
                this.Editor.Dispose();
            }
            catch (Exception e) {
                await IoC.MessageDialogs.ShowMessageExAsync("Failed to dispose", "Exception while disposing editor", e.GetToString());
            }

            return true;
        }

        // Dragging a top thumb to resize between 2 tracks is a bit iffy... so nah
        // private void OnTopThumbDrag(object sender, DragDeltaEventArgs e) {
        //     if ((sender as Thumb)?.DataContext is TrackViewModel track) {
        //         double trackHeight = track.Height - e.VerticalChange;
        //         if (trackHeight < track.MinHeight || trackHeight > track.MaxHeight) {
        //             if (track.Timeline.GetPrevious(track) is trackViewModel behind1) {
        //                 double behindHeight = behind1.Height + e.VerticalChange;
        //                 if (behindHeight < behind1.MinHeight || behindHeight > behind1.MaxHeight)
        //                     return;
        //                 behind1.Height = behindHeight;
        //             }
        //         }
        //         else if (track.Timeline.GetPrevious(track) is trackViewModel behind2) {
        //             double behindHeight = behind2.Height + e.VerticalChange;
        //             if (behindHeight < behind2.MinHeight || behindHeight > behind2.MaxHeight) {
        //                 return;
        //             }
        //             track.Height = trackHeight;
        //             behind2.Height = behindHeight;
        //         }
        //     }
        // }

        private void OnBottomThumbDrag(object sender, DragDeltaEventArgs e) {
            if ((sender as Thumb)?.DataContext is TrackViewModel track) {
                track.Height = Maths.Clamp(track.Height + e.VerticalChange, track.MinHeight, track.MaxHeight);
            }
        }

        private void OnFitContentToWindowClick(object sender, RoutedEventArgs e) {
            this.VPViewBox.FitContentToCenter();
        }

        private int number;

        private void MenuItem_OnClick(object sender, RoutedEventArgs e) {
            this.NotificationPanel.PushNotification(new MessageNotification("Header!!!", $"Some message here ({++this.number})", TimeSpan.FromSeconds(5)));
        }

        private void ShowLogsClick(object sender, RoutedEventArgs e) {
            new AppLoggerWindow().Show();
        }

        private void SetThemeClick(object sender, RoutedEventArgs e) {
            ThemeType type;
            switch (((MenuItem) sender).Uid) {
                case "0": type = ThemeType.DeepDark; break;
                case "1": type = ThemeType.SoftDark; break;
                case "2": type = ThemeType.GreyTheme; break;
                case "3": type = ThemeType.RedBlackTheme; break;
                default: return;
            }

            ThemesController.SetTheme(type);
        }
    }
}
