using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FramePFX.Core.Actions;
using FramePFX.Core.Actions.Contexts;
using FramePFX.Core.AdvancedContextService;
using FramePFX.Core.Utils;
using FramePFX.Shortcuts.Converters;

namespace FramePFX.AdvancedContextService {
    public class AdvancedActionMenuItem : AdvancedMenuItem {
        public static readonly DependencyProperty ActionIdProperty =
            DependencyProperty.Register(
                "ActionId",
                typeof(string),
                typeof(AdvancedActionMenuItem),
                new FrameworkPropertyMetadata(null, (d, e) => ((AdvancedActionMenuItem) d).UpdateVisuals()));

        public static readonly DependencyProperty InvokeActionAfterBaseClickProperty =
            DependencyProperty.Register(
                "InvokeActionAfterBaseClick",
                typeof(bool),
                typeof(AdvancedActionMenuItem),
                new PropertyMetadata(BoolBox.True));

        public static readonly DependencyProperty IsActionExecutionEnabledProperty =
            DependencyProperty.Register(
                "IsActionExecutionEnabled",
                typeof(bool),
                typeof(AdvancedActionMenuItem),
                new PropertyMetadata(BoolBox.True));

        public static readonly DependencyProperty AutoGenerateDetailsProperty =
            DependencyProperty.Register(
                "AutoGenerateDetails",
                typeof(bool),
                typeof(AdvancedActionMenuItem),
                new PropertyMetadata(BoolBox.True));

        public string ActionId {
            get => (string) this.GetValue(ActionIdProperty);
            set => this.SetValue(ActionIdProperty, value);
        }

        public bool InvokeActionAfterBaseClick {
            get => (bool) this.GetValue(InvokeActionAfterBaseClickProperty);
            set => this.SetValue(InvokeActionAfterBaseClickProperty, value.Box());
        }

        /// <summary>
        /// Gets or sets whether this menu item should attempt to execute the underlying action specified by <see cref="ActionId"/>
        /// <para>
        /// Setting this to false is useful if you just want to have the header/tooltip/gesture
        /// update, but still use a command to execute some sort of action
        /// </para>
        /// </summary>
        public bool IsActionExecutionEnabled {
            get => (bool) this.GetValue(IsActionExecutionEnabledProperty);
            set => this.SetValue(IsActionExecutionEnabledProperty, value.Box());
        }

        public bool AutoGenerateDetails {
            get => (bool) this.GetValue(AutoGenerateDetailsProperty);
            set => this.SetValue(AutoGenerateDetailsProperty, value.Box());
        }

        // probably doesn't even need to be volatile, a bool will be fine
        public bool IsExecuting { get; private set; }

        private bool canExecute;
        protected bool CanExecute {
            get => this.canExecute;
            set {
                this.canExecute = value;

                // Causes IsEnableCore to be fetched, which returns false if we are executing something or
                // we have no valid action, causing this menu item to be "disabled"
                this.CoerceValue(IsEnabledProperty);
            }
        }

        private bool hasFirstLoad;
        private bool hasExplicitGesture;

        public AdvancedActionMenuItem() {
            this.Loaded += this.OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e) {
            this.UpdateVisuals();
            if (!this.AutoGenerateDetails) {
                // this.CoerceValue(ActionIdProperty);
                return;
            }

            string id = this.ActionId;
            if (string.IsNullOrEmpty(id))
                return;

            AnAction action = ActionManager.Instance.GetAction(id);
            if (action == null)
                return;

            bool hasNoGesture = this.IsValueUnset(InputGestureTextProperty);
            if (!this.hasFirstLoad) {
                this.hasExplicitGesture = !hasNoGesture;
            }

            if (!this.hasExplicitGesture && (hasNoGesture || this.hasFirstLoad)) {
                if (ActionIdToGestureConverter.ActionIdToGesture(id, null, out string value)) {
                    this.SetCurrentValue(InputGestureTextProperty, value);
                }
            }

            if (!this.hasFirstLoad) {
                this.hasFirstLoad = true;
            }
        }

        protected bool IsValueUnset(DependencyProperty property) {
            object value = this.GetValue(property);
            return !(value is string str) || string.IsNullOrEmpty(str);
        }

        protected override bool IsEnabledCore => base.IsEnabledCore && this.CanExecute;

        protected DataContext GetDataContext(bool includeToggleState = true) {
            DataContext context = new DataContext();
            object dc = this.DataContext;
            if (dc != null) {
                if (dc is IDataContext ctx) {
                    context.Merge(ctx);
                }
                else if (dc is BaseContextEntry entry) {
                    context.Merge(entry.Context);
                }
                else {
                    context.AddContext(dc);
                }
            }

            context.AddContext(this);
            IInputElement focused = Keyboard.FocusedElement;
            if (focused != null && !ReferenceEquals(focused, this) && focused is DependencyObject obj) {
                if (obj is FrameworkElement element && element.DataContext is object dc1) {
                    context.AddContext(dc1);
                }

                context.AddContext(obj);
                ItemsControl itemsControl = ItemsControlFromItemContainer(obj);
                if (itemsControl != null && itemsControl.IsItemItsOwnContainer(obj)) {
                    context.AddContext(itemsControl);
                }
            }

            if (Window.GetWindow(this) is Window win) {
                if (win.DataContext is object dc1) {
                    context.AddContext(dc1);
                }

                context.AddContext(win);
            }

            if (includeToggleState && this.IsCheckable) {
                context.Set(ToggleAction.IsToggledKey, this.IsChecked.Box());
            }

            return context;
        }

        public void UpdateVisuals() {
            if (!this.IsLoaded)
                return;

            if (this.IsExecuting) {
                this.CanExecute = false;
            }
            else if (!this.IsActionExecutionEnabled) {
                this.CanExecute = true;
            }
            else {
                string id = this.ActionId;
                if (string.IsNullOrEmpty(id)) {
                    this.CanExecute = false;
                }
                else {
                    DataContext context = this.GetDataContext();
                    this.CanExecute = ActionManager.Instance.CanExecute(id, context);
                }
            }
        }

        protected override void OnClick() {
            // Originally used a binding to bind this menu item's command to an ActionContextEntry's
            // internal command, but you lose the ability to access Keyboard.FocusedElement, so it's
            // better to just handle the click manually
            // context should not be an instance of CommandContextEntry... but just in case
            // if (this.DataContext is CommandContextEntry || this.DataContext is ActionContextEntry) {
            //     base.OnClick(); // clicking is handled in the entry
            //     return;
            // }

            if (this.IsExecuting) {
                this.CanExecute = false;
                return;
            }

            this.IsExecuting = true;
            string id = this.ActionId;
            if (string.IsNullOrEmpty(id) || !this.IsActionExecutionEnabled) {
                base.OnClick();
                this.IsExecuting = false;
                this.UpdateVisuals();
                return;
            }

            this.CanExecute = false;
            if (this.InvokeActionAfterBaseClick) {
                // true by default, and ToggleActions would behave weirdly if this was false
                base.OnClick();
                this.DispatchAction(id);
            }
            else {
                this.DispatchAction(id);
                base.OnClick();
            }
        }

        private void DispatchAction(string id) {
            DataContext context = this.GetDataContext();
            this.Dispatcher.InvokeAsync(async () => {
                try {
                    await ActionManager.Instance.Execute(id, context);
                }
                finally {
                    this.IsExecuting = false;
                    this.UpdateVisuals();
                }
            }, DispatcherPriority.Render);
        }
    }
}