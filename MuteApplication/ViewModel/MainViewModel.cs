using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.ComponentModel;
using System.Diagnostics;

using AudioSwitcher.AudioApi.CoreAudio;
using AudioSwitcher.AudioApi.CoreAudio.Interfaces;

using MuteApplication.ViewModel;
using MuteApplication.Utilities;

namespace MuteApplication.ViewModel
{
    public class MainViewModel : ViewModelBase
    {
        //HotKey _HotKey = new HotKey(Key.M, HotKey.KeyModifier.Ctrl | HotKey.KeyModifier.Shift, OnHotKeyHandler);
        HotKey _HotKey;


        private ICollectionView _ApplicationCollectionView;
        private CoreAudioDevice defaultPlaybackDevice;
        private ObservableCollection<AudioSwitcher.AudioApi.Session.IAudioSession> _Applications = new ObservableCollection<AudioSwitcher.AudioApi.Session.IAudioSession>();
        private ObservableCollection<AudioSwitcher.AudioApi.Session.IAudioSession> _SimilarApplications = new ObservableCollection<AudioSwitcher.AudioApi.Session.IAudioSession>();
        private CancellationTokenSource _cTSource;

        public MainViewModel()
        {
            defaultPlaybackDevice = new CoreAudioController().DefaultPlaybackDevice;

            //_Applications = ConvertIEnumToOC( defaultPlaybackDevice.SessionController.ActiveSessions() );

            UpdateActiveSessions();
                        
            _ApplicationCollectionView = CollectionViewSource.GetDefaultView(Applications);
            _ApplicationCollectionView.SortDescriptions.Add(new SortDescription("DisplayName", ListSortDirection.Ascending));
            _ApplicationCollectionView.Filter = SearchInListFilter;

            // set up hotkey
            _HotKey = new HotKey(Key.M, HotKey.KeyModifier.Ctrl | HotKey.KeyModifier.Shift, OnHotKeyHandler);
            //_HotKey.Dispose();
        }

        private static async Task PeriodicUpdate(Action onTick, TimeSpan interval, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                onTick?.Invoke();

                await Task.Delay(interval, cancellationToken);
            }
        }


        #region FilterForCollection
        private bool SearchInListFilter(object item)
        {
        AudioSwitcher.AudioApi.Session.IAudioSession session = item as AudioSwitcher.AudioApi.Session.IAudioSession;
            return session.DisplayName.IndexOf(SearchInListString, StringComparison.OrdinalIgnoreCase) >= 0;
        }
        #endregion


        private AudioSwitcher.AudioApi.Session.IAudioSession _SelectedApplication = null;
        public AudioSwitcher.AudioApi.Session.IAudioSession SelectedApplication
        {
            get { return _SelectedApplication; }
            set
            {
                if (value == null) return;
                _SelectedApplication = value;
                onPropertyChanged("SelectedApplication");
            }
        }




        public ICollectionView ApplicationCollectionView
        {
            get { return _ApplicationCollectionView; }
        }

        public ObservableCollection<AudioSwitcher.AudioApi.Session.IAudioSession> Applications
        {
            get { return _Applications; }
            set
            {
                Debug.WriteLine("Updating observable collection");
                _Applications = value;
                onPropertyChanged("Applications"); // does nothing
                //onPropertyChanged("ApplicationCollectionView"); // does nothing
            }
        }

        public ObservableCollection<AudioSwitcher.AudioApi.Session.IAudioSession> SimilarApplications
        {
            get { return _SimilarApplications; }
            set { _SimilarApplications = value; }
        }

        private string _SearchInListString = "";
        public string SearchInListString
        {
            get { return _SearchInListString; }
            set
            {
                _SearchInListString = value;
                onPropertyChanged("SearchInListString");
                _ApplicationCollectionView.Refresh();
            }
        }

        private string _StatusBarString = "";
        public string StatusBarString
        {
            get { return _StatusBarString; }
            set
            {
                _StatusBarString = value;
                onPropertyChanged("StatusBarString");
            }
        }

        private bool _CondeseSimilarSessions = true;
        public bool CondenseSimilarSessions
        {
            get { return _CondeseSimilarSessions; }
            set
            {
                _CondeseSimilarSessions = value;
                onPropertyChanged("CondenseSimilarSessions");
            }
        }

        private bool _AllSessions = false;
        public bool AllSessions
        {
            get { return _AllSessions; }
            set
            {
                _AllSessions = value;
                onPropertyChanged("AllSessions");
            }
        }

        private bool _AutoRefresh = false;
        public bool AutoRefresh
        {
            get { return _AutoRefresh; }
            set
            {
                _AutoRefresh = value;
            }
        }

        private string _RefreshIntervalString = "5";
        public string RefreshIntervalString
        {
            get { return _RefreshIntervalString; }
            set
            {
                try
                {
                    RefreshInterval = int.Parse(value);
                    _RefreshIntervalString = RefreshInterval.ToString();
                }
                catch { _RefreshIntervalString = ""; }
                onPropertyChanged("RefreshIntervalString");
            }
        }

        private int _RefreshInterval = 5;
        public int RefreshInterval
        {
            get { return _RefreshInterval; }
            set
            {
                if (value < 1) _RefreshInterval = 1;
                else if (value > 60) _RefreshInterval = 60;
                else _RefreshInterval = value;
            }
        }


        #region RefreshList
        public ICommand RefreshList
        {
            get
            {
                return new RelayCommand(ExecuteRefreshList, CanRefreshList);
            }
        }

        public void ExecuteRefreshList(object parameter)
        {
            UpdateActiveSessions();
        }

        public bool CanRefreshList(object parameter)
        {
            return !AutoRefresh;
        }
        #endregion

        #region AutoRefresh start and stop
        public ICommand AutoRefreshCommand
        {
            get
            {
                return new RelayCommand(ExecuteAutoRefreshCommand, CanAutoRefreshCommand);
            }
        }

        public void ExecuteAutoRefreshCommand(object parameter)
        {
            Debug.WriteLine("AutoRefreshCommand, AutoRefresh: " + AutoRefresh);
            if (AutoRefresh)
            {
                StatusBarString = "Enabled auto refresh.";
                _cTSource = new CancellationTokenSource();
                PeriodicUpdate(UpdateActiveSessions, TimeSpan.FromSeconds(RefreshInterval), _cTSource.Token);
            }
            else
            {
                _cTSource.Cancel();
                StatusBarString = "Disabled auto refresh.";
            }
        }

        public bool CanAutoRefreshCommand(object parameter)
        {
            return true;
        }
        #endregion

        #region Mute and Unmute
        // ICommand to mute and unmute
        public ICommand MuteSession
        {
            get
            {
                return new RelayCommand(ExecuteMuteSession, CanMuteSession);
            }
        }

        public void ExecuteMuteSession(object parameter)
        {
            SelectedApplication.IsMuted = true;
            if (CondenseSimilarSessions)
            {
                foreach (var item in SimilarApplications)
                {
                    if (item.DisplayName == SelectedApplication.DisplayName) item.IsMuted = true;
                }
            }
            StatusBarString = SelectedApplication.Id.Split('\\').Last().Split('%').First() + " is muted.";
        }

        public bool CanMuteSession(object parameter)
        {
            if (SelectedApplication == null) return false;
            return !SelectedApplication.IsMuted;
        }

        public ICommand UnmuteSession
        {
            get
            {
                return new RelayCommand(ExecuteUnmuteSession, CanUnmuteSession);
            }
        }

        public void ExecuteUnmuteSession(object parameter)
        {
            SelectedApplication.IsMuted = false;
            if (CondenseSimilarSessions)
            {
                foreach (var item in SimilarApplications)
                {
                    if (item.DisplayName == SelectedApplication.DisplayName) item.IsMuted = false;
                }
            }
            StatusBarString = SelectedApplication.Id.Split('\\').Last().Split('%').First() + " is unmuted.";
        }

        public bool CanUnmuteSession(object parameter)
        {
            if (SelectedApplication == null) return false;
            return SelectedApplication.IsMuted;
        }
        #endregion


        private void UpdateActiveSessions()
        {
            //AudioSwitcher.AudioApi.Session.IAudioSession SessTest = ApplicationCollectionView.CurrentItem as AudioSwitcher.AudioApi.Session.IAudioSession;

            Applications.Clear();
            SimilarApplications.Clear();

            IEnumerable<AudioSwitcher.AudioApi.Session.IAudioSession> SessionsTemp;
            bool sessTestBool = false;
            if (AllSessions)
            {
                SessionsTemp = defaultPlaybackDevice.SessionController.All();
            }
            else
            {
                SessionsTemp = defaultPlaybackDevice.SessionController.ActiveSessions();
            }

            foreach (var item in SessionsTemp)
            {
                if (item.IsSystemSession) continue;
                //if (item.Equals(SessTest)) sessTestBool = true;

                if (CondenseSimilarSessions)
                {
                    bool exists = false;
                    foreach (var v in Applications)
                    {
                        if (item.DisplayName == v.DisplayName)
                        {
                            exists = true;
                            break;
                        }
                    }
                    if (!exists) Applications.Add(item);
                    else SimilarApplications.Add(item);
                }
                else
                {
                    Applications.Add(item);
                }
                Debug.WriteLine(item.Id + " ID: " + item.ProcessId);
            }

            //if (sessTestBool) SelectedApplication = SessTest;

            // Does not update ICollectionView when the source is changed. Have to use Clear() and Add().
            //Applications = ConvertIEnumToOC(defaultPlaybackDevice.SessionController.ActiveSessions());
            
            Debug.WriteLine("Updated active sessions. Count: " + defaultPlaybackDevice.SessionController.ActiveSessions().Count() + " " + Applications.Count + " ");
        }


        private ObservableCollection<AudioSwitcher.AudioApi.Session.IAudioSession> ConvertIEnumToOC(IEnumerable<AudioSwitcher.AudioApi.Session.IAudioSession> original)
        {
            return new ObservableCollection<AudioSwitcher.AudioApi.Session.IAudioSession>(original.Cast<AudioSwitcher.AudioApi.Session.IAudioSession>());
        }

        #region Hotkey
        //[DllImport("user32.dll")]
        //private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        //[DllImport("user32.dll")]
        //private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        //private const int HOTKEY_ID = 9000;

        ////Modifiers:
        //private const uint MOD_NONE = 0x0000; //(none)
        //private const uint MOD_ALT = 0x0001; //ALT
        //private const uint MOD_CONTROL = 0x0002; //CTRL
        //private const uint MOD_SHIFT = 0x0004; //SHIFT
        //private const uint MOD_WIN = 0x0008; //WINDOWS
        ////CAPS LOCK:
        //private const uint VK_CAPITAL = 0x14;

        //private IntPtr _windowHandle;
        //private HwndSource _source;
        //protected override void OnSourceInitialized(EventArgs e)
        //{
        //    base.OnSourceInitialized(e);

        //    _windowHandle = new WindowInteropHelper(this).Handle;
        //    _source = HwndSource.FromHwnd(_windowHandle);
        //    _source.AddHook(HwndHook);

        //    RegisterHotKey(_windowHandle, HOTKEY_ID, MOD_CONTROL, VK_CAPITAL); //CTRL + CAPS_LOCK
        //}

        //private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        //{
        //    const int WM_HOTKEY = 0x0312;
        //    switch (msg)
        //    {
        //        case WM_HOTKEY:
        //            switch (wParam.ToInt32())
        //            {
        //                case HOTKEY_ID:
        //                    int vkey = (((int)lParam >> 16) & 0xFFFF);
        //                    if (vkey == VK_CAPITAL)
        //                    {
        //                        Debug.WriteLine("Hotkey pressed.");
        //                    }
        //                    handled = true;
        //                    break;
        //            }
        //            break;
        //    }
        //    return IntPtr.Zero;
        //}

        //protected override void OnClosed(EventArgs e)
        //{
        //    _source.RemoveHook(HwndHook);
        //    UnregisterHotKey(_windowHandle, HOTKEY_ID);
        //    base.OnClosed(e);
        //}
        #endregion

        #region HotKey
        private void OnHotKeyHandler(HotKey hotKey)
        {
            Debug.WriteLine("Inside OnHotKeyHandler");
            if (SelectedApplication.IsMuted) ExecuteUnmuteSession(null);
            else ExecuteMuteSession(null);
        }
        #endregion

        #region Exit application
        private bool _ShuttingDown = false;

        public void ExitApplication()
        {
            ExecuteExitApplication();
        }       

        private void ExecuteExitApplication()
        {
            if (_ShuttingDown) return;
            _ShuttingDown = true;
            _HotKey.Dispose();
            Debug.WriteLine("Inside ExecuteExitApplication");
            System.Windows.Application.Current.Shutdown();
        }
        #endregion

    }
}
