using System;
using System.Diagnostics;
using System.Threading.Tasks;
using FramePFX.Core.Editor.Notifications;
using FramePFX.Core.Editor.ResourceChecker;
using FramePFX.Core.History.ViewModels;
using FramePFX.Core.RBC;
using FramePFX.Core.Utils;
using FramePFX.Core.Views.Dialogs;

namespace FramePFX.Core.Editor.ViewModels {
    /// <summary>
    /// A view model that represents a video editor
    /// </summary>
    public class VideoEditorViewModel : BaseViewModel, IDisposable {
        private ProjectViewModel activeProject;

        /// <summary>
        /// The project that is currently being edited in this editor. May be null if no project is loaded
        /// </summary>
        public ProjectViewModel ActiveProject {
            get => this.activeProject;
            private set {
                if (ReferenceEquals(this.activeProject, value))
                    return;
                if (this.activeProject != null) {
                    this.activeProject.Editor = null;
                }

                if (value != null) {
                    this.Model.ActiveProject = value.Model;
                    value.Editor = this; // this also sets the project model's editor
                }
                else {
                    this.Model.ActiveProject = null;
                }

                this.RaisePropertyChanged(ref this.activeProject, value);
            }
        }

        public bool IsProjectSaving {
            get => this.Model.IsProjectSaving;
            private set {
                this.Model.IsProjectSaving = value;
                this.RaisePropertyChanged();
            }
        }

        private bool isClosingProject;
        public bool IsClosingProject {
            get => this.isClosingProject;
            set => this.RaisePropertyChanged(ref this.isClosingProject, value);
        }

        public EditorPlaybackViewModel Playback { get; }

        public VideoEditorModel Model { get; }

        public IVideoEditor View { get; }

        public AsyncRelayCommand NewProjectCommand { get; }

        public HistoryManagerViewModel HistoryManager { get; }

        public AsyncRelayCommand OpenProjectCommand { get; }

        private SavingProjectNotification notification;

        public VideoEditorViewModel(IVideoEditor view) {
            this.View = view ?? throw new ArgumentNullException(nameof(view));
            this.Model = new VideoEditorModel();
            this.HistoryManager = new HistoryManagerViewModel(view.NotificationPanel, this.Model.HistoryManager);
            this.Playback = new EditorPlaybackViewModel(this);
            this.Playback.ProjectModified += this.OnProjectModified;
            this.Playback.Model.OnStepFrame = () => this.ActiveProject?.Timeline.OnStepFrameTick();
            this.NewProjectCommand = new AsyncRelayCommand(this.NewProjectAction);
            this.OpenProjectCommand = new AsyncRelayCommand(this.OpenProjectAction);
        }

        private void OnProjectModified(object sender, string property) {
            this.activeProject?.OnProjectModified(sender, property);
        }

        private async Task NewProjectAction() {
            if (this.ActiveProject != null && !await this.PromptSaveProjectAction()) {
                return;
            }

            ProjectViewModel project = new ProjectViewModel(new ProjectModel());
            project.Settings.Resolution = new Resolution(1280, 720);
            await this.CloseProjectAction();
            await this.SetProject(project);
            this.ActiveProject.SetHasUnsavedChanges(false);
        }

        public async Task OpenProjectAction() {
            DialogResult<string[]> result = IoC.FilePicker.OpenFiles(Filters.ProjectTypeAndAllFiles, null, "Select a project file to open");
            if (!result.IsSuccess || result.Value.Length < 1) {
                return;
            }

            if (this.ActiveProject != null && !await this.PromptSaveProjectAction()) {
                return;
            }

            #if DEBUG
            RBEBase rbe = RBEUtils.ReadFromFilePacked(result.Value[0]);
            RBEDictionary dictionary = (RBEDictionary) rbe;
            ProjectModel projectModel = new ProjectModel();
            projectModel.ReadFromRBE(dictionary);
            ProjectViewModel project = new ProjectViewModel(projectModel) {ProjectFilePath = result.Value[0]};
            #else
            RBEDictionary dictionary;
            try {
                dictionary = RBEUtils.ReadFromFilePacked(result.Value[0]) as RBEDictionary;
            }
            catch (Exception e) {
                await IoC.MessageDialogs.ShowMessageExAsync("Read error", "Failed to read project from file", e.GetToString());
                return;
            }
            if (dictionary == null) {
                await IoC.MessageDialogs.ShowMessageAsync("Invalid project", "The project contains invalid data (non RBEDictionary)");
                return;
            }
            ProjectModel projectModel = new ProjectModel();
            ProjectViewModel project;
            try {
                projectModel.ReadFromRBE(dictionary);
                project = new ProjectViewModel(projectModel);
            }
            catch (Exception e) {
                await IoC.MessageDialogs.ShowMessageExAsync("Project load error", "Failed to load project", e.GetToString());
                return;
            }
            #endif

            if (this.ActiveProject != null) {
                try {
                    await this.CloseProjectAction();
                }
                catch (Exception e) {
                    await IoC.MessageDialogs.ShowMessageExAsync("Exception", "Failed to close previous project. This error can be ignored", e.GetToString());
                }

                this.ActiveProject = null;
            }

            if (!await ResourceCheckerViewModel.ProcessProjectForInvalidResources(project, true)) {
                #if DEBUG
                project.Dispose();
                #else
                try {
                    project.Dispose();
                }
                catch (Exception e) {
                    await IoC.MessageDialogs.ShowMessageExAsync("Failed to close project", "...", e.GetToString());
                }
                #endif
                return;
            }

            await this.SetProject(project);
            this.ActiveProject.SetHasUnsavedChanges(false);
            project.HasSavedOnce = true;
        }

        public async Task SetProject(ProjectViewModel project) {
            await this.Playback.OnProjectChanging(project);
            this.ActiveProject = project;
            await this.Playback.OnProjectChanged(project);
        }

        public void Dispose() {
            using (ExceptionStack stack = new ExceptionStack("Exception occurred while disposing video editor")) {
                if (this.ActiveProject != null) {
                    try {
                        this.ActiveProject.Dispose();
                    }
                    catch (Exception e) {
                        stack.Add(new Exception("Exception disposing active project", e));
                    }
                }

                try {
                    this.Playback.Dispose();
                }
                catch (Exception e) {
                    stack.Add(new Exception("Exception disposing playback", e));
                }
            }
        }

        /// <summary>
        /// Saves and closes the project
        /// </summary>
        /// <returns>True when the project was closed,</returns>
        public async Task<bool> PromptSaveAndCloseProjectAction() {
            if (this.ActiveProject == null) {
                throw new Exception("No active project");
            }

            if (!await this.PromptSaveProjectAction()) {
                return false;
            }

            this.IsClosingProject = true;
            await this.CloseProjectAction();
            this.IsClosingProject = false;
            return true;
        }

        public async Task<bool> PromptSaveProjectAction() {
            if (this.ActiveProject == null) {
                throw new Exception("No active project");
            }

            // if (!this.ActiveProject.HasUnsavedChanges) {
            //     return true;
            // }

            bool? result = await IoC.MessageDialogs.ShowYesNoCancelDialogAsync("Save project", "Do you want to save the current project first?");
            if (result == true) {
                await this.ActiveProject.SaveActionAsync();
            }
            else if (result == null) {
                return false;
            }

            return true;
        }

        public async Task CloseProjectAction() {
            if (this.ActiveProject == null) {
                throw new Exception("No active project");
            }

            try {
                this.ActiveProject.Dispose();
            }
            catch (Exception e) {
                await IoC.MessageDialogs.ShowMessageExAsync("Close Project", "An exception occurred while closing project", e.GetToString());
            }

            await this.SetProject(null);
        }

        public async Task OnProjectSaving() {
            this.IsProjectSaving = true;
            await this.Playback.OnProjectSaving();
            this.notification = new SavingProjectNotification();
            this.View.NotificationPanel.PushNotification(this.notification, false);
            this.notification.BeginSave();
        }

        public async Task OnProjectSaved(bool success = true) {
            this.IsProjectSaving = false;
            await this.Playback.OnProjectSaved(success);
            if (success) {
                this.notification.OnSaveComplete();
            }
            else {
                this.notification.OnSaveFailed("TODO implement this lol");
            }

            this.notification.Timeout = TimeSpan.FromSeconds(5);
            this.notification.StartAutoHideTask();
            this.notification = null;
        }

        public void DoRender(bool schedule = false) {
            this.View.RenderViewPort(schedule);
        }
    }
}