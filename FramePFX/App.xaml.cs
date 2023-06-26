﻿using System;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using FFmpeg.AutoGen;
using FramePFX.Core;
using FramePFX.Core.Actions;
using FramePFX.Core.Automation.Keyframe;
using FramePFX.Core.Editor;
using FramePFX.Core.Editor.ResourceManaging;
using FramePFX.Core.Editor.ResourceManaging.Resources;
using FramePFX.Core.Editor.Timeline;
using FramePFX.Core.Editor.Timeline.AudioClips;
using FramePFX.Core.Editor.Timeline.Tracks;
using FramePFX.Core.Editor.Timeline.VideoClips;
using FramePFX.Core.Editor.ViewModels;
using FramePFX.Core.Editor.ViewModels.Timeline;
using FramePFX.Core.Shortcuts.Managing;
using FramePFX.Core.Shortcuts.ViewModels;
using FramePFX.Core.Utils;
using FramePFX.Editor;
using FramePFX.Shortcuts;
using FramePFX.Shortcuts.Converters;
using FramePFX.Utils;
using FramePFX.Views;

namespace FramePFX {
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application {
        private AppSplashScreen splash;

        #if DEBUG
        public static ProjectViewModel DemoProject { get; } = new ProjectViewModel(CreateDemoProject());
        public static TimelineViewModel DemoTimeline { get; } = new TimelineViewModel(DemoProject, new TimelineModel(DemoProject.Model));
        #endif

        public App() {

        }

        public void RegisterActions() {
            ActionManager.SearchAndRegisterActions(ActionManager.Instance);
        }

        private async Task SetActivity(string activity) {
            this.splash.CurrentActivity = activity;
            await this.Dispatcher.InvokeAsync(() => {
            }, DispatcherPriority.ApplicationIdle);
        }

        public async Task InitApp() {
            await this.SetActivity("Loading services...");
            string[] envArgs = Environment.GetCommandLineArgs();
            if (envArgs.Length > 0 && Path.GetDirectoryName(envArgs[0]) is string dir && dir.Length > 0) {
                Directory.SetCurrentDirectory(dir);
            }

            IoC.Dispatcher = new DispatcherDelegate(this.Dispatcher);
            IoC.OnShortcutModified = (x) => {
                if (!string.IsNullOrWhiteSpace(x)) {
                    ShortcutManager.Instance.InvalidateShortcutCache();
                    GlobalUpdateShortcutGestureConverter.BroadcastChange();
                }
            };

            IoC.LoadServicesFromAttributes();

            await this.SetActivity("Loading shortcut and action managers...");
            ShortcutManager.Instance = new WPFShortcutManager();
            ActionManager.Instance = new ActionManager();
            InputStrokeViewModel.KeyToReadableString = KeyStrokeStringConverter.ToStringFunction;
            InputStrokeViewModel.MouseToReadableString = MouseStrokeStringConverter.ToStringFunction;

            this.RegisterActions();

            await this.SetActivity("Loading keymap...");
            string keymapFilePath = Path.GetFullPath(@"Keymap.xml");
            if (File.Exists(keymapFilePath)) {
                using (FileStream stream = File.OpenRead(keymapFilePath)) {
                    ShortcutGroup group = WPFKeyMapSerialiser.Instance.Deserialise(stream);
                    WPFShortcutManager.WPFInstance.SetRoot(group);
                }
            }
            else {
                await IoC.MessageDialogs.ShowMessageAsync("No keymap available", "Keymap file does not exist: " + keymapFilePath + $".\nCurrent directory: {Directory.GetCurrentDirectory()}\nCommand line args:{string.Join("\n", Environment.GetCommandLineArgs())}");
            }

            await this.SetActivity("Loading FFmpeg...");
            ffmpeg.avdevice_register_all();
        }

        private async void Application_Startup(object sender, StartupEventArgs e) {
            // Dialogs may be shown, becoming the main window, possibly causing the
            // app to shutdown when the mode is OnMainWindowClose or OnLastWindowClose

            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            this.MainWindow = this.splash = new AppSplashScreen();
            this.splash.Show();

            try {
                await this.InitApp();
            }
            catch (Exception ex) {
                if (IoC.MessageDialogs != null) {
                    await IoC.MessageDialogs.ShowMessageExAsync("App initialisation failed", "Failed to start FramePFX", ex.GetToString());
                }
                else {
                    MessageBox.Show("Failed to start FramePFX:\n\n" + ex, "Fatal App initialisation failure");
                }

                this.Dispatcher.Invoke(() => {
                    this.Shutdown(0);
                }, DispatcherPriority.Background);

                return;
            }

            await this.SetActivity("Loading FramePFX main window...");
            EditorMainWindow window = new EditorMainWindow();
            this.splash.Close();
            this.MainWindow = window;
            this.ShutdownMode = ShutdownMode.OnMainWindowClose;
            window.Show();
            await this.Dispatcher.Invoke(async () => {
                await this.OnVideoEditorLoaded(window.Editor);
            }, DispatcherPriority.Loaded);
        }

        public async Task OnVideoEditorLoaded(VideoEditorViewModel editor) {
            #if DEBUG
            await editor.SetProject(new ProjectViewModel(CreateDebugProject()), true);
            #else
            await editor.SetProject(new ProjectViewModel(CreateDemoProject()), true);
            #endif
            ((EditorMainWindow) this.MainWindow)?.VPViewBox.FitContentToCenter();
            editor.ActiveProject.AutomationEngine.TickAndRefreshProject(false);
            editor.View.RenderViewPort(false);
        }

        protected override void OnExit(ExitEventArgs e) {
            base.OnExit(e);
        }

        public static ProjectModel CreateDemoProject() {
            // Demo project -- projects can be created as entirely models
            ProjectModel project = new ProjectModel();
            project.Settings.Resolution = new Resolution(1920, 1080);

            {
                ResourceManager manager = project.ResourceManager;
                manager.RegisterEntry("colour_red", manager.RootGroup.Add(new ResourceColour(220, 25, 25)));
                manager.RegisterEntry("colour_green", manager.RootGroup.Add(new ResourceColour(25, 220, 25)));
                manager.RegisterEntry("colour_blue", manager.RootGroup.Add(new ResourceColour(25, 25, 220)));

                ResourceGroup group = new ResourceGroup("Extra Colours");
                manager.RootGroup.AddItemToList(group);
                manager.RegisterEntry("white colour", group.Add(new ResourceColour(220, 220, 220)));
                manager.RegisterEntry("idek", group.Add(new ResourceColour(50, 100, 220)));
            }

            {
                VideoTrackModel track1 = new VideoTrackModel(project.Timeline) {
                    DisplayName = "Track 1 with stuff"
                };
                project.Timeline.AddTrack(track1);

                track1.AutomationData[VideoTrackModel.OpacityKey].AddKeyFrame(new KeyFrameDouble(0, 0.3d));
                track1.AutomationData[VideoTrackModel.OpacityKey].AddKeyFrame(new KeyFrameDouble(50, 0.5d));
                track1.AutomationData[VideoTrackModel.OpacityKey].AddKeyFrame(new KeyFrameDouble(100, 1d));
                track1.AutomationData.ActiveKeyFullId = VideoTrackModel.OpacityKey.FullId;

                ShapeClipModel clip1 = new ShapeClipModel {
                    Width = 200, Height = 200,
                    FrameSpan = new FrameSpan(0, 120),
                    DisplayName = "Clip colour_red"
                };

                clip1.MediaPosition = new Vector2(0, 0);
                clip1.SetTargetResourceId("idek");
                track1.AddClip(clip1);

                ShapeClipModel clip2 = new ShapeClipModel {
                    Width = 200, Height = 200,
                    FrameSpan = new FrameSpan(150, 30),
                    DisplayName = "Clip colour_green"
                };
                clip2.MediaPosition = new Vector2(200, 200);
                clip2.SetTargetResourceId("colour_green");

                track1.AddClip(clip2);
            }
            {
                VideoTrackModel track2 = new VideoTrackModel(project.Timeline) {
                    DisplayName = "Track 2"
                };
                project.Timeline.AddTrack(track2);

                ShapeClipModel clip1 = new ShapeClipModel {
                    Width = 400, Height = 400,
                    FrameSpan = new FrameSpan(300, 90),
                    DisplayName = "Clip colour_blue"
                };

                clip1.MediaPosition = new Vector2(200, 200);
                clip1.SetTargetResourceId("colour_blue");
                track2.AddClip(clip1);
                ShapeClipModel clip2 = new ShapeClipModel {
                    Width = 100, Height = 1000,
                    FrameSpan = new FrameSpan(15, 130),
                    DisplayName = "Clip colour_green"
                };

                clip2.AutomationData[VideoClipModel.MediaPositionKey].AddKeyFrame(new KeyFrameVector2(10L, Vector2.Zero));
                clip2.AutomationData[VideoClipModel.MediaPositionKey].AddKeyFrame(new KeyFrameVector2(75L, new Vector2(100, 200)));
                clip2.AutomationData[VideoClipModel.MediaPositionKey].AddKeyFrame(new KeyFrameVector2(90L, new Vector2(400, 400)));
                clip2.AutomationData[VideoClipModel.MediaPositionKey].AddKeyFrame(new KeyFrameVector2(115L, new Vector2(100, 700)));
                clip2.AutomationData.ActiveKeyFullId = VideoClipModel.MediaPositionKey.FullId;

                clip2.MediaPosition = new Vector2(400, 400);
                clip2.SetTargetResourceId("colour_green");
                track2.AddClip(clip2);
            }
            {
                VideoTrackModel track1 = new VideoTrackModel(project.Timeline) {
                    DisplayName = "Empty track"
                };
                project.Timeline.AddTrack(track1);
            }

            return project;
        }

         public static ProjectModel CreateDebugProject() {
            // Debug project - test a lot of features and make sure they work
            ProjectModel project = new ProjectModel();
            project.Settings.Resolution = new Resolution(1920, 1080);

            {
                ResourceManager manager = project.ResourceManager;
                manager.RegisterEntry("colour_red", manager.RootGroup.Add(new ResourceColour(220, 25, 25)));
                manager.RegisterEntry("colour_green", manager.RootGroup.Add(new ResourceColour(25, 220, 25)));
                manager.RegisterEntry("colour_blue", manager.RootGroup.Add(new ResourceColour(25, 25, 220)));

                ResourceGroup group = new ResourceGroup("Extra Colours");
                manager.RootGroup.AddItemToList(group);
                manager.RegisterEntry("white colour", group.Add(new ResourceColour(220, 220, 220)));
                manager.RegisterEntry("idek", group.Add(new ResourceColour(50, 100, 220)));
            }

            {
                VideoTrackModel track = new VideoTrackModel(project.Timeline) {
                    DisplayName = "Track 1 with stuff"
                };
                project.Timeline.AddTrack(track);

                track.AutomationData[VideoTrackModel.OpacityKey].AddKeyFrame(new KeyFrameDouble(0, 0.3d));
                track.AutomationData[VideoTrackModel.OpacityKey].AddKeyFrame(new KeyFrameDouble(50, 0.5d));
                track.AutomationData[VideoTrackModel.OpacityKey].AddKeyFrame(new KeyFrameDouble(100, 1d));
                track.AutomationData.ActiveKeyFullId = VideoTrackModel.OpacityKey.FullId;

                ShapeClipModel clip1 = new ShapeClipModel {
                    Width = 200, Height = 200,
                    FrameSpan = new FrameSpan(0, 120),
                    DisplayName = "Clip colour_red"
                };

                clip1.MediaPosition = new Vector2(0, 0);
                clip1.SetTargetResourceId("idek");
                track.AddClip(clip1);

                ShapeClipModel clip2 = new ShapeClipModel {
                    Width = 200, Height = 200,
                    FrameSpan = new FrameSpan(150, 30),
                    DisplayName = "Clip colour_green"
                };
                clip2.MediaPosition = new Vector2(200, 200);
                clip2.SetTargetResourceId("colour_green");

                track.AddClip(clip2);
            }
            {
                VideoTrackModel track = new VideoTrackModel(project.Timeline) {
                    DisplayName = "Track 2"
                };
                project.Timeline.AddTrack(track);

                ShapeClipModel clip1 = new ShapeClipModel {
                    Width = 400, Height = 400,
                    FrameSpan = new FrameSpan(300, 90),
                    DisplayName = "Clip colour_blue"
                };

                clip1.MediaPosition = new Vector2(200, 200);
                clip1.SetTargetResourceId("colour_blue");
                track.AddClip(clip1);
                ShapeClipModel clip2 = new ShapeClipModel {
                    Width = 100, Height = 1000,
                    FrameSpan = new FrameSpan(15, 130),
                    DisplayName = "Clip colour_green"
                };

                clip2.AutomationData[VideoClipModel.MediaPositionKey].AddKeyFrame(new KeyFrameVector2(10L, Vector2.Zero));
                clip2.AutomationData[VideoClipModel.MediaPositionKey].AddKeyFrame(new KeyFrameVector2(75L, new Vector2(100, 200)));
                clip2.AutomationData[VideoClipModel.MediaPositionKey].AddKeyFrame(new KeyFrameVector2(90L, new Vector2(400, 400)));
                clip2.AutomationData[VideoClipModel.MediaPositionKey].AddKeyFrame(new KeyFrameVector2(115L, new Vector2(100, 700)));
                clip2.AutomationData.ActiveKeyFullId = VideoClipModel.MediaPositionKey.FullId;

                clip2.MediaPosition = new Vector2(400, 400);
                clip2.SetTargetResourceId("colour_green");
                track.AddClip(clip2);
            }
            {
                VideoTrackModel track = new VideoTrackModel(project.Timeline) {
                    DisplayName = "Empty track"
                };
                project.Timeline.AddTrack(track);
            }
            {
                AudioTrackModel track = new AudioTrackModel(project.Timeline) {
                    DisplayName = "Audio Track 1"
                };
                project.Timeline.AddTrack(track);
                SinewaveClipModel clip = new SinewaveClipModel() {
                    FrameSpan = new FrameSpan(300, 90),
                    DisplayName = "Clip Sine"
                };

                track.AddClip(clip);
            }

            return project;
        }
    }
}
