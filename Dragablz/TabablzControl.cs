﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Dragablz.Core;
using Dragablz.Dockablz;
using Dragablz.Referenceless;

namespace Dragablz {
    //original code specific to keeping visual tree "alive" sourced from http://stackoverflow.com/questions/12432062/binding-to-itemssource-of-tabcontrol-in-wpf    

    /// <summary>
    /// Extended tab control which supports tab repositioning, and drag and drop.  Also 
    /// uses the common WPF technique for pesisting the visual tree across tabs.
    /// </summary>
    [TemplatePart(Name = HeaderItemsControlPartName, Type = typeof(DragablzItemsControl))]
    [TemplatePart(Name = ItemsHolderPartName, Type = typeof(Panel))]
    public class TabablzControl : TabControl {
        /// <summary>
        /// Template part.
        /// </summary>
        public const string HeaderItemsControlPartName = "PART_HeaderItemsControl";

        /// <summary>
        /// Template part.
        /// </summary>
        public const string ItemsHolderPartName = "PART_ItemsHolder";

        /// <summary>
        /// Routed command which can be used to close a tab.
        /// </summary>
        public static RoutedCommand CloseItemCommand = new RoutedUICommand("Close", "Close", typeof(TabablzControl));

        /// <summary>
        /// Routed command which can be used to add a new tab.  See <see cref="NewItemFactory"/>.
        /// </summary>
        public static RoutedCommand AddItemCommand = new RoutedUICommand("Add", "Add", typeof(TabablzControl));

        private static readonly HashSet<TabablzControl> LoadedInstances = new HashSet<TabablzControl>();
        private static readonly HashSet<TabablzControl> VisibleInstances = new HashSet<TabablzControl>();

        private Panel _itemsHolder;
        private TabHeaderDragStartInformation _tabHeaderDragStartInformation;
        private WeakReference _previousSelection;
        private DragablzItemsControl _dragablzItemsControl;
        private IDisposable _templateSubscription;
        private readonly SerialDisposable _windowSubscription = new SerialDisposable();

        private InterTabTransfer _interTabTransfer;

        static TabablzControl() {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(TabablzControl), new FrameworkPropertyMetadata(typeof(TabablzControl)));
            CommandManager.RegisterClassCommandBinding(typeof(FrameworkElement), new CommandBinding(CloseItemCommand, CloseItemClassHandler, CloseItemCanExecuteClassHandler));
        }

        /// <summary>
        /// Default constructor.
        /// </summary>
        public TabablzControl() {
            this.AddHandler(DragablzItem.DragStarted, new DragablzDragStartedEventHandler(this.ItemDragStarted), true);
            this.AddHandler(DragablzItem.PreviewDragDelta, new DragablzDragDeltaEventHandler(this.PreviewItemDragDelta), true);
            this.AddHandler(DragablzItem.DragDelta, new DragablzDragDeltaEventHandler(this.ItemDragDelta), true);
            this.AddHandler(DragablzItem.DragCompleted, new DragablzDragCompletedEventHandler(this.ItemDragCompleted), true);
            this.CommandBindings.Add(new CommandBinding(AddItemCommand, this.AddItemHandler));

            this.Loaded += this.OnLoaded;
            this.Unloaded += this.OnUnloaded;
            this.IsVisibleChanged += OnIsVisibleChanged;
        }

        public static readonly DependencyProperty CustomHeaderItemStyleProperty = DependencyProperty.Register(
            "CustomHeaderItemStyle", typeof(Style), typeof(TabablzControl), new PropertyMetadata(default(Style)));

        /// <summary>
        /// Helper method which returns all the currently loaded instances.
        /// </summary>
        /// <returns></returns>
        public static IEnumerable<TabablzControl> GetLoadedInstances() {
            return LoadedInstances.Union(VisibleInstances).Distinct().ToList();
        }

        /// <summary>
        /// Helper method to close all tabs where the item is the tab's content (helpful with MVVM scenarios)
        /// </summary>
        /// <remarks>
        /// In MVVM scenarios where you don't want to bind the routed command to your ViewModel,
        /// with this helper method and embedding the TabablzControl in a UserControl, you can keep
        /// the View-specific dependencies out of the ViewModel.
        /// </remarks>
        /// <param name="tabContentItem">An existing Tab item content (a ViewModel in MVVM scenarios) which is backing a tab control</param>
        public static void CloseItem(object tabContentItem) {
            if (tabContentItem == null)
                return; //Do nothing.

            //Find all loaded TabablzControl instances with tabs backed by this item and close them
            foreach (var tabWithItemContent in
                GetLoadedInstances().SelectMany(tc =>
                    tc._dragablzItemsControl.DragablzItems().Where(di => di.Content.Equals(tabContentItem)).Select(di => new {tc, di}))) {
                CloseItem(tabWithItemContent.di, tabWithItemContent.tc);
            }
        }

        /// <summary>
        /// Helper method to add an item next to an existing item.
        /// </summary>
        /// <remarks>
        /// Due to the organisable nature of the control, the order of items may not reflect the order in the source collection.  This method
        /// will add items to the source collection, managing their initial appearance on screen at the same time. 
        /// If you are using a <see cref="InterTabController.InterTabClient"/> this will be used to add the item into the source collection.
        /// </remarks>
        /// <param name="item">New item to add.</param>
        /// <param name="nearItem">Existing object/tab item content which defines which tab control should be used to add the object.</param>
        /// <param name="addLocationHint">Location, relative to the <paramref name="nearItem"/> object</param>
        public static void AddItem(object item, object nearItem, AddLocationHint addLocationHint) {
            if (nearItem == null)
                throw new ArgumentNullException("nearItem");

            var existingLocation = GetLoadedInstances().SelectMany(tabControl =>
                (tabControl.ItemsSource ?? tabControl.Items).OfType<object>().Select(existingObject => new {tabControl, existingObject})).SingleOrDefault(a => nearItem.Equals(a.existingObject));

            if (existingLocation == null)
                throw new ArgumentException("Did not find precisely one instance of adjacentTo", "nearItem");

            existingLocation.tabControl.AddToSource(item);
            if (existingLocation.tabControl._dragablzItemsControl != null)
                existingLocation.tabControl._dragablzItemsControl.MoveItem(new MoveItemRequest(item, nearItem, addLocationHint));
        }

        /// <summary>
        /// Finds and selects an item.
        /// </summary>
        /// <param name="item"></param>
        public static void SelectItem(object item) {
            var existingLocation = GetLoadedInstances().SelectMany(tabControl =>
                (tabControl.ItemsSource ?? tabControl.Items).OfType<object>().Select(existingObject => new {tabControl, existingObject})).FirstOrDefault(a => item.Equals(a.existingObject));

            if (existingLocation == null)
                return;

            existingLocation.tabControl.SelectedItem = item;
        }

        /// <summary>
        /// Style to apply to header items which are not their own item container (<see cref="TabItem"/>).  Typically items bound via the <see cref="ItemsSource"/> will use this style.
        /// </summary>
        [Obsolete]
        public Style CustomHeaderItemStyle {
            get { return (Style) this.GetValue(CustomHeaderItemStyleProperty); }
            set { this.SetValue(CustomHeaderItemStyleProperty, value); }
        }

        public static readonly DependencyProperty CustomHeaderItemTemplateProperty = DependencyProperty.Register(
            "CustomHeaderItemTemplate", typeof(DataTemplate), typeof(TabablzControl), new PropertyMetadata(default(DataTemplate)));

        [Obsolete("Prefer HeaderItemTemplate")]
        public DataTemplate CustomHeaderItemTemplate {
            get { return (DataTemplate) this.GetValue(CustomHeaderItemTemplateProperty); }
            set { this.SetValue(CustomHeaderItemTemplateProperty, value); }
        }

        public static readonly DependencyProperty DefaultHeaderItemStyleProperty = DependencyProperty.Register(
            "DefaultHeaderItemStyle", typeof(Style), typeof(TabablzControl), new PropertyMetadata(default(Style)));

        [Obsolete]
        public Style DefaultHeaderItemStyle {
            get { return (Style) this.GetValue(DefaultHeaderItemStyleProperty); }
            set { this.SetValue(DefaultHeaderItemStyleProperty, value); }
        }

        public static readonly DependencyProperty AdjacentHeaderItemOffsetProperty = DependencyProperty.Register(
            "AdjacentHeaderItemOffset", typeof(double), typeof(TabablzControl), new PropertyMetadata(default(double), AdjacentHeaderItemOffsetPropertyChangedCallback));

        private static void AdjacentHeaderItemOffsetPropertyChangedCallback(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs dependencyPropertyChangedEventArgs) {
            dependencyObject.SetValue(HeaderItemsOrganiserProperty, new HorizontalOrganiser((double) dependencyPropertyChangedEventArgs.NewValue));
        }

        public double AdjacentHeaderItemOffset {
            get { return (double) this.GetValue(AdjacentHeaderItemOffsetProperty); }
            set { this.SetValue(AdjacentHeaderItemOffsetProperty, value); }
        }

        public static readonly DependencyProperty HeaderItemsOrganiserProperty = DependencyProperty.Register(
            "HeaderItemsOrganiser", typeof(IItemsOrganiser), typeof(TabablzControl), new PropertyMetadata(new HorizontalOrganiser()));

        public IItemsOrganiser HeaderItemsOrganiser {
            get { return (IItemsOrganiser) this.GetValue(HeaderItemsOrganiserProperty); }
            set { this.SetValue(HeaderItemsOrganiserProperty, value); }
        }

        public static readonly DependencyProperty HeaderMemberPathProperty = DependencyProperty.Register(
            "HeaderMemberPath", typeof(string), typeof(TabablzControl), new PropertyMetadata(default(string)));

        public string HeaderMemberPath {
            get { return (string) this.GetValue(HeaderMemberPathProperty); }
            set { this.SetValue(HeaderMemberPathProperty, value); }
        }

        public static readonly DependencyProperty HeaderItemTemplateProperty = DependencyProperty.Register(
            "HeaderItemTemplate", typeof(DataTemplate), typeof(TabablzControl), new PropertyMetadata(default(DataTemplate)));

        public DataTemplate HeaderItemTemplate {
            get { return (DataTemplate) this.GetValue(HeaderItemTemplateProperty); }
            set { this.SetValue(HeaderItemTemplateProperty, value); }
        }

        public static readonly DependencyProperty HeaderPrefixContentProperty = DependencyProperty.Register(
            "HeaderPrefixContent", typeof(object), typeof(TabablzControl), new PropertyMetadata(default(object)));

        public object HeaderPrefixContent {
            get { return (object) this.GetValue(HeaderPrefixContentProperty); }
            set { this.SetValue(HeaderPrefixContentProperty, value); }
        }

        public static readonly DependencyProperty HeaderPrefixContentStringFormatProperty = DependencyProperty.Register(
            "HeaderPrefixContentStringFormat", typeof(string), typeof(TabablzControl), new PropertyMetadata(default(string)));

        public string HeaderPrefixContentStringFormat {
            get { return (string) this.GetValue(HeaderPrefixContentStringFormatProperty); }
            set { this.SetValue(HeaderPrefixContentStringFormatProperty, value); }
        }

        public static readonly DependencyProperty HeaderPrefixContentTemplateProperty = DependencyProperty.Register(
            "HeaderPrefixContentTemplate", typeof(DataTemplate), typeof(TabablzControl), new PropertyMetadata(default(DataTemplate)));

        public DataTemplate HeaderPrefixContentTemplate {
            get { return (DataTemplate) this.GetValue(HeaderPrefixContentTemplateProperty); }
            set { this.SetValue(HeaderPrefixContentTemplateProperty, value); }
        }

        public static readonly DependencyProperty HeaderPrefixContentTemplateSelectorProperty = DependencyProperty.Register(
            "HeaderPrefixContentTemplateSelector", typeof(DataTemplateSelector), typeof(TabablzControl), new PropertyMetadata(default(DataTemplateSelector)));

        public DataTemplateSelector HeaderPrefixContentTemplateSelector {
            get { return (DataTemplateSelector) this.GetValue(HeaderPrefixContentTemplateSelectorProperty); }
            set { this.SetValue(HeaderPrefixContentTemplateSelectorProperty, value); }
        }

        public static readonly DependencyProperty HeaderSuffixContentProperty = DependencyProperty.Register(
            "HeaderSuffixContent", typeof(object), typeof(TabablzControl), new PropertyMetadata(default(object)));

        public object HeaderSuffixContent {
            get { return (object) this.GetValue(HeaderSuffixContentProperty); }
            set { this.SetValue(HeaderSuffixContentProperty, value); }
        }

        public static readonly DependencyProperty HeaderSuffixContentStringFormatProperty = DependencyProperty.Register(
            "HeaderSuffixContentStringFormat", typeof(string), typeof(TabablzControl), new PropertyMetadata(default(string)));

        public string HeaderSuffixContentStringFormat {
            get { return (string) this.GetValue(HeaderSuffixContentStringFormatProperty); }
            set { this.SetValue(HeaderSuffixContentStringFormatProperty, value); }
        }

        public static readonly DependencyProperty HeaderSuffixContentTemplateProperty = DependencyProperty.Register(
            "HeaderSuffixContentTemplate", typeof(DataTemplate), typeof(TabablzControl), new PropertyMetadata(default(DataTemplate)));

        public DataTemplate HeaderSuffixContentTemplate {
            get { return (DataTemplate) this.GetValue(HeaderSuffixContentTemplateProperty); }
            set { this.SetValue(HeaderSuffixContentTemplateProperty, value); }
        }

        public static readonly DependencyProperty HeaderSuffixContentTemplateSelectorProperty = DependencyProperty.Register(
            "HeaderSuffixContentTemplateSelector", typeof(DataTemplateSelector), typeof(TabablzControl), new PropertyMetadata(default(DataTemplateSelector)));

        public DataTemplateSelector HeaderSuffixContentTemplateSelector {
            get { return (DataTemplateSelector) this.GetValue(HeaderSuffixContentTemplateSelectorProperty); }
            set { this.SetValue(HeaderSuffixContentTemplateSelectorProperty, value); }
        }

        public static readonly DependencyProperty ShowDefaultCloseButtonProperty = DependencyProperty.Register(
            "ShowDefaultCloseButton", typeof(bool), typeof(TabablzControl), new PropertyMetadata(default(bool)));

        /// <summary>
        /// Indicates whether a default close button should be displayed.  If manually templating the tab header content the close command 
        /// can be called by executing the <see cref="TabablzControl.CloseItemCommand"/> command (typically via a <see cref="Button"/>).
        /// </summary>
        public bool ShowDefaultCloseButton {
            get { return (bool) this.GetValue(ShowDefaultCloseButtonProperty); }
            set { this.SetValue(ShowDefaultCloseButtonProperty, value); }
        }

        public static readonly DependencyProperty ShowDefaultAddButtonProperty = DependencyProperty.Register(
            "ShowDefaultAddButton", typeof(bool), typeof(TabablzControl), new PropertyMetadata(default(bool)));

        /// <summary>
        /// Indicates whether a default add button should be displayed.  Alternately an add button
        /// could be added in <see cref="HeaderPrefixContent"/> or <see cref="HeaderSuffixContent"/>, utilising 
        /// <see cref="AddItemCommand"/>.
        /// </summary>
        public bool ShowDefaultAddButton {
            get { return (bool) this.GetValue(ShowDefaultAddButtonProperty); }
            set { this.SetValue(ShowDefaultAddButtonProperty, value); }
        }

        public static readonly DependencyProperty IsHeaderPanelVisibleProperty = DependencyProperty.Register(
            "IsHeaderPanelVisible", typeof(bool), typeof(TabablzControl), new PropertyMetadata(true));

        /// <summary>
        /// Indicates wither the heaeder panel is visible.  Default is <c>true</c>.
        /// </summary>
        public bool IsHeaderPanelVisible {
            get { return (bool) this.GetValue(IsHeaderPanelVisibleProperty); }
            set { this.SetValue(IsHeaderPanelVisibleProperty, value); }
        }

        public static readonly DependencyProperty AddLocationHintProperty = DependencyProperty.Register(
            "AddLocationHint", typeof(AddLocationHint), typeof(TabablzControl), new PropertyMetadata(AddLocationHint.Last));

        /// <summary>
        /// Gets or sets the location to add new tab items in the header.
        /// </summary>
        /// <remarks>
        /// The logical order of the header items might not add match the content of the source items,
        /// so this property allows control of where new items should appear.
        /// </remarks>
        public AddLocationHint AddLocationHint {
            get { return (AddLocationHint) this.GetValue(AddLocationHintProperty); }
            set { this.SetValue(AddLocationHintProperty, value); }
        }

        public static readonly DependencyProperty FixedHeaderCountProperty = DependencyProperty.Register(
            "FixedHeaderCount", typeof(int), typeof(TabablzControl), new PropertyMetadata(default(int)));

        /// <summary>
        /// Allows a the first adjacent tabs to be fixed (no dragging, and default close button will not show).
        /// </summary>
        public int FixedHeaderCount {
            get { return (int) this.GetValue(FixedHeaderCountProperty); }
            set { this.SetValue(FixedHeaderCountProperty, value); }
        }

        public static readonly DependencyProperty InterTabControllerProperty = DependencyProperty.Register(
            "InterTabController", typeof(InterTabController), typeof(TabablzControl), new PropertyMetadata(null, InterTabControllerPropertyChangedCallback));

        private static void InterTabControllerPropertyChangedCallback(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs dependencyPropertyChangedEventArgs) {
            var instance = (TabablzControl) dependencyObject;
            if (dependencyPropertyChangedEventArgs.OldValue != null)
                instance.RemoveLogicalChild(dependencyPropertyChangedEventArgs.OldValue);
            if (dependencyPropertyChangedEventArgs.NewValue != null)
                instance.AddLogicalChild(dependencyPropertyChangedEventArgs.NewValue);
        }

        /// <summary>
        /// An <see cref="InterTabController"/> must be provided to enable tab tearing. Behaviour customisations can be applied
        /// vie the controller.
        /// </summary>
        public InterTabController InterTabController {
            get { return (InterTabController) this.GetValue(InterTabControllerProperty); }
            set { this.SetValue(InterTabControllerProperty, value); }
        }

        /// <summary>
        /// Allows a factory to be provided for generating new items. Typically used in conjunction with <see cref="AddItemCommand"/>.
        /// </summary>
        public static readonly DependencyProperty NewItemFactoryProperty = DependencyProperty.Register(
            "NewItemFactory", typeof(Func<object>), typeof(TabablzControl), new PropertyMetadata(default(Func<object>)));

        /// <summary>
        /// Allows a factory to be provided for generating new items. Typically used in conjunction with <see cref="AddItemCommand"/>.
        /// </summary>
        public Func<object> NewItemFactory {
            get { return (Func<object>) this.GetValue(NewItemFactoryProperty); }
            set { this.SetValue(NewItemFactoryProperty, value); }
        }

        private static readonly DependencyPropertyKey IsEmptyPropertyKey =
            DependencyProperty.RegisterReadOnly(
                "IsEmpty", typeof(bool), typeof(TabablzControl),
                new PropertyMetadata(true, OnIsEmptyChanged));

        /// <summary>
        /// Indicates if there are no current tab items.
        /// </summary>
        public static readonly DependencyProperty IsEmptyProperty =
            IsEmptyPropertyKey.DependencyProperty;

        /// <summary>
        /// Indicates if there are no current tab items.
        /// </summary>
        public bool IsEmpty {
            get { return (bool) this.GetValue(IsEmptyProperty); }
            private set { this.SetValue(IsEmptyPropertyKey, value); }
        }

        /// <summary>
        /// Raised when <see cref="IsEmpty"/> changes.
        /// </summary>
        public static readonly RoutedEvent IsEmptyChangedEvent =
            EventManager.RegisterRoutedEvent(
                "IsEmptyChanged",
                RoutingStrategy.Bubble,
                typeof(RoutedPropertyChangedEventHandler<bool>),
                typeof(TabablzControl));

        /// <summary>
        /// Event handler to list to <see cref="IsEmptyChangedEvent"/>.
        /// </summary>
        public event RoutedPropertyChangedEventHandler<bool> IsEmptyChanged {
            add { this.AddHandler(IsEmptyChangedEvent, value); }
            remove { this.RemoveHandler(IsEmptyChangedEvent, value); }
        }

        private static void OnIsEmptyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var instance = d as TabablzControl;
            var args = new RoutedPropertyChangedEventArgs<bool>(
                (bool) e.OldValue,
                (bool) e.NewValue) {RoutedEvent = IsEmptyChangedEvent};
            instance?.RaiseEvent(args);
        }

        /// <summary>
        /// Optionally allows a close item hook to be bound in.  If this propety is provided, the func must return true for the close to continue.
        /// </summary>
        public static readonly DependencyProperty ClosingItemCallbackProperty = DependencyProperty.Register(
            "ClosingItemCallback", typeof(ItemActionCallback), typeof(TabablzControl), new PropertyMetadata(default(ItemActionCallback)));

        /// <summary>
        /// Optionally allows a close item hook to be bound in.  If this propety is provided, the func must return true for the close to continue.
        /// </summary>
        public ItemActionCallback ClosingItemCallback {
            get { return (ItemActionCallback) this.GetValue(ClosingItemCallbackProperty); }
            set { this.SetValue(ClosingItemCallbackProperty, value); }
        }

        /// <summary>
        /// Set to <c>true</c> to have tabs automatically be moved to another tab is a window is closed, so that they arent lost.
        /// Can be useful for fixed/persistant tabs that may have been dragged into another Window.  You can further control
        /// this behaviour on a per tab item basis by providing <see cref="ConsolidatingOrphanedItemCallback" />.
        /// </summary>
        public static readonly DependencyProperty ConsolidateOrphanedItemsProperty = DependencyProperty.Register(
            "ConsolidateOrphanedItems", typeof(bool), typeof(TabablzControl), new PropertyMetadata(default(bool)));

        /// <summary>
        /// Set to <c>true</c> to have tabs automatically be moved to another tab is a window is closed, so that they arent lost.
        /// Can be useful for fixed/persistant tabs that may have been dragged into another Window.  You can further control
        /// this behaviour on a per tab item basis by providing <see cref="ConsolidatingOrphanedItemCallback" />.
        /// </summary>
        public bool ConsolidateOrphanedItems {
            get { return (bool) this.GetValue(ConsolidateOrphanedItemsProperty); }
            set { this.SetValue(ConsolidateOrphanedItemsProperty, value); }
        }

        /// <summary>
        /// Assuming <see cref="ConsolidateOrphanedItems"/> is set to <c>true</c>, consolidation of individual
        /// tab items can be cancelled by providing this call back and cancelling the <see cref="ItemActionCallbackArgs{TOwner}"/>
        /// instance.
        /// </summary>
        public static readonly DependencyProperty ConsolidatingOrphanedItemCallbackProperty = DependencyProperty.Register(
            "ConsolidatingOrphanedItemCallback", typeof(ItemActionCallback), typeof(TabablzControl), new PropertyMetadata(default(ItemActionCallback)));

        /// <summary>
        /// Assuming <see cref="ConsolidateOrphanedItems"/> is set to <c>true</c>, consolidation of individual
        /// tab items can be cancelled by providing this call back and cancelling the <see cref="ItemActionCallbackArgs{TOwner}"/>
        /// instance.
        /// </summary>
        public ItemActionCallback ConsolidatingOrphanedItemCallback {
            get { return (ItemActionCallback) this.GetValue(ConsolidatingOrphanedItemCallbackProperty); }
            set { this.SetValue(ConsolidatingOrphanedItemCallbackProperty, value); }
        }


        private static readonly DependencyPropertyKey IsDraggingWindowPropertyKey =
            DependencyProperty.RegisterReadOnly(
                "IsDraggingWindow", typeof(bool), typeof(TabablzControl),
                new PropertyMetadata(default(bool), OnIsDraggingWindowChanged));

        /// <summary>
        /// Readonly dependency property which indicates whether the owning <see cref="Window"/> 
        /// is currently dragged 
        /// </summary>
        public static readonly DependencyProperty IsDraggingWindowProperty =
            IsDraggingWindowPropertyKey.DependencyProperty;

        /// <summary>
        /// Readonly dependency property which indicates whether the owning <see cref="Window"/> 
        /// is currently dragged 
        /// </summary>
        public bool IsDraggingWindow {
            get { return (bool) this.GetValue(IsDraggingWindowProperty); }
            private set { this.SetValue(IsDraggingWindowPropertyKey, value); }
        }

        /// <summary>
        /// Event indicating <see cref="IsDraggingWindow"/> has changed.
        /// </summary>
        public static readonly RoutedEvent IsDraggingWindowChangedEvent =
            EventManager.RegisterRoutedEvent(
                "IsDraggingWindowChanged",
                RoutingStrategy.Bubble,
                typeof(RoutedPropertyChangedEventHandler<bool>),
                typeof(TabablzControl));

        /// <summary>
        /// Event indicating <see cref="IsDraggingWindow"/> has changed.
        /// </summary>
        public event RoutedPropertyChangedEventHandler<bool> IsDraggingWindowChanged {
            add { this.AddHandler(IsDraggingWindowChangedEvent, value); }
            remove { this.RemoveHandler(IsDraggingWindowChangedEvent, value); }
        }

        private static void OnIsDraggingWindowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var instance = (TabablzControl) d;
            var args = new RoutedPropertyChangedEventArgs<bool>(
                (bool) e.OldValue,
                (bool) e.NewValue) {
                RoutedEvent = IsDraggingWindowChangedEvent
            };
            instance.RaiseEvent(args);
        }

        /// <summary>
        /// Temporarily set by the framework if a users drag opration causes a Window to close (e.g if a tab is dragging into another tab).
        /// </summary>
        public static readonly DependencyProperty IsClosingAsPartOfDragOperationProperty = DependencyProperty.RegisterAttached(
            "IsClosingAsPartOfDragOperation", typeof(bool), typeof(TabablzControl), new FrameworkPropertyMetadata(default(bool), FrameworkPropertyMetadataOptions.NotDataBindable));

        internal static void SetIsClosingAsPartOfDragOperation(Window element, bool value) {
            element.SetValue(IsClosingAsPartOfDragOperationProperty, value);
        }

        /// <summary>
        /// Helper method which can tell you if a <see cref="Window"/> is being automatically closed due
        /// to a user instigated drag operation (typically when a single tab is dropped into another window.
        /// </summary>
        /// <param name="element"></param>
        /// <returns></returns>
        public static bool GetIsClosingAsPartOfDragOperation(Window element) {
            return (bool) element.GetValue(IsClosingAsPartOfDragOperationProperty);
        }

        /// <summary>
        /// Provide a hint for how the header should size itself if there are no tabs left (and a Window is still open).
        /// </summary>
        public static readonly DependencyProperty EmptyHeaderSizingHintProperty = DependencyProperty.Register(
            "EmptyHeaderSizingHint", typeof(EmptyHeaderSizingHint), typeof(TabablzControl), new PropertyMetadata(default(EmptyHeaderSizingHint)));

        /// <summary>
        /// Provide a hint for how the header should size itself if there are no tabs left (and a Window is still open).
        /// </summary>
        public EmptyHeaderSizingHint EmptyHeaderSizingHint {
            get { return (EmptyHeaderSizingHint) this.GetValue(EmptyHeaderSizingHintProperty); }
            set { this.SetValue(EmptyHeaderSizingHintProperty, value); }
        }

        public static readonly DependencyProperty IsWrappingTabItemProperty = DependencyProperty.RegisterAttached(
            "IsWrappingTabItem", typeof(bool), typeof(TabablzControl), new PropertyMetadata(default(bool)));

        internal static void SetIsWrappingTabItem(DependencyObject element, bool value) {
            element.SetValue(IsWrappingTabItemProperty, value);
        }

        public static bool GetIsWrappingTabItem(DependencyObject element) {
            return (bool) element.GetValue(IsWrappingTabItemProperty);
        }

        /// <summary>
        /// Adds an item to the source collection.  If the InterTabController.InterTabClient is set that instance will be deferred to.
        /// Otherwise an attempt will be made to add to the <see cref="ItemsSource" /> property, and lastly <see cref="Items"/>.
        /// </summary>
        /// <param name="item"></param>
        public void AddToSource(object item) {
            if (item == null)
                throw new ArgumentNullException("item");

            var manualInterTabClient = this.InterTabController == null ? null : this.InterTabController.InterTabClient as IManualInterTabClient;
            if (manualInterTabClient != null) {
                manualInterTabClient.Add(item);
            }
            else {
                CollectionTeaser collectionTeaser;
                if (CollectionTeaser.TryCreate(this.ItemsSource, out collectionTeaser))
                    collectionTeaser.Add(item);
                else
                    this.Items.Add(item);
            }
        }

        /// <summary>
        /// Removes an item from the source collection.  If the InterTabController.InterTabClient is set that instance will be deferred to.
        /// Otherwise an attempt will be made to remove from the <see cref="ItemsSource" /> property, and lastly <see cref="Items"/>.
        /// </summary>
        /// <param name="item"></param>
        public void RemoveFromSource(object item) {
            if (item == null)
                throw new ArgumentNullException("item");

            var manualInterTabClient = this.InterTabController == null ? null : this.InterTabController.InterTabClient as IManualInterTabClient;
            if (manualInterTabClient != null) {
                manualInterTabClient.Remove(item);
            }
            else {
                CollectionTeaser collectionTeaser;
                if (CollectionTeaser.TryCreate(this.ItemsSource, out collectionTeaser))
                    collectionTeaser.Remove(item);
                else
                    this.Items.Remove(item);
            }
        }

        /// <summary>
        /// Gets the header items, ordered according to their current visual position in the tab header.
        /// </summary>
        /// <returns></returns>
        public IEnumerable<DragablzItem> GetOrderedHeaders() {
            return this._dragablzItemsControl.ItemsOrganiser.Sort(this._dragablzItemsControl.DragablzItems());
        }

        /// <summary>
        /// Called when <see cref="M:System.Windows.FrameworkElement.ApplyTemplate"/> is called.
        /// </summary>
        public override void OnApplyTemplate() {
            this._templateSubscription?.Dispose();
            this._templateSubscription = Disposable.Empty;

            this._dragablzItemsControl = this.GetTemplateChild(HeaderItemsControlPartName) as DragablzItemsControl;
            if (this._dragablzItemsControl != null) {
                this._dragablzItemsControl.ItemContainerGenerator.StatusChanged += this.ItemContainerGeneratorOnStatusChanged;
                this._templateSubscription =
                    Disposable.Create(
                        () => this._dragablzItemsControl.ItemContainerGenerator.StatusChanged -= this.ItemContainerGeneratorOnStatusChanged);

                this._dragablzItemsControl.ContainerCustomisations = new ContainerCustomisations(null, this.PrepareChildContainerForItemOverride);
            }

            if (this.SelectedItem == null)
                this.SetCurrentValue(SelectedItemProperty, this.Items.OfType<object>().FirstOrDefault());

            this._itemsHolder = this.GetTemplateChild(ItemsHolderPartName) as Panel;
            this.UpdateSelectedItem();
            this.MarkWrappedTabItems();
            this.MarkInitialSelection();

            base.OnApplyTemplate();
        }

        /// <summary>
        /// update the visible child in the ItemsHolder
        /// </summary>
        /// <param name="e"></param>
        protected override void OnSelectionChanged(SelectionChangedEventArgs e) {
            if (e.RemovedItems.Count > 0 && e.AddedItems.Count > 0)
                this._previousSelection = new WeakReference(e.RemovedItems[0]);

            base.OnSelectionChanged(e);
            this.UpdateSelectedItem();

            if (this._dragablzItemsControl == null)
                return;

            Func<IList, IEnumerable<DragablzItem>> notTabItems =
                l =>
                    l.Cast<object>().Where(o => !(o is TabItem)).Select(o => this._dragablzItemsControl.ItemContainerGenerator.ContainerFromItem(o)).OfType<DragablzItem>();
            foreach (var addedItem in notTabItems(e.AddedItems)) {
                addedItem.IsSelected = true;
                addedItem.BringIntoView();
            }

            foreach (var removedItem in notTabItems(e.RemovedItems)) {
                removedItem.IsSelected = false;
            }

            foreach (var tabItem in e.AddedItems.OfType<TabItem>().Select(t => this._dragablzItemsControl.ItemContainerGenerator.ContainerFromItem(t)).OfType<DragablzItem>()) {
                tabItem.IsSelected = true;
                tabItem.BringIntoView();
            }

            foreach (var tabItem in e.RemovedItems.OfType<TabItem>().Select(t => this._dragablzItemsControl.ItemContainerGenerator.ContainerFromItem(t)).OfType<DragablzItem>()) {
                tabItem.IsSelected = false;
            }
        }

        /// <summary>
        /// when the items change we remove any generated panel children and add any new ones as necessary
        /// </summary>
        /// <param name="e"></param>
        protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e) {
            base.OnItemsChanged(e);

            if (this._itemsHolder == null) {
                return;
            }

            switch (e.Action) {
                case NotifyCollectionChangedAction.Reset:
                    this._itemsHolder.Children.Clear();

                    if (this.Items.Count > 0) {
                        this.SelectedItem = base.Items[0];
                        this.UpdateSelectedItem();
                    }

                    break;

                case NotifyCollectionChangedAction.Add:
                    this.UpdateSelectedItem();
                    if (e.NewItems.Count == 1 && this.Items.Count > 1 && this._dragablzItemsControl != null && this._interTabTransfer == null)
                        this._dragablzItemsControl.MoveItem(new MoveItemRequest(e.NewItems[0], this.SelectedItem, this.AddLocationHint));

                    break;

                case NotifyCollectionChangedAction.Remove:
                    foreach (var item in e.OldItems) {
                        var cp = this.FindChildContentPresenter(item);
                        if (cp != null)
                            this._itemsHolder.Children.Remove(cp);
                    }

                    if (this.SelectedItem == null)
                        this.RestorePreviousSelection();
                    this.UpdateSelectedItem();
                    break;

                case NotifyCollectionChangedAction.Replace: throw new NotImplementedException("Replace not implemented yet");
            }

            this.IsEmpty = this.Items.Count == 0;
        }

        /// <summary>
        /// Provides class handling for the <see cref="E:System.Windows.ContentElement.KeyDown"/> routed event that occurs when the user presses a key.
        /// </summary>
        /// <param name="e">Provides data for <see cref="T:System.Windows.Input.KeyEventArgs"/>.</param>
        protected override void OnKeyDown(KeyEventArgs e) {
            var sortedDragablzItems = this._dragablzItemsControl.ItemsOrganiser.Sort(this._dragablzItemsControl.DragablzItems()).ToList();
            DragablzItem selectDragablzItem = null;
            switch (e.Key) {
                case Key.Tab:
                    if (this.SelectedItem == null) {
                        selectDragablzItem = sortedDragablzItems.FirstOrDefault();
                        break;
                    }

                    if ((e.KeyboardDevice.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) {
                        var selectedDragablzItem = (DragablzItem) this._dragablzItemsControl.ItemContainerGenerator.ContainerFromItem(this.SelectedItem);
                        var selectedDragablzItemIndex = sortedDragablzItems.IndexOf(selectedDragablzItem);
                        var direction = ((e.KeyboardDevice.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                            ? -1
                            : 1;
                        var newIndex = selectedDragablzItemIndex + direction;
                        if (newIndex < 0)
                            newIndex = sortedDragablzItems.Count - 1;
                        else if (newIndex == sortedDragablzItems.Count)
                            newIndex = 0;

                        selectDragablzItem = sortedDragablzItems[newIndex];
                    }

                    break;
                case Key.Home:
                    selectDragablzItem = sortedDragablzItems.FirstOrDefault();
                    break;
                case Key.End:
                    selectDragablzItem = sortedDragablzItems.LastOrDefault();
                    break;
            }

            if (selectDragablzItem != null) {
                var item = this._dragablzItemsControl.ItemContainerGenerator.ItemFromContainer(selectDragablzItem);
                this.SetCurrentValue(SelectedItemProperty, item);
                e.Handled = true;
            }

            if (!e.Handled)
                base.OnKeyDown(e);
        }

        /// <summary>
        /// Provides an appropriate automation peer implementation for this control
        /// as part of the WPF automation infrastructure.
        /// </summary>
        /// <returns>The type-specific System.Windows.Automation.Peers.AutomationPeer implementation.</returns>
        protected override AutomationPeer OnCreateAutomationPeer() {
            return new FrameworkElementAutomationPeer(this);
        }

        internal static TabablzControl GetOwnerOfHeaderItems(DragablzItemsControl itemsControl) {
            return LoadedInstances.FirstOrDefault(t => Equals(t._dragablzItemsControl, itemsControl));
        }

        private static void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs dependencyPropertyChangedEventArgs) {
            var tabablzControl = (TabablzControl) sender;
            if (tabablzControl.IsVisible)
                VisibleInstances.Add(tabablzControl);
            else if (VisibleInstances.Contains(tabablzControl))
                VisibleInstances.Remove(tabablzControl);
        }

        private void OnLoaded(object sender, RoutedEventArgs routedEventArgs) {
            LoadedInstances.Add(this);
            var window = Window.GetWindow(this);
            if (window == null)
                return;
            window.Closing += this.WindowOnClosing;
            this._windowSubscription.Disposable = Disposable.Create(() => window.Closing -= this.WindowOnClosing);
        }

        private void WindowOnClosing(object sender, CancelEventArgs cancelEventArgs) {
            this._windowSubscription.Disposable = Disposable.Empty;
            if (!this.ConsolidateOrphanedItems || this.InterTabController == null)
                return;

            var window = (Window) sender;

            var orphanedItems = this._dragablzItemsControl.DragablzItems();
            if (this.ConsolidatingOrphanedItemCallback != null) {
                orphanedItems =
                    orphanedItems.Where(
                        di => {
                            var args = new ItemActionCallbackArgs<TabablzControl>(window, this, di);
                            this.ConsolidatingOrphanedItemCallback(args);
                            return !args.IsCancelled;
                        }).ToList();
            }

            var target =
                LoadedInstances.Except(this).FirstOrDefault(
                    other =>
                        other.InterTabController != null &&
                        other.InterTabController.Partition == this.InterTabController.Partition);
            if (target == null)
                return;

            foreach (var item in orphanedItems.Select(orphanedItem => this._dragablzItemsControl.ItemContainerGenerator.ItemFromContainer(orphanedItem))) {
                this.RemoveFromSource(item);
                target.AddToSource(item);
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs routedEventArgs) {
            this._windowSubscription.Disposable = Disposable.Empty;
            LoadedInstances.Remove(this);
        }

        private void MarkWrappedTabItems() {
            if (this._dragablzItemsControl == null)
                return;

            foreach (var pair in this._dragablzItemsControl.Items.OfType<TabItem>().Select(tabItem =>
                new {
                    tabItem,
                    dragablzItem = this._dragablzItemsControl.ItemContainerGenerator.ContainerFromItem(tabItem) as DragablzItem
                }).Where(a => a.dragablzItem != null)) {
                var toolTipBinding = new Binding("ToolTip") {Source = pair.tabItem};
                BindingOperations.SetBinding(pair.dragablzItem, ToolTipProperty, toolTipBinding);
                SetIsWrappingTabItem(pair.dragablzItem, true);
            }
        }

        private void MarkInitialSelection() {
            if (this._dragablzItemsControl == null || this._dragablzItemsControl.ItemContainerGenerator.Status != GeneratorStatus.ContainersGenerated)
                return;

            if (this._dragablzItemsControl == null || this.SelectedItem == null)
                return;

            var tabItem = this.SelectedItem as TabItem;
            tabItem?.SetCurrentValue(IsSelectedProperty, true);

            var containerFromItem = this._dragablzItemsControl.ItemContainerGenerator.ContainerFromItem(this.SelectedItem) as DragablzItem;

            containerFromItem?.SetCurrentValue(DragablzItem.IsSelectedProperty, true);
        }

        private void ItemDragStarted(object sender, DragablzDragStartedEventArgs e) {
            if (!this.IsMyItem(e.DragablzItem))
                return;

            //the thumb may steal the user selection, so we will try and apply it manually
            if (this._dragablzItemsControl == null)
                return;

            e.DragablzItem.IsDropTargetFound = false;

            var sourceOfDragItemsControl = ItemsControlFromItemContainer(e.DragablzItem) as DragablzItemsControl;
            if (sourceOfDragItemsControl == null || !Equals(sourceOfDragItemsControl, this._dragablzItemsControl))
                return;

            var itemsControlOffset = Mouse.GetPosition(this._dragablzItemsControl);
            this._tabHeaderDragStartInformation = new TabHeaderDragStartInformation(e.DragablzItem, itemsControlOffset.X,
                itemsControlOffset.Y, e.DragStartedEventArgs.HorizontalOffset, e.DragStartedEventArgs.VerticalOffset);

            foreach (var otherItem in this._dragablzItemsControl.Containers<DragablzItem>().Except(e.DragablzItem))
                otherItem.IsSelected = false;
            e.DragablzItem.IsSelected = true;
            e.DragablzItem.PartitionAtDragStart = this.InterTabController?.Partition;
            var item = this._dragablzItemsControl.ItemContainerGenerator.ItemFromContainer(e.DragablzItem);
            var tabItem = item as TabItem;
            if (tabItem != null)
                tabItem.IsSelected = true;
            this.SelectedItem = item;

            if (this.ShouldDragWindow(sourceOfDragItemsControl))
                this.IsDraggingWindow = true;
        }

        private bool ShouldDragWindow(DragablzItemsControl sourceOfDragItemsControl) {
            return (this.Items.Count == 1
                    && (this.InterTabController == null || this.InterTabController.MoveWindowWithSolitaryTabs)
                    && !Layout.IsContainedWithinBranch(sourceOfDragItemsControl));
        }

        private void PreviewItemDragDelta(object sender, DragablzDragDeltaEventArgs e) {
            if (this._dragablzItemsControl == null)
                return;

            var sourceOfDragItemsControl = ItemsControlFromItemContainer(e.DragablzItem) as DragablzItemsControl;
            if (sourceOfDragItemsControl == null || !Equals(sourceOfDragItemsControl, this._dragablzItemsControl))
                return;

            if (!this.ShouldDragWindow(sourceOfDragItemsControl))
                return;

            if (this.MonitorReentry(e))
                return;

            var myWindow = Window.GetWindow(this);
            if (myWindow == null)
                return;

            if (this._interTabTransfer != null) {
                var cursorPos = Native.GetCursorPos().ToWpf();
                if (this._interTabTransfer.BreachOrientation == Orientation.Vertical) {
                    var vector = cursorPos - this._interTabTransfer.DragStartWindowOffset;
                    myWindow.Left = vector.X;
                    myWindow.Top = vector.Y;
                }
                else {
                    var offset = e.DragablzItem.TranslatePoint(this._interTabTransfer.OriginatorContainer.MouseAtDragStart, myWindow);
                    var borderVector = myWindow.PointToScreen(new Point()).ToWpf() - new Point(myWindow.Left, myWindow.Top);
                    offset.Offset(borderVector.X, borderVector.Y);
                    myWindow.Left = cursorPos.X - offset.X;
                    myWindow.Top = cursorPos.Y - offset.Y;
                }
            }
            else {
                myWindow.Left += e.DragDeltaEventArgs.HorizontalChange;
                myWindow.Top += e.DragDeltaEventArgs.VerticalChange;
            }

            e.Handled = true;
        }

        private bool MonitorReentry(DragablzDragDeltaEventArgs e) {
            var screenMousePosition = this._dragablzItemsControl.PointToScreen(Mouse.GetPosition(this._dragablzItemsControl));

            var sourceTabablzControl = (TabablzControl) e.Source;
            if (sourceTabablzControl.Items.Count > 1 && e.DragablzItem.LogicalIndex < sourceTabablzControl.FixedHeaderCount) {
                return false;
            }

            var otherTabablzControls = LoadedInstances.Where(
                tc =>
                    tc != this && tc.InterTabController != null && this.InterTabController != null
                    && Equals(tc.InterTabController.Partition, this.InterTabController.Partition)
                    && tc._dragablzItemsControl != null).Select(tc => {
                var topLeft = tc._dragablzItemsControl.PointToScreen(new Point());
                var lastFixedItem = tc._dragablzItemsControl.DragablzItems().OrderBy(di => di.LogicalIndex).Take(tc._dragablzItemsControl.FixedItemCount).LastOrDefault();
                //TODO work this for vert tabs
                if (lastFixedItem != null)
                    topLeft.Offset(lastFixedItem.X + lastFixedItem.ActualWidth, 0);
                var bottomRight =
                    tc._dragablzItemsControl.PointToScreen(new Point(tc._dragablzItemsControl.ActualWidth,
                        tc._dragablzItemsControl.ActualHeight));

                return new {tc, topLeft, bottomRight};
            });


            var target = Native.SortWindowsTopToBottom(Application.Current.Windows.OfType<Window>()).Join(otherTabablzControls, w => w, a => Window.GetWindow(a.tc), (w, a) => a).FirstOrDefault(a => new Rect(a.topLeft, a.bottomRight).Contains(screenMousePosition));

            if (target == null)
                return false;

            var mousePositionOnItem = Mouse.GetPosition(e.DragablzItem);

            var floatingItemSnapShots = this.VisualTreeDepthFirstTraversal().OfType<Layout>().SelectMany(l => l.FloatingDragablzItems().Select(FloatingItemSnapShot.Take)).ToList();

            e.DragablzItem.IsDropTargetFound = true;
            var item = this.RemoveItem(e.DragablzItem);

            var interTabTransfer = new InterTabTransfer(item, e.DragablzItem, mousePositionOnItem, floatingItemSnapShots);
            e.DragablzItem.IsDragging = false;

            target.tc.ReceiveDrag(interTabTransfer);
            e.Cancel = true;

            return true;
        }

        internal object RemoveItem(DragablzItem dragablzItem) {
            var item = this._dragablzItemsControl.ItemContainerGenerator.ItemFromContainer(dragablzItem);

            //stop the header shrinking if the tab stays open when empty
            var minSize = this.EmptyHeaderSizingHint == EmptyHeaderSizingHint.PreviousTab
                ? new Size(this._dragablzItemsControl.ActualWidth, this._dragablzItemsControl.ActualHeight)
                : new Size();

            this._dragablzItemsControl.MinHeight = 0;
            this._dragablzItemsControl.MinWidth = 0;

            var contentPresenter = this.FindChildContentPresenter(item);
            this.RemoveFromSource(item);
            this._itemsHolder.Children.Remove(contentPresenter);

            if (this.Items.Count != 0)
                return item;

            var window = Window.GetWindow(this);
            if (window != null
                && this.InterTabController != null
                && this.InterTabController.InterTabClient.TabEmptiedHandler(this, window) == TabEmptiedResponse.CloseWindowOrLayoutBranch) {
                if (Layout.ConsolidateBranch(this))
                    return item;

                try {
                    SetIsClosingAsPartOfDragOperation(window, true);
                    window.Close();
                }
                finally {
                    SetIsClosingAsPartOfDragOperation(window, false);
                }
            }
            else {
                this._dragablzItemsControl.MinHeight = minSize.Height;
                this._dragablzItemsControl.MinWidth = minSize.Width;
            }

            return item;
        }

        private void ItemDragCompleted(object sender, DragablzDragCompletedEventArgs e) {
            if (!this.IsMyItem(e.DragablzItem))
                return;

            this._interTabTransfer = null;
            this._dragablzItemsControl.LockedMeasure = null;
            this.IsDraggingWindow = false;
        }

        private void ItemDragDelta(object sender, DragablzDragDeltaEventArgs e) {
            if (!this.IsMyItem(e.DragablzItem))
                return;
            if (this.FixedHeaderCount > 0 && this._dragablzItemsControl.ItemsOrganiser.Sort(this._dragablzItemsControl.DragablzItems()).Take(this.FixedHeaderCount).Contains(e.DragablzItem))
                return;

            if (this._tabHeaderDragStartInformation == null ||
                !Equals(this._tabHeaderDragStartInformation.DragItem, e.DragablzItem) || this.InterTabController == null)
                return;

            if (this.InterTabController.InterTabClient == null)
                throw new InvalidOperationException("An InterTabClient must be provided on an InterTabController.");

            this.MonitorBreach(e);
        }

        private bool IsMyItem(DragablzItem item) {
            return this._dragablzItemsControl != null && this._dragablzItemsControl.DragablzItems().Contains(item);
        }

        private void MonitorBreach(DragablzDragDeltaEventArgs e) {
            var mousePositionOnHeaderItemsControl = Mouse.GetPosition(this._dragablzItemsControl);

            Orientation? breachOrientation = null;
            if (mousePositionOnHeaderItemsControl.X < -this.InterTabController.HorizontalPopoutGrace
                || (mousePositionOnHeaderItemsControl.X - this._dragablzItemsControl.ActualWidth) > this.InterTabController.HorizontalPopoutGrace)
                breachOrientation = Orientation.Horizontal;
            else if (mousePositionOnHeaderItemsControl.Y < -this.InterTabController.VerticalPopoutGrace
                     || (mousePositionOnHeaderItemsControl.Y - this._dragablzItemsControl.ActualHeight) > this.InterTabController.VerticalPopoutGrace)
                breachOrientation = Orientation.Vertical;

            if (!breachOrientation.HasValue)
                return;

            var newTabHost = this.InterTabController.InterTabClient.GetNewHost(this.InterTabController.InterTabClient, this.InterTabController.Partition, this);
            if (newTabHost?.TabablzControl == null || newTabHost.Container == null)
                throw new ApplicationException("New tab host was not correctly provided");

            var item = this._dragablzItemsControl.ItemContainerGenerator.ItemFromContainer(e.DragablzItem);
            var isTransposing = this.IsTransposing(newTabHost.TabablzControl);

            var myWindow = Window.GetWindow(this);
            if (myWindow == null)
                throw new ApplicationException("Unable to find owning window.");
            var dragStartWindowOffset = this.ConfigureNewHostSizeAndGetDragStartWindowOffset(myWindow, newTabHost, e.DragablzItem, isTransposing);

            var dragableItemHeaderPoint = e.DragablzItem.TranslatePoint(new Point(), this._dragablzItemsControl);
            var dragableItemSize = new Size(e.DragablzItem.ActualWidth, e.DragablzItem.ActualHeight);
            var floatingItemSnapShots = this.VisualTreeDepthFirstTraversal().OfType<Layout>().SelectMany(l => l.FloatingDragablzItems().Select(FloatingItemSnapShot.Take)).ToList();

            var interTabTransfer = new InterTabTransfer(item, e.DragablzItem, breachOrientation.Value, dragStartWindowOffset, e.DragablzItem.MouseAtDragStart, dragableItemHeaderPoint, dragableItemSize, floatingItemSnapShots, isTransposing);

            if (myWindow.WindowState == WindowState.Maximized) {
                var desktopMousePosition = Native.GetCursorPos().ToWpf();
                newTabHost.Container.Left = desktopMousePosition.X - dragStartWindowOffset.X;
                newTabHost.Container.Top = desktopMousePosition.Y - dragStartWindowOffset.Y;
            }
            else {
                newTabHost.Container.Left = myWindow.Left;
                newTabHost.Container.Top = myWindow.Top;
            }

            newTabHost.Container.Show();
            var contentPresenter = this.FindChildContentPresenter(item);

            //stop the header shrinking if the tab stays open when empty
            var minSize = this.EmptyHeaderSizingHint == EmptyHeaderSizingHint.PreviousTab
                ? new Size(this._dragablzItemsControl.ActualWidth, this._dragablzItemsControl.ActualHeight)
                : new Size();
            System.Diagnostics.Debug.WriteLine("B " + minSize);

            this.RemoveFromSource(item);
            this._itemsHolder.Children.Remove(contentPresenter);
            if (this.Items.Count == 0) {
                this._dragablzItemsControl.MinHeight = minSize.Height;
                this._dragablzItemsControl.MinWidth = minSize.Width;
                Layout.ConsolidateBranch(this);
            }

            this.RestorePreviousSelection();

            foreach (var dragablzItem in this._dragablzItemsControl.DragablzItems()) {
                dragablzItem.IsDragging = false;
                dragablzItem.IsSiblingDragging = false;
            }

            newTabHost.TabablzControl.ReceiveDrag(interTabTransfer);
            interTabTransfer.OriginatorContainer.IsDropTargetFound = true;
            e.Cancel = true;
        }

        private bool IsTransposing(TabControl target) {
            return IsVertical(this) != IsVertical(target);
        }

        private static bool IsVertical(TabControl tabControl) {
            return tabControl.TabStripPlacement == Dock.Left
                   || tabControl.TabStripPlacement == Dock.Right;
        }

        private void RestorePreviousSelection() {
            var previousSelection = this._previousSelection?.Target;
            if (previousSelection != null && this.Items.Contains(previousSelection))
                this.SelectedItem = previousSelection;
            else
                this.SelectedItem = this.Items.OfType<object>().FirstOrDefault();
        }

        private Point ConfigureNewHostSizeAndGetDragStartWindowOffset(Window currentWindow, INewTabHost<Window> newTabHost, DragablzItem dragablzItem, bool isTransposing) {
            var layout = this.VisualTreeAncestory().OfType<Layout>().FirstOrDefault();
            Point dragStartWindowOffset;
            if (layout != null) {
                newTabHost.Container.Width = this.ActualWidth + Math.Max(0, currentWindow.RestoreBounds.Width - layout.ActualWidth);
                newTabHost.Container.Height = this.ActualHeight + Math.Max(0, currentWindow.RestoreBounds.Height - layout.ActualHeight);
                dragStartWindowOffset = dragablzItem.TranslatePoint(new Point(), this);
                //dragStartWindowOffset.Offset(currentWindow.RestoreBounds.Width - layout.ActualWidth, currentWindow.RestoreBounds.Height - layout.ActualHeight);
            }
            else {
                if (newTabHost.Container.GetType() == currentWindow.GetType()) {
                    newTabHost.Container.Width = currentWindow.RestoreBounds.Width;
                    newTabHost.Container.Height = currentWindow.RestoreBounds.Height;
                    dragStartWindowOffset = isTransposing ? new Point(dragablzItem.MouseAtDragStart.X, dragablzItem.MouseAtDragStart.Y) : dragablzItem.TranslatePoint(new Point(), currentWindow);
                }
                else {
                    newTabHost.Container.Width = this.ActualWidth;
                    newTabHost.Container.Height = this.ActualHeight;
                    dragStartWindowOffset = isTransposing ? new Point() : dragablzItem.TranslatePoint(new Point(), this);
                    dragStartWindowOffset.Offset(dragablzItem.MouseAtDragStart.X, dragablzItem.MouseAtDragStart.Y);
                    return dragStartWindowOffset;
                }
            }

            dragStartWindowOffset.Offset(dragablzItem.MouseAtDragStart.X, dragablzItem.MouseAtDragStart.Y);
            var borderVector = currentWindow.PointToScreen(new Point()).ToWpf() - new Point(currentWindow.GetActualLeft(), currentWindow.GetActualTop());
            dragStartWindowOffset.Offset(borderVector.X, borderVector.Y);
            return dragStartWindowOffset;
        }

        internal void ReceiveDrag(InterTabTransfer interTabTransfer) {
            var myWindow = Window.GetWindow(this);
            if (myWindow == null)
                throw new ApplicationException("Unable to find owning window.");
            myWindow.Activate();

            this._interTabTransfer = interTabTransfer;

            if (this.Items.Count == 0) {
                if (interTabTransfer.IsTransposing)
                    this._dragablzItemsControl.LockedMeasure = new Size(
                        interTabTransfer.ItemSize.Width,
                        interTabTransfer.ItemSize.Height);
                else
                    this._dragablzItemsControl.LockedMeasure = new Size(
                        interTabTransfer.ItemPositionWithinHeader.X + interTabTransfer.ItemSize.Width,
                        interTabTransfer.ItemPositionWithinHeader.Y + interTabTransfer.ItemSize.Height);
            }

            var lastFixedItem = this._dragablzItemsControl.DragablzItems().OrderBy(i => i.LogicalIndex).Take(this._dragablzItemsControl.FixedItemCount).LastOrDefault();

            this.AddToSource(interTabTransfer.Item);
            this.SelectedItem = interTabTransfer.Item;

            this.Dispatcher.BeginInvoke(new Action(() => Layout.RestoreFloatingItemSnapShots(this, interTabTransfer.FloatingItemSnapShots)), DispatcherPriority.Loaded);
            this._dragablzItemsControl.InstigateDrag(interTabTransfer.Item, newContainer => {
                newContainer.PartitionAtDragStart = interTabTransfer.OriginatorContainer.PartitionAtDragStart;
                newContainer.IsDropTargetFound = true;

                if (interTabTransfer.TransferReason == InterTabTransferReason.Breach) {
                    if (interTabTransfer.IsTransposing) {
                        newContainer.Y = 0;
                        newContainer.X = 0;
                    }
                    else {
                        newContainer.Y = interTabTransfer.OriginatorContainer.Y;
                        newContainer.X = interTabTransfer.OriginatorContainer.X;
                    }
                }
                else {
                    if (this.TabStripPlacement == Dock.Top || this.TabStripPlacement == Dock.Bottom) {
                        var mouseXOnItemsControl = Native.GetCursorPos().X - this._dragablzItemsControl.PointToScreen(new Point()).X;
                        var newX = mouseXOnItemsControl - interTabTransfer.DragStartItemOffset.X;
                        if (lastFixedItem != null) {
                            newX = Math.Max(newX, lastFixedItem.X + lastFixedItem.ActualWidth);
                        }

                        newContainer.X = newX;
                        newContainer.Y = 0;
                    }
                    else {
                        var mouseYOnItemsControl = Native.GetCursorPos().Y - this._dragablzItemsControl.PointToScreen(new Point()).Y;
                        var newY = mouseYOnItemsControl - interTabTransfer.DragStartItemOffset.Y;
                        if (lastFixedItem != null) {
                            newY = Math.Max(newY, lastFixedItem.Y + lastFixedItem.ActualHeight);
                        }

                        newContainer.X = 0;
                        newContainer.Y = newY;
                    }
                }

                newContainer.MouseAtDragStart = interTabTransfer.DragStartItemOffset;
            });
        }

        /// <summary>
        /// generate a ContentPresenter for the selected item
        /// </summary>
        private void UpdateSelectedItem() {
            if (this._itemsHolder == null) {
                return;
            }

            this.CreateChildContentPresenter(this.SelectedItem);

            // show the right child
            var selectedContent = GetContent(this.SelectedItem);
            foreach (ContentPresenter child in this._itemsHolder.Children) {
                var isSelected = (child.Content == selectedContent);
                child.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
                child.IsEnabled = isSelected;
            }
        }

        private static object GetContent(object item) {
            return (item is TabItem) ? ((TabItem) item).Content : item;
        }

        /// <summary>
        /// create the child ContentPresenter for the given item (could be data or a TabItem)
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        private void CreateChildContentPresenter(object item) {
            if (item == null)
                return;

            var cp = this.FindChildContentPresenter(item);
            if (cp != null)
                return;

            // the actual child to be added.  cp.Tag is a reference to the TabItem
            cp = new ContentPresenter {
                Content = GetContent(item),
                ContentTemplate = this.ContentTemplate,
                ContentTemplateSelector = this.ContentTemplateSelector,
                ContentStringFormat = this.ContentStringFormat,
                Visibility = Visibility.Collapsed,
            };
            this._itemsHolder.Children.Add(cp);
        }

        /// <summary>
        /// Find the CP for the given object.  data could be a TabItem or a piece of data
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        private ContentPresenter FindChildContentPresenter(object data) {
            if (data is TabItem)
                data = ((TabItem) data).Content;

            return data == null
                ? null
                : this._itemsHolder?.Children.Cast<ContentPresenter>().FirstOrDefault(cp => cp.Content == data);
        }

        private void ItemContainerGeneratorOnStatusChanged(object sender, EventArgs eventArgs) {
            this.MarkWrappedTabItems();
            this.MarkInitialSelection();
        }

        private static void CloseItem(DragablzItem item, TabablzControl owner) {
            if (item == null)
                throw new ApplicationException("Valid DragablzItem to close is required.");

            if (owner == null)
                throw new ApplicationException("Valid TabablzControl container is required.");

            if (!owner.IsMyItem(item))
                throw new ApplicationException("TabablzControl container must be an owner of the DragablzItem to close");

            var cancel = false;
            if (owner.ClosingItemCallback != null) {
                var callbackArgs = new ItemActionCallbackArgs<TabablzControl>(Window.GetWindow(owner), owner, item);
                owner.ClosingItemCallback(callbackArgs);
                cancel = callbackArgs.IsCancelled;
            }

            if (!cancel)
                owner.RemoveItem(item);
        }

        private static void CloseItemCanExecuteClassHandler(object sender, CanExecuteRoutedEventArgs e) {
            e.CanExecute = FindOwner(e.Parameter, e.OriginalSource) != null;
        }

        private static void CloseItemClassHandler(object sender, ExecutedRoutedEventArgs e) {
            var owner = FindOwner(e.Parameter, e.OriginalSource);

            if (owner == null)
                throw new ApplicationException("Unable to ascertain DragablzItem to close.");

            CloseItem(owner.Item1, owner.Item2);
        }

        private static Tuple<DragablzItem, TabablzControl> FindOwner(object eventParameter, object eventOriginalSource) {
            var dragablzItem = eventParameter as DragablzItem;
            if (dragablzItem == null) {
                var dependencyObject = eventOriginalSource as DependencyObject;
                dragablzItem = dependencyObject.VisualTreeAncestory().OfType<DragablzItem>().FirstOrDefault();
                if (dragablzItem == null) {
                    var popup = dependencyObject.LogicalTreeAncestory().OfType<Popup>().LastOrDefault();
                    if (popup?.PlacementTarget != null) {
                        dragablzItem = popup.PlacementTarget.VisualTreeAncestory().OfType<DragablzItem>().FirstOrDefault();
                    }
                }
            }

            if (dragablzItem == null)
                return null;

            var tabablzControl = LoadedInstances.FirstOrDefault(tc => tc.IsMyItem(dragablzItem));

            return tabablzControl == null ? null : new Tuple<DragablzItem, TabablzControl>(dragablzItem, tabablzControl);
        }

        private void AddItemHandler(object sender, ExecutedRoutedEventArgs e) {
            if (this.NewItemFactory == null)
                throw new InvalidOperationException("NewItemFactory must be provided.");

            var newItem = this.NewItemFactory();
            if (newItem == null)
                throw new ApplicationException("NewItemFactory returned null.");

            this.AddToSource(newItem);
            this.SelectedItem = newItem;

            this.Dispatcher.BeginInvoke(new Action(this._dragablzItemsControl.InvalidateMeasure), DispatcherPriority.Loaded);
        }

        private void PrepareChildContainerForItemOverride(DependencyObject dependencyObject, object o) {
            var dragablzItem = dependencyObject as DragablzItem;
            if (dragablzItem != null && this.HeaderMemberPath != null) {
                var contentBinding = new Binding(this.HeaderMemberPath) {Source = o};
                dragablzItem.SetBinding(ContentControl.ContentProperty, contentBinding);
                dragablzItem.UnderlyingContent = o;
            }

            SetIsWrappingTabItem(dependencyObject, o is TabItem);
        }
    }
}