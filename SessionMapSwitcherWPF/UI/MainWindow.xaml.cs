﻿using System;
using System.Threading.Tasks;
using System.Windows;

using System.Windows.Input;
using System.Diagnostics;
using SessionMapSwitcherCore.Classes;
using SessionMapSwitcher.Classes.Events;
using SessionMapSwitcher.UI;
using System.Windows.Threading;
using SessionMapSwitcherCore.Utils;
using System.Windows.Forms;
using SessionMapSwitcherWPF.Classes;
using SessionModManagerCore.Classes;
using SessionModManagerCore.ViewModels;
using SessionModManagerWPF.UI;

namespace SessionMapSwitcher
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel ViewModel;

        private bool IsNewVersionAvailable = false;

        /// <summary>
        /// used to prevent the window size to be saved to app config when setting it
        /// </summary>
        private bool IsSettingWindowSize = false;

        /// <summary>
        /// Timer to trigger the update window to open after a second
        /// if there is an update avaialble. This is used so the Window
        /// is created on the main UI thread
        /// </summary>
        private DispatcherTimer updateTimer;

        private int SelectedTabIndex;

        public MainWindow()
        {
            InitializeComponent();

            SetCustomWindowSizeFromAppSettings();

            ViewModel = new MainWindowViewModel();
            ViewModel.ReloadAvailableMapsInBackground();

            this.DataContext = ViewModel;
            this.Title = $"{App.GetAppName()} - v{App.GetAppVersion()}";

            SelectedTabIndex = tabControl.SelectedIndex;

            // set window state
            string windowStateVal = AppSettingsUtil.GetAppSetting(SettingKey.WindowState);
            if (!string.IsNullOrWhiteSpace(windowStateVal) && WindowState.TryParse(windowStateVal, out WindowState state))
            {
                this.WindowState = state;
            }
        }

        private void SetCustomWindowSizeFromAppSettings()
        {
            IsSettingWindowSize = true;

            string customSize = AppSettingsUtil.GetAppSetting(SettingKey.CustomWindowSize);

            if (string.IsNullOrEmpty(customSize))
            {
                IsSettingWindowSize = false;
                return;
            }

            string[] dimensions = customSize.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);


            bool didParseWidth = double.TryParse(dimensions[0], out double newWidth);
            bool didParseHeight = double.TryParse(dimensions[1], out double newHeight);

            if (didParseWidth && didParseHeight)
            {
                this.Width = newWidth;
                this.Height = newHeight;
            }


            IsSettingWindowSize = false;
        }

        private void BtnBrowseSessionPath_Click(object sender, RoutedEventArgs e)
        {
            using (FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog())
            {
                DialogResult result = folderBrowserDialog.ShowDialog();
                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    SetAndValidateSessionPath(folderBrowserDialog.SelectedPath);
                }
            }
        }

        private void BtnStartGame_Click(object sender, RoutedEventArgs e)
        {
            if (SessionPath.IsSessionRunning())
            {
                MessageBoxResult result = System.Windows.MessageBox.Show("Session is already running! Click 'Yes' if you want to restart the game.", "Notice!", MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.No);

                if (result == MessageBoxResult.No)
                {
                    return;
                }
                else
                {
                    // kill the process
                    Process[] procs = System.Diagnostics.Process.GetProcessesByName("SessionGame-Win64-Shipping");
                    if (procs.Length > 0)
                    {
                        procs[0].Kill();
                    }
                }
            }

            LoadMapInBackgroundAndContinueWith((antecedent) =>
            {
                ViewModel.InputControlsEnabled = true;

                ViewModel.StartGameAndLoadSecondMap();
            });
        }

        private void BtnLoadMap_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // double check the controls are disabled and should not load (e.g. when double clicking map in list)
                if (ViewModel.InputControlsEnabled == false)
                {
                    return;
                }

                LoadMapInBackgroundAndContinueWith((antecedent) =>
                {
                    if (antecedent.IsFaulted)
                    {
                        ViewModel.UserMessage = $"failed to load map: {antecedent.Exception.InnerException?.Message}";
                    }

                    ViewModel.InputControlsEnabled = true;
                });
            }
            catch (AggregateException ae)
            {
                ViewModel.UserMessage = $"failed to load: {ae.InnerException?.Message}";
            }
            catch (Exception ex)
            {
                ViewModel.UserMessage = $"failed to load: {ex.Message}";
            }


        }

        private void LoadMapInBackgroundAndContinueWith(Action<Task> continuationTask)
        {
            if (SessionPath.IsSessionPathValid() == false)
            {
                System.Windows.MessageBox.Show("You have selected an incorrect path to Session. Make sure the directory you choose has the folders 'Engine' and 'SessionGame'.", "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (UeModUnlocker.IsGamePatched() == false)
            {
                MessageBoxResult result = System.Windows.MessageBox.Show("Session has not been patched yet. Click 'Patch With Illusory Mod Unlocker' to patch the game.", "Notice!", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (lstMaps.SelectedItem == null)
            {
                System.Windows.MessageBox.Show("Select a map to load first!",
                                                "Notice!",
                                                MessageBoxButton.OK,
                                                MessageBoxImage.Information);
                return;
            }


            MapListItem selectedItem = lstMaps.SelectedItem as MapListItem;

            if (selectedItem.IsValid == false)
            {
                System.Windows.MessageBox.Show("This map is missing the required Game Mode Override 'PBP_InGameSessionGameMode'.\n\nAdd a Game Mode to your map in UE4: '/Content/Data/PBP_InGameSessionGameMode.uasset'.\nThen reload the list of available maps.",
                                                "Error!",
                                                MessageBoxButton.OK,
                                                MessageBoxImage.Error);
                return;
            }

            ViewModel.UserMessage = $"Loading {selectedItem.MapName} ...";
            ViewModel.InputControlsEnabled = false;

            Task t = Task.Run(() => ViewModel.LoadSelectedMap(selectedItem));

            t.ContinueWith(continuationTask);
        }

        private void BtnReloadMaps_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ReloadAvailableMapsInBackground();
        }

        /// <summary>
        /// Load map when an available map is double clicked
        /// </summary>
        private void ListBoxItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            BtnLoadMap_Click(sender, e);
        }

        private void TxtSessionPath_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SetAndValidateSessionPath(ViewModel.SessionPathTextInput); // the viewmodel is 2-way binded so the new path value is already set when enter is pressed so we pass the same value to store in app setttings and validate it
                ViewModel.UserMessage = "Session Path updated!";
            }
        }

        private void SetAndValidateSessionPath(string path)
        {
            ViewModel.SetSessionPath(path); // this will save it to app settings
            ViewModel.SetCurrentlyLoadedMap();

            if (SessionPath.IsSessionPathValid())
            {
                ViewModel.RefreshGameSettings();
                ViewModel.ReloadAvailableMapsInBackground();
            }
            else
            {
                System.Windows.MessageBox.Show("You may have selected an incorrect path to Session. Make sure the directory you choose has the folders 'Engine' and 'SessionGame'.", "Warning!", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void MenuOpenSessionFolder_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.OpenFolderToSession();
        }

        private void MenuOpenMapsFolder_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.OpenFolderToSessionContent();
        }

        private void MenuOpenSelectedMapFolder_Click(object sender, RoutedEventArgs e)
        {
            if (lstMaps.SelectedItem == null)
            {
                ViewModel.UserMessage = "Cannot open folder: No map selected.";
                return;
            }

            MapListItem selectedMap = lstMaps.SelectedItem as MapListItem;
            ViewModel.OpenFolderToSelectedMap(selectedMap);
        }

        private void ChkShowInvalidMaps_Click(object sender, RoutedEventArgs e)
        {
            //ViewModel.FilteredAvailableMaps.Refresh();
        }

        private void BtnApplySettings_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.InputControlsEnabled == false)
            {
                return;
            }

            if (UeModUnlocker.IsGamePatched() == false)
            {
                MessageBoxResult result = System.Windows.MessageBox.Show("Session has not been patched yet. Click 'Patch With Illusory Mod Unlocker' to patch the game.", "Notice!", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool didSet = ViewModel.UpdateGameSettings();

            if (didSet)
            {
                if (SessionPath.IsSessionRunning())
                {
                    ViewModel.UserMessage += " Restart the game for changes to take effect.";
                }
            }
        }

        private void MenuOpenReadme_Click(object sender, RoutedEventArgs e)
        {
            OpenReadMeInBrowser();
        }

        private static void OpenReadMeInBrowser()
        {
            ProcessStartInfo info = new ProcessStartInfo()
            {
                FileName = "https://github.com/rodriada000/SessionMapSwitcher/blob/master/README.md"
            };

            Process.Start(info);
        }

        private void BtnImportMap_Click(object sender, RoutedEventArgs e)
        {
            if (UeModUnlocker.IsGamePatched() == false)
            {
                MessageBoxResult result = System.Windows.MessageBox.Show("Session has not been patched yet. Click 'Patch With Illusory Mod Unlocker' to patch the game.", "Notice!", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            importContextMenu.IsOpen = true;
        }

        private void MenuComputerImport_Click(object sender, RoutedEventArgs e)
        {
            OpenComputerImportWindow();
        }

        private void MenuOnlineImport_Click(object sender, RoutedEventArgs e)
        {
            controlAssetStore.ViewModel.DisplayMaps = true; // check this in-case was unchecked previously
            tabControl.SelectedIndex = 1;
        }

        internal void OpenComputerImportWindow()
        {
            if (SessionPath.IsSessionPathValid() == false)
            {
                ViewModel.UserMessage = "Cannot import: You must set your path to Session before importing maps.";
                return;
            }

            ComputerImportViewModel importViewModel = new ComputerImportViewModel();

            ComputerImportWindow importWindow = new ComputerImportWindow(importViewModel);
            importWindow.ShowDialog();

            ViewModel.ReloadAvailableMapsInBackground(); // reload list of available maps as it may have changed
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            CheckForNewVersionInBackground();
            ctrlTextureReplacer.ViewModel.MessageChanged += TextureReplacer_MessageChanged;
            ctrlProjectWatcher.ViewModel.MapImported += ProjectWatcher_MapImported;
        }

        private void TextureReplacer_MessageChanged(string message)
        {
            ViewModel.UserMessage = message;
        }

        private void ProjectWatcher_MapImported(object sender, MapImportedEventArgs e)
        {
            ViewModel.LoadMap(e.MapName);
        }

        #region Update Related Methods

        private void CheckForNewVersionInBackground()
        {
            ViewModel.UserMessage = "Checking for updates ...";
            Task task = Task.Factory.StartNew(() =>
            {
                IsNewVersionAvailable = VersionChecker.IsUpdateAvailable();
            });


            task.ContinueWith((antecedent) =>
            {
                ViewModel.UserMessage = "";

                if (IsNewVersionAvailable)
                {
                    StartUpdateTimerToOpenWindow();
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>
        /// Creates and starts a timer to open the <see cref="UpdateWindow"/> after a few milliseconds
        /// </summary>
        private void StartUpdateTimerToOpenWindow()
        {
            updateTimer = new DispatcherTimer();
            updateTimer.Tick += UpdateTimer_Tick;
            updateTimer.Interval = new TimeSpan(days: 0, hours: 0, minutes: 0, seconds: 0, milliseconds: 250);
            updateTimer.Start();
        }

        /// <summary>
        /// Stops the timer and shows the <see cref="UpdateWindow"/>
        /// </summary>
        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            if (updateTimer != null)
            {
                updateTimer.Stop();
                updateTimer.Tick -= UpdateTimer_Tick;
            }

            UpdateWindow updateWindow = new UpdateWindow();
            updateWindow.ShowDialog();
        }

        #endregion

        private void MenuReimporSelectedMap_Click(object sender, RoutedEventArgs e)
        {
            if (lstMaps.SelectedItem == null)
            {
                System.Windows.MessageBox.Show("Select a map to load first!",
                                                "Notice!",
                                                MessageBoxButton.OK,
                                                MessageBoxImage.Information);
                return;
            }


            MapListItem selectedItem = lstMaps.SelectedItem as MapListItem;

            ViewModel.ReimportMapFiles(selectedItem);
        }

        private void ContextMenu_ContextMenuOpening(object sender, System.Windows.Controls.ContextMenuEventArgs e)
        {
            // disable certain menu items if no map selected
            bool isMapSelected = (lstMaps.SelectedItem != null);
            bool isSessionPathValid = SessionPath.IsSessionPathValid();

            menuReimporSelectedMap.IsEnabled = isMapSelected;
            menuOpenSelectedMapFolder.IsEnabled = isMapSelected;
            menuRenameSelectedMap.IsEnabled = isMapSelected;
            menuHideSelectedMap.IsEnabled = isMapSelected;

            menuOpenSessionFolder.IsEnabled = isSessionPathValid;
            menuOpenMapsFolder.IsEnabled = isSessionPathValid;

            if (isMapSelected)
            {
                MapListItem selected = (lstMaps.SelectedItem as MapListItem);
                bool hasImportLocation = MetaDataManager.IsImportLocationStored(selected);
                menuReimporSelectedMap.IsEnabled = hasImportLocation;
                menuReimporSelectedMap.ToolTip = hasImportLocation ? null : "You can only re-import if you imported the map from 'Import Map > From Computer ...' and imported a folder.\n(does not work with .zip files)";
                menuHideSelectedMap.Header = selected.IsHiddenByUser ? "Show Selected Map" : "Hide Selected Map";


                if (ViewModel.SecondMapToLoad == null || ViewModel.SecondMapToLoad?.FullPath != selected.FullPath)
                {
                    menuSecondMapToLoad.Header = "Set As Second Map To Load (When Leaving Apartment)";
                    menuSecondMapToLoad.ToolTip = "Set the map to be loaded after you leave the apartment (before starting the game)";
                }
                else
                {
                    menuSecondMapToLoad.ToolTip = "This will clear the selected map to not load after you leave the apartment";
                    menuSecondMapToLoad.Header = "Clear As Second Map To Load";
                }


                bool canBeDeleted = MetaDataManager.HasPathToMapFilesStored(selected);
                menuDeleteSelectedMap.IsEnabled = canBeDeleted;
            }
        }


        /// <summary>
        /// Opens the window to rename a map. 
        /// </summary>
        private void MenuRenameSelectedMap_Click(object sender, RoutedEventArgs e)
        {
            if (lstMaps.SelectedItem == null)
            {
                System.Windows.MessageBox.Show("Select a map to rename first!",
                                                "Notice!",
                                                MessageBoxButton.OK,
                                                MessageBoxImage.Information);
                return;
            }


            MapListItem selectedItem = lstMaps.SelectedItem as MapListItem;
            OpenRenameMapWindow(selectedItem);
        }

        private void MenuHideSelectedMap_Click(object sender, RoutedEventArgs e)
        {
            if (lstMaps.SelectedItem == null)
            {
                System.Windows.MessageBox.Show("Select a map to hide/show first!",
                                                "Notice!",
                                                MessageBoxButton.OK,
                                                MessageBoxImage.Information);
                return;
            }

            MapListItem selectedItem = lstMaps.SelectedItem as MapListItem;
            ViewModel.ToggleVisiblityOfMap(selectedItem);
        }

        private void TxtSessionPath_PreviewDragOver(object sender, System.Windows.DragEventArgs e)
        {
            e.Handled = true;

            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop, false) == true)
            {
                e.Effects = System.Windows.DragDropEffects.All;
            }
        }

        private void TxtSessionPath_PreviewDrop(object sender, System.Windows.DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
            if (files != null && files.Length != 0)
            {
                ViewModel.SessionPathTextInput = files[0];
            }
        }

        private void BtnPatch_Click(object sender, RoutedEventArgs e)
        {
            PromptToPatch();
        }

        private void mainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (IsSettingWindowSize || IsLoaded == false)
            {
                return;
            }

            string newSize = $"{this.ActualWidth},{this.ActualHeight}";
            AppSettingsUtil.AddOrUpdateAppSettings(SettingKey.CustomWindowSize, newSize);
        }

        /// <summary>
        /// Opens a window to enter a new name for a map.
        /// Writes to meta data file if user clicks 'Rename' in window.
        /// </summary>
        /// <param name="selectedMap"></param>
        internal void OpenRenameMapWindow(MapListItem selectedMap)
        {
            RenameMapViewModel viewModel = new RenameMapViewModel(selectedMap);
            RenameMapWindow window = new RenameMapWindow(viewModel)
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };
            bool? result = window.ShowDialog();

            if (result.GetValueOrDefault(false) == true)
            {
                bool didWrite = MetaDataManager.WriteCustomMapPropertiesToFile(selectedMap);

                if (didWrite == false)
                {
                    ViewModel.UserMessage = "Failed to update .meta file with new custom name. Your custom name may have not been saved and will be lost when the app restarts.";
                    return;
                }

                ViewModel.RefreshFilteredMaps();
                ViewModel.UserMessage = $"{selectedMap.MapName} renamed to {selectedMap.CustomName}!";
            }
        }

        /// <summary>
        /// Prompts to download Illusory mod unlocker or opens it if already installed
        /// </summary>
        public void PromptToPatch()
        {
            string displayName = "IllusoryUniversalModUnlocker";

            if (!RegistryHelper.IsSoftwareInstalled(displayName, Microsoft.Win32.RegistryHive.CurrentUser, Microsoft.Win32.RegistryView.Registry32))
            {
                ModUnlockerDownloadLink downloadInfo = UeModUnlocker.GetLatestDownloadLinkInfo();

                if (downloadInfo == null || string.IsNullOrEmpty(downloadInfo.Url))
                {
                    System.Windows.MessageBox.Show("Failed to get the latest download link. Check that you have internet and try again.", "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                MessageBoxResult result = System.Windows.MessageBox.Show("This will open your browser to download the Illusory Universal Mod Unlocker.\n\nLaunch the unlocker installer after it downloads to patch Session.\n\nDo you want to continue?", 
                                                                         "Notice!", 
                                                                         MessageBoxButton.YesNo, 
                                                                         MessageBoxImage.Warning, 
                                                                         MessageBoxResult.Yes);

                if (result == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo(downloadInfo.Url));
                }
            }
            else
            {
                string modUnlockerPath = RegistryHelper.GetExePathFromDisplayIcon(displayName, Microsoft.Win32.RegistryHive.CurrentUser, Microsoft.Win32.RegistryView.Registry32);
                Process.Start(modUnlockerPath);
            }
        }

        private void menuDeleteSelectedMap_Click(object sender, RoutedEventArgs e)
        {
            if (lstMaps.SelectedItem == null)
            {
                System.Windows.MessageBox.Show("Select a map to delete first!",
                                                "Notice!",
                                                MessageBoxButton.OK,
                                                MessageBoxImage.Information);
                return;
            }

            MapListItem selectedItem = lstMaps.SelectedItem as MapListItem;

            MessageBoxResult result = System.Windows.MessageBox.Show($"Are you sure you want to delete {selectedItem.DisplayName}?"
                                                                    , "Warning!"
                                                                    , MessageBoxButton.YesNo
                                                                    , MessageBoxImage.Warning
                                                                    , MessageBoxResult.Yes);

            if (result == MessageBoxResult.Yes)
            {
                ViewModel.DeleteSelectedMap(selectedItem);
            }
        }

        private void tabControl_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (tabControl.SelectedIndex != SelectedTabIndex)
            {
                SelectedTabIndex = tabControl.SelectedIndex;

                if (tabMainWindow.IsSelected && controlAssetStore.ViewModel.HasDownloadedMap)
                {
                    controlAssetStore.ViewModel.HasDownloadedMap = false;
                    ViewModel.ReloadAvailableMapsInBackground();
                }
            }

        }

        private void menuSecondMapToLoad_Click(object sender, RoutedEventArgs e)
        {
            if (lstMaps.SelectedItem == null)
            {
                System.Windows.MessageBox.Show("Select a map first!",
                                                "Notice!",
                                                MessageBoxButton.OK,
                                                MessageBoxImage.Information);
                return;
            }


            MapListItem selectedItem = lstMaps.SelectedItem as MapListItem;
            ViewModel.SetOrClearSecondMapToLoad(selectedItem);
        }

        private void mainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            ctrlTextureReplacer.ViewModel.MessageChanged -= TextureReplacer_MessageChanged;
            ImageCache.WriteToFile();
        }

        private void mainWindow_StateChanged(object sender, EventArgs e)
        {
            AppSettingsUtil.AddOrUpdateAppSettings(SettingKey.WindowState, this.WindowState.ToString());
        }

        private void menuOpenSaveFolder_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.OpenFolderToSaveFiles();
        }
    }
}
