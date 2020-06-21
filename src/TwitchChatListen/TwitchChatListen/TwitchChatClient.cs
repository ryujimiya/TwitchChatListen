using IvyFEMProtoApp;
using Meebey.SmartIrc4net;
using MyUtilLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing; // Color
using System.Windows;

namespace TwitchChatListen
{
    //////////////////////////////////////////////////////////////
    // デリゲート宣言
    //////////////////////////////////////////////////////////////
    public delegate void OnStopDeleagte(TwitchChatClient sender);
    public delegate void OnBusyChangeDelegate(TwitchChatClient sender, bool isBusy);
    public delegate void OnChannelMessageDelegate(TwitchChatClient sender, string channelName, string nickName, string host, string message);
    public delegate void OnTopicDelegate(TwitchChatClient sender, string channelName, string who, string topic);
    public delegate void OnUserCountChangeDelegate(TwitchChatClient sender, string channelName);
    public delegate void OnModeChangeDelegate(TwitchChatClient sender);
    public delegate void OnEndOfNamesDelegate(TwitchChatClient sender, string channelName);
    public delegate void OnQueryMessageDelegate(TwitchChatClient sender, string nickName, string host, string message);

    public class TwitchChatClient
    {
        //////////////////////////////////////////////////////////////
        // 定数
        //////////////////////////////////////////////////////////////
        private static readonly System.Text.Encoding Encoding = System.Text.Encoding.UTF8;
        private static readonly int SendDelay = 0;
        private static readonly int ServerPort = 6667;

        //////////////////////////////////////////////////////////////
        // 変数
        //////////////////////////////////////////////////////////////
        // irc://irc.chat.twitch.tv:6667
        // IRCサーバー名
        public string ServerName { get; private set; } = "irc.chat.twitch.tv";
        // ニックネーム(必須)
        public string NickName { get; private set; } = "";
        // 本名
        public string RealName { get; private set; } = "";
        // ユーザー名
        public string UserName { get; private set; } = "";
        // 固定ハンドルネーム
        private string FixedNickName = "";
        // パスワード(必須)
        public string Password { get; private set; } = "";

        private bool Disposed = false;
        private MainWindow MainWindow = null;
        // IRCクライアント
        private IrcClient Irc = null;
        private Thread IrcClientThread = null;
        // 開始、または終了の処理中
        private bool IsBusy = false;
        // スレッド稼働中排他(開始時の二重起動チェック用) 初期状態:シグナルON
        private AutoResetEvent ThreadAutoResetEvent = new AutoResetEvent(true);
        private ChannelInfoEx[] ChannelInfoExs = null;
        private Color[] Colors = null;
        // key: nickName, value:ChannelUserInfoEx
        private Hashtable UserHashtable = new Hashtable();
        // 意図した切断中
        private bool IsDisconnecting = false;
        // Kickメソッドのシグナル
        private AutoResetEvent KickReplyEvent = null;
        // Kickメソッドのエラーメッセージ
        private string KickErrorMessage = "";
        // Banメソッドのシグナル
        private AutoResetEvent ModeReplyEvent = null;
        // Banメソッドのエラーメッセージ
        private string ModeErrorMessage = "";
        // IRCクライアントスレッドのエラーメッセージ
        private string ThreadErrorMessage = "";
        // チャンネルからBANされたか?(どこか1つでもBanになった場合フラグが立つ)
        private bool IsBannedFromChannel = false;

        //////////////////////////////////////////////////////////////
        // イベント
        //////////////////////////////////////////////////////////////
        // スレッド終了イベントハンドラ
        public event OnStopDeleagte OnStop = null;
        // ボタンの無効化/有効化処理用イベントハンドラ
        public event OnBusyChangeDelegate OnBusyChange = null;
        // コメント受信イベントハンドラ
        public event OnChannelMessageDelegate OnChannelMessage = null;
        // トピック取得イベントハンドラ
        public event OnTopicDelegate OnTopic = null;
        // ユーザー数変更(NAMES,JOIN,KICK,PART)受信イベントハンドラ
        public event OnUserCountChangeDelegate OnUserCountChange = null;
        // モード変更通知イベントハンドラ
        public event OnModeChangeDelegate OnModeChange = null;
        // トーク受信イベントハンドラ
        public event OnQueryMessageDelegate OnQueryMessage = null;


        ///////////////////////////////////////////////////////////////
        // static functions
        ///////////////////////////////////////////////////////////////
        public static bool IsValidChannelFormat(string channelName)
        {
            MatchCollection matches = Regex.Matches(channelName, "^[#&][^ ,\\r\\n]+$");
            return (matches.Count == 1);
        }

        ///////////////////////////////////////////////////////////////
        // 初期化/終了
        ///////////////////////////////////////////////////////////////
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="parent"></param>
        /// <param name="fixedNickName"></param>
        public TwitchChatClient(MainWindow parent, string fixedNickName, string password)
        {
            MainWindow = parent;
            FixedNickName = fixedNickName;
            Password = password;

            // 色テーブル初期化
            GenerateColors();
        }

        /// <summary>
        /// クライアントを開始する
        /// </summary>
        /// <param name="channelNames"></param>
        public void Start(string[] channelNames)
        {
            System.Diagnostics.Debug.WriteLine("TwitchChatClient Start");

            // IRCクライアントスレッドが稼働していないことを保証する
            if (!WaitForThreadTerminate("Start"))
            {
                return;
            }

            // 現在のチャンネルリストの破棄
            ClearChannelNames();

            // チャンネル設定
            SetChannelNames(channelNames);

            // IRCクライアントスレッド開始
            IrcClientThreadStart();
        }

        /// <summary>
        /// クライアントを停止する
        /// </summary>
        public void Stop()
        {
            System.Diagnostics.Debug.WriteLine("TwitchChatClient Stop");
            IrcClientThreadTerminate();
        }

        /// <summary>
        /// ファイナライザ
        /// </summary>
        ~TwitchChatClient()
        {
            Dispose(false);
        }

        /// <summary>
        /// 破棄処理
        /// </summary>
        public void Dispose()
        {
            Dispose(true);

            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 破棄処理
        /// NOTE:
        /// </summary>
        /// <param name="disposing">true:アンマネージメモリ/マネージメモリを開放する false:アンマネージメモリのみ開放する</param>
        protected virtual void Dispose(bool disposing)
        {
            System.Diagnostics.Debug.WriteLine("TwitchChatClient Dispose(" + disposing + ")");

            if (!this.Disposed)
            {
                // IRCクライアント切断
                IrcClientThreadTerminate();
                System.Diagnostics.Debug.WriteLine("Irc finalize done.");

                // イベントのクリア
                OnStop = null;
                OnBusyChange = null;
                OnChannelMessage = null;
                OnTopic = null;
                OnUserCountChange = null;
                OnModeChange = null;

                this.Disposed = true;
            }
        }

        ///////////////////////////////////////////////////////////////
        // スレッド起動/終了API
        ///////////////////////////////////////////////////////////////
        /// <summary>
        /// IRCクライアントスレッドを開始する
        /// </summary>
        private void IrcClientThreadStart()
        {
            // IRCクライアントスレッドが稼働していないことを保証する
            if (!WaitForThreadTerminate("IrcClientThread_Start"))
            {
                return;
            }

            if (ChannelInfoExs == null)
            {
                //MessageBox_ShowErrorAsync("チャンネルが指定されていません", "");
                return;
            }

            // ニックネーム生成
            if (FixedNickName != "")
            {
                // 固定ハンドルネームを設定
                NickName = FixedNickName;
                RealName = NickName;
                UserName = NickName;
            }
            else
            {
                return;
                /*
                // ニックネーム生成
                GenerateNickName();
                */
            }
            // IRCクライアントスレッド開始
            Thread t = new Thread(new ThreadStart(IrcClientThreadProc));
            t.Name = "TwitchChatClient IrcClientThread";
            t.Start();
            IrcClientThread = t;
        }

        /// <summary>
        /// IRCクライアントスレッドを停止する
        /// </summary>
        private void IrcClientThreadTerminate()
        {
            if (IrcClientThread != null && IrcClientThread.IsAlive)
            {
                IsDisconnecting = true;

                // ボタンの無効化
                IsBusy = true;
                if (OnBusyChange != null)
                {
                    MainWindow.Dispatcher.Invoke(OnBusyChange, new object[] { this, IsBusy });
                }

                // IRCクライアント切断
                try
                {
                    // 自動再接続を止める
                    Irc.AutoReconnect = false;
                    Irc.AutoRelogin = false;
                    Irc.AutoRejoin = false;

                    //QUIT
                    Irc.RfcQuit();
                    System.Diagnostics.Debug.WriteLine("Irc quit done");

                    System.Diagnostics.Debug.WriteLine("IrcClientThread Abort ...");
                    IrcClientThread.Abort();
                    System.Diagnostics.Debug.WriteLine("IrcClientThread Abort done");

                    // IRCクライアントスレッドでThreadAbortExceptionをキャッチしているので、スレッドは正常に終了する
                    System.Diagnostics.Debug.WriteLine("IrcClientThread Join ...");
                    while (!IrcClientThread.Join(100))
                    {
                        WPFUtils.DoEvents();
                    }
                    System.Diagnostics.Debug.WriteLine("IrcClientThread Join done");
                }
                catch (Exception exception)
                {
                    System.Diagnostics.Debug.WriteLine(exception.Message + " " + exception.StackTrace);
                    MessageBox_ShowErrorAsync(exception.Message, "スレッド終了エラー");
                }
                IsDisconnecting = false;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("IrcClientThread is not alive!");
            }
        }

        /*
        /// <summary>
        /// 自動退出チェック処理
        ///   自分しかチャンネルにいない場合は、退出する
        /// </summary>
        /// <param name="channelName"></param>
        private void ChkAutoPart(string channelName)
        {
            int userCnt = 0;

            userCnt = GetChannelUserCount(channelName);
            if (userCnt <= 1)  // 自分しかいない -->チャンネルなし
            {
                // チャンネルを存在しないにする
                ChannelInfoEx channelInfoEx = GetChannelInfoExByRealName(channelName);
                if (channelInfoEx != null)
                {
                    channelInfoEx.IsExisted = false; // 存在しないにする
                    channelInfoEx.IsParting = true;  // 退出するのでフラグをセット
                }
                // 退出する
                Irc.RfcPart(channelName);
                System.Diagnostics.Debug.WriteLine("chkAutoPart: RfcPart" + channelName + " done.");
            }
        }
        */

        ///////////////////////////////////////////////////////////////
        // スレッド排他チェック
        ///////////////////////////////////////////////////////////////
        /// <summary>
        /// IRCクライアントスレッドが稼働していないことを保証する(スレッド起動時の二重起動チェック用)
        /// </summary>
        /// <param name="channelName">呼び出し元判別タグ</param>
        /// <returns></returns>
        private bool WaitForThreadTerminate(string tag)
        {
            bool ret;

            // タイムアウト5秒
            ret = ThreadAutoResetEvent.WaitOne(5000);
            if (!ret)
            {
                System.Diagnostics.Debug.WriteLine("[ERROR]スレッド稼働中に再度起動しようとしました。(" + tag + ")");
                MessageBox_ShowErrorAsync("[ERROR]スレッド稼働中に再度起動しようとしました。(" + tag + ")", "");
            }
            // シグナル状態に戻しておく
            ThreadAutoResetEvent.Set();
            return ret;
        }

        ///////////////////////////////////////////////////////////////
        // スレッド
        ///////////////////////////////////////////////////////////////
        /// <summary>
        /// IRCクライアントスレッド
        /// </summary>
        private void IrcClientThreadProc()
        {
            System.Diagnostics.Debug.WriteLine("IrcClientThreadProc start");
            bool ret;

            // スレッド稼働中ロック
            ThreadAutoResetEvent.WaitOne();

            // スレッド初期化処理
            ret = IrcClientThreadProcInit();

            if (ret)
            {
                // スレッドメインループ
                IrcClientThreadProcMain();
            }

            // スレッド終了処理
            IrcClientThreadProcDispose();

            // スレッド稼働中ロック解除
            ThreadAutoResetEvent.Set();

            System.Diagnostics.Debug.WriteLine("IrcClientThreadProc end.");
        }

        /// <summary>
        /// IRCクライアントスレッド[初期化処理]
        /// </summary>
        private bool IrcClientThreadProcInit()
        {
            ThreadErrorMessage = "";

            // ボタンの無効化
            IsBusy = true;
            if (OnBusyChange != null)
            {
                MainWindow.Dispatcher.Invoke(OnBusyChange, new object[] { this, IsBusy });
            }

            // 放送者初期化
            foreach (ChannelInfoEx channelInfoEx in ChannelInfoExs)
            {
                channelInfoEx.BcName = "";
                //channelInfoEx.IsWaitForBroadcasterMessage = false;
            }

            ////////////////////////////////////////////////////////////////////
            // IRCクライアント生成
            Irc = new IrcClient();

            // 文字コード
            Irc.Encoding = Encoding;

            // 送信ディレイ (Excess floodを防ぐため、ある程度間隔を置いて送信する)
            Irc.SendDelay = SendDelay;

            // チャンネルシンク(Irc.GetChannel()等使用可能になる)
            Irc.ActiveChannelSyncing = true;

            // 意図しない切断の場合は、再接続する
            Irc.AutoReconnect = true;
            Irc.AutoRelogin = true;
            Irc.AutoRejoin = true;

            // ニックネームが重複しているとき自動で変更しない
            Irc.AutoNickHandling = false;

            Irc.OnConnected += Irc_OnConnected;
            Irc.OnConnecting += Irc_OnConnecting;
            Irc.OnConnectionError += Irc_OnConnectionError;
            Irc.OnDisconnected += Irc_OnDisconnected;
            Irc.OnDisconnecting += Irc_OnDisconnecting;
            Irc.OnQueryMessage += Irc_OnQueryMessage;
            Irc.OnQueryAction += Irc_OnQueryAction;
            Irc.OnChannelMessage += Irc_OnChannelMessage;
            Irc.OnChannelAction += Irc_OnChannelAction;
            Irc.OnError += Irc_OnError;
            Irc.OnErrorMessage += Irc_OnErrorMessage;
            Irc.OnTopic += Irc_OnTopic;
            Irc.OnTopicChange += Irc_OnTopicChange;
            Irc.OnNames += Irc_OnNames;
            Irc.OnWho += Irc_OnWho;
            Irc.OnList += Irc_OnList;
            Irc.OnJoin += Irc_OnJoin;
            Irc.OnPart += Irc_OnPart;
            Irc.OnKick += Irc_OnKick;
            Irc.OnBan += Irc_OnBan;
            Irc.OnUnban += Irc_OnUnban;
            Irc.OnQuit += Irc_OnQuit;
            Irc.OnChannelModeChange += Irc_OnChannelModeChange;
            Irc.OnUserModeChange += Irc_OnUserModeChange;
            Irc.OnModeChange += Irc_OnModeChange;
            Irc.OnRawMessage += Irc_OnRawMessage;
            Irc.OnWriteLine += Irc_OnWriteLine;

            ////////////////////////////////////////////////////////////////////
            // チャットサーバーへ接続する
            string[] serverlist;
            serverlist = new string[] { ServerName };
            int port = ServerPort;
            try
            {
                Irc.Connect(serverlist, port);
            }
            catch (ConnectionException exception)
            {
                //System.Diagnostics.Debug.WriteLine("couldn't connect! Reason: " + exception.Message);
                System.Diagnostics.Debug.WriteLine(exception.Message + " " + exception.StackTrace);
                MessageBox_ShowErrorAsync("サーバーに接続できませんでした。" + System.Environment.NewLine + exception.Message, "");
                return false;
            }

            return true;
        }

        /// <summary>
        /// IRCクライアントスレッド[メインループ]
        /// </summary>
        private void IrcClientThreadProcMain()
        {
            //////////////////////////////////////////////////////////////////////
            // メインループ
            if (Irc.IsConnected)
            {
                try
                {
                    // ログイン: ニックネーム、ユーザー通知
                    //Irc.Login(NickName, RealName, 0, UserName);
                    Irc.Login(NickName, RealName, 0, UserName, Password);

                    // チャンネルに参加する
                    foreach (ChannelInfoEx channelInfoEx in ChannelInfoExs)
                    {
                        Irc.RfcJoin(channelInfoEx.Name);
                    }

                    // ボタンの有効化
                    IsBusy = false;
                    if (OnBusyChange != null)
                    {
                        MainWindow.Dispatcher.Invoke(OnBusyChange, new object[] { this, IsBusy });
                    }

                    // 電文受信ループ
                    Irc.Listen();
                }
                catch (ConnectionException exception)
                {
                    System.Diagnostics.Debug.WriteLine(exception.Message + " " + exception.StackTrace);
                    //System.Diagnostics.Debug.WriteLine("Error occurred! Connection Exception Message: " + exception.Message);
                    //System.Diagnostics.Debug.WriteLine("Exception: " + exception.StackTrace);
                    ThreadErrorMessage += "ConnectionException: " + exception.Message + System.Environment.NewLine;
                }
                catch (ThreadAbortException exception)
                {
                    Thread.ResetAbort();
                    System.Diagnostics.Debug.WriteLine("IrcClientThread aborted[1]");
                    System.Diagnostics.Debug.WriteLine(exception.Message + " " + exception.StackTrace);
                }
                catch (Exception exception)
                {
                    //System.Diagnostics.Debug.WriteLine("Error occurred! Message: " + exception.Message);
                    //System.Diagnostics.Debug.WriteLine("Exception: " + exception.StackTrace);
                    System.Diagnostics.Debug.WriteLine(exception.Message + " " + exception.StackTrace);
                    ThreadErrorMessage += "Exception: " + exception.Message + System.Environment.NewLine + exception.StackTrace + System.Environment.NewLine;
                }
            }
        }

        /// <summary>
        /// IRCクライアントスレッド[終了処理]
        /// </summary>
        private void IrcClientThreadProcDispose()
        {
            //////////////////////////////////////////////////////////////////////
            // 終了処理
            if (Irc.IsConnected)
            {
                try
                {
                    Irc.Disconnect();
                    System.Diagnostics.Debug.WriteLine("Irc disconnect done.");
                }
                catch (ThreadAbortException exception)
                {
                    Thread.ResetAbort();
                    System.Diagnostics.Debug.WriteLine("IrcClientThread aborted[2]");
                    System.Diagnostics.Debug.WriteLine(exception.Message + " " + exception.StackTrace);
                    ThreadErrorMessage += "Aborted while disconnecting Irc." + System.Environment.NewLine;
                }
                catch (ConnectionException exception)
                {
                    //System.Diagnostics.Debug.WriteLine("Error occurred! Connection Exception Message: " + exception.Message);
                    //System.Diagnostics.Debug.WriteLine("Exception: " + exception.StackTrace);
                    System.Diagnostics.Debug.WriteLine(exception.Message + " " + exception.StackTrace);
                    ThreadErrorMessage += "ConnectionException: " + exception.Message + System.Environment.NewLine;
                }
                catch (Exception exception)
                {
                    //System.Diagnostics.Debug.WriteLine("Error occurred! Message: " + exception.Message);
                    //System.Diagnostics.Debug.WriteLine("Exception: " + exception.StackTrace);
                    System.Diagnostics.Debug.WriteLine(exception.Message + " " + exception.StackTrace);
                    ThreadErrorMessage += "Exception: " + exception.Message + System.Environment.NewLine + exception.StackTrace + System.Environment.NewLine;
                }
            }

            // 以下イベントは、form側でThread.Joinをコールしているとブロックする
            if (OnStop != null)
            {
                MainWindow.Dispatcher.Invoke(OnStop, new object[] { this });
            }
            // ボタンの有効化(初期化処理途中で切断等が行われた場合、ボタンが無効のままになるのを防ぐ)
            if (IsBusy)
            {
                IsBusy = false;
                if (OnBusyChange != null)
                {
                    MainWindow.Dispatcher.Invoke(OnBusyChange, new object[] { this, IsBusy });
                }
            }

            Irc.OnConnected -= Irc_OnConnected;
            Irc.OnConnecting -= Irc_OnConnecting;
            Irc.OnConnectionError -= Irc_OnConnectionError;
            Irc.OnDisconnected -= Irc_OnDisconnected;
            Irc.OnDisconnecting -= Irc_OnDisconnecting;
            Irc.OnQueryMessage -= Irc_OnQueryMessage;
            Irc.OnQueryAction -= Irc_OnQueryAction;
            Irc.OnChannelMessage -= Irc_OnChannelMessage;
            Irc.OnChannelAction -= Irc_OnChannelAction;
            Irc.OnError -= Irc_OnError;
            Irc.OnErrorMessage -= Irc_OnErrorMessage;
            Irc.OnTopic -= Irc_OnTopic;
            Irc.OnTopicChange -= Irc_OnTopicChange;
            Irc.OnNames -= Irc_OnNames;
            Irc.OnWho -= Irc_OnWho;
            Irc.OnList -= Irc_OnList;
            Irc.OnJoin -= Irc_OnJoin;
            Irc.OnPart -= Irc_OnPart;
            Irc.OnKick -= Irc_OnKick;
            Irc.OnBan -= Irc_OnBan;
            Irc.OnUnban -= Irc_OnUnban;
            Irc.OnQuit -= Irc_OnQuit;
            Irc.OnChannelModeChange -= Irc_OnChannelModeChange;
            Irc.OnUserModeChange -= Irc_OnUserModeChange;
            Irc.OnModeChange -= Irc_OnModeChange;
            Irc.OnRawMessage -= Irc_OnRawMessage;
            Irc.OnWriteLine -= Irc_OnWriteLine;

            // 再接続の時必要なのでチャンネルリストは破棄しない
            //channelInfoExs = null;
            UserHashtable.Clear();

            if (!IsDisconnecting)  // 意図したスレッド終了でない場合
            {
                // 最後の改行を削除
                if (ThreadErrorMessage.LastIndexOf(System.Environment.NewLine) == ThreadErrorMessage.Length - 2)
                {
                    ThreadErrorMessage = ThreadErrorMessage.Substring(0, ThreadErrorMessage.Length - 2);
                }
                MessageBox_ShowErrorAsync("IRCの接続が切れました。" + System.Environment.NewLine + ThreadErrorMessage, "");
            }
        }

        ///////////////////////////////////////////////////////////////
        // イベント
        ///////////////////////////////////////////////////////////////
        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Irc_OnConnected(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("--------------------(Irc_OnConnected)");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Irc_OnConnecting(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("--------------------(Irc_OnConnecting)");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Irc_OnConnectionError(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("--------------------(Irc_OnConnectionError)");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Irc_OnDisconnected(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("--------------------(Irc_OnDisconnected)");

            // AutoReconnectの場合があるので
            // チャンネル情報はクリアしない
            // 参加状態を退出にする
            foreach (ChannelInfoEx channelInfoEx in ChannelInfoExs)
            {
                //channelInfoEx.RealName = "";
                channelInfoEx.IsJoined = false;
                channelInfoEx.IsExisted = true;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Irc_OnDisconnecting(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("--------------------(Irc_OnDisconnecting)");
        }

        /// <summary>
        /// IRCチャンネルメッセージ受信イベント
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Irc_OnChannelMessage(object sender, IrcEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("--------------------(Irc_OnChannelMessage)");
            if (e.Data.MessageArray != null)
            {
                foreach (string str in e.Data.MessageArray)
                {
                    System.Diagnostics.Debug.WriteLine(str);
                }

                // メッセージを登録
                string channelName = e.Data.Channel;
                channelName = channelName.ToLower();
                ChannelInfoEx channelInfoEx = GetChannelInfoExByRealName(channelName);
                if (channelInfoEx != null)
                {
                    channelInfoEx.AddMessage(e.Data.Nick, channelName);
                }

                // メッセージの追加
                if (OnChannelMessage != null)
                {
                    ChannelUser channelUser = Irc.GetChannelUser(e.Data.Channel, e.Data.Nick);
                    string host = "";
                    if (channelUser != null && channelUser.Host != null)
                    {
                        host = channelUser.Host;
                    }
                    // GUI側へ通知
                    MainWindow.Dispatcher.Invoke(OnChannelMessage, new object[] { this, e.Data.Channel, e.Data.Nick, host, e.Data.Message });
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[ERROR]e.Data.MessageArray is null");
            }
        }

        /// <summary>
        /// IRCチャンネルアクションイベント
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Irc_OnChannelAction(object sender, ActionEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("--------------------(Irc_OnChannelAction)");
            if (e.Data.MessageArray != null)
            {
                foreach (string str in e.Data.MessageArray)
                {
                    System.Diagnostics.Debug.WriteLine(str);
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[ERROR]e.Data.MessageArray is null");
            }
        }

        /// <summary>
        /// IRCクエリーメッセージ受信イベント
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Irc_OnQueryMessage(object sender, IrcEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("--------------------(Irc_OnQueryMessage)");
            if (e.Data.MessageArray != null)
            {
                foreach (string str in e.Data.MessageArray)
                {
                    System.Diagnostics.Debug.WriteLine(str);
                }
                if (OnQueryMessage != null)
                {
                    // GUI側へ通知
                    MainWindow.Dispatcher.Invoke(OnQueryMessage, new object[] { this, e.Data.Nick, e.Data.Host, e.Data.Message });
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[ERROR]e.Data.MessageArray is null");
            }
        }

        /// <summary>
        /// IRCクエリーアクションイベント
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Irc_OnQueryAction(object sender, ActionEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("--------------------(Irc_OnQueryAction)");
            if (e.Data.MessageArray != null)
            {
                foreach (string str in e.Data.MessageArray)
                {
                    System.Diagnostics.Debug.WriteLine(str);
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[ERROR]e.Data.MessageArray is null");
            }
        }

        /// <summary>
        /// IRCエラー受信イベント
        ///   NOTE: ERROR :Closing Link :Twitchreamer-xxxxxx[host](Excess Flood)等のIRCエラーメッセージを受信したときに発生するイベントです。
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Irc_OnError(object sender, ErrorEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("--------------------(Irc_OnError)");
            System.Diagnostics.Debug.WriteLine("Error: " + e.ErrorMessage);

            bool doErrorProcess = true;

            bool closingLinkErrorProcess = Irc_ClosingLinkErrorProcess(sender, e);
            if (!closingLinkErrorProcess)
            {
                // エラー処理をしない(内部で処理した)の場合
                doErrorProcess = false; // エラー処理しない(伝搬させる)
            }

            if (doErrorProcess)
            {
                MessageBox_ShowErrorAsync(e.ErrorMessage, "IRCエラー");
            }
        }

        /// <summary>
        /// Closing Link(Excess Flood等)エラー処理
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <returns></returns>
        private bool Irc_ClosingLinkErrorProcess(object sender, ErrorEventArgs e)
        {
            bool doErrorProcess = true; // 初期値:エラー処理をする

            // 該当メッセージか判定
            if (e.ErrorMessage.IndexOf("Closing Link") >= 0)
            {
                // Closing Linkエラー処理スレッドを起動
                Thread t = new Thread(new ParameterizedThreadStart(closingLinkErrorThread));
                t.Name = "TwitchChatClient closingLinkErrorThread";
                t.Start(e);

                doErrorProcess = false;
            }

            return doErrorProcess;
        }

        /// <summary>
        /// Closing Link(Excess Flood等)エラー処理スレッド
        /// </summary>
        private void closingLinkErrorThread(object parameter)
        {
            ErrorEventArgs e = parameter as ErrorEventArgs;

            // チャンネルから退出するリクエスト送信
            foreach (ChannelInfoEx channelInfoEx in ChannelInfoExs)
            {
                System.Diagnostics.Debug.WriteLine("nickNameInUseErrorThread PART " + channelInfoEx.Name);
                Irc.RfcPart(channelInfoEx.Name);
                channelInfoEx.IsParting = true;
            }

            IrcClientThreadTerminate();

            //MessageBox_ShowAsync(e.ErrorMessage, "サーバーから切断されました。再接続します...");

            // IRCクライアントスレッド開始
            IrcClientThreadStart();
        }

        /// <summary>
        /// IRCエラー応答受信イベント
        ///     Note: ReplyCodeがエラーのメッセージを受信したときに発生するイベントです
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Irc_OnErrorMessage(object sender, IrcEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("--------------------(Irc_OnErrorMessage)");
            System.Diagnostics.Debug.WriteLine("ErrorReply: " + e.Data.ReplyCode);
            bool doErrorProcess = true;

            switch (e.Data.ReplyCode)
            {
                case ReplyCode.ErrorNotRegistered:  // ログイン時に毎回発生するので煩わしい エラー扱いにしない
                    doErrorProcess = false;
                    break;
                case ReplyCode.ErrorNoSuchChannel:
                    doErrorProcess = false;
                    break;
                case ReplyCode.ErrorUnknownCommand:
                    doErrorProcess = false;
                    break;
            }

            // BANされてチャンネルに入れないとき
            bool bannedFromChannelDoErrorProcess = Irc_BannedFromChannelProcess(sender, e);
            if (!bannedFromChannelDoErrorProcess)
            {
                // エラー処理をしない(内部で処理した)の場合
                doErrorProcess = false; // エラー処理しない(伝搬させる)
            }

            bool nicknameInUseErrorProcess = Irc_NicknameInUseErrorProcess(sender, e);
            if (!nicknameInUseErrorProcess)
            {
                // エラー処理をしない(内部で処理した)の場合
                doErrorProcess = false; // エラー処理しない(伝搬させる)
            }

            // Kickメソッドのエラー応答捕捉
            bool kickDoErrorProcess = Irc_KickErrorReplyProcess(sender, e);
            if (!kickDoErrorProcess)
            {
                // エラー処理をしない(内部で処理した)の場合
                doErrorProcess = false; // エラー処理しない(伝搬させる)
            }

            // Ban/Unbanメソッドのエラー応答処理
            bool modeDoErrorProcess = Irc_ModeErrorReplyProcess(sender, e);
            if (!modeDoErrorProcess)
            {
                // エラー処理をしない(内部で処理した)の場合
                doErrorProcess = false; // エラー処理しない(伝搬させる)
            }

            if (doErrorProcess)
            {
                MessageBox_ShowErrorAsync(e.Data.Message, "");
            }
        }

        /// <summary>
        /// チャンネルからBANされた場合のエラー処理
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <returns></returns>
        private bool Irc_BannedFromChannelProcess(object sender, IrcEventArgs e)
        {
            bool doErrorProcess = true; // 初期値:エラー処理をする

            if (e.Data.ReplyCode == ReplyCode.ErrorBannedFromChannel)  // BANされてチャンネルに入れないとき
            {
                IsBannedFromChannel = true;
                MessageBox_ShowAsync("チャンネル参加が禁止(Ban)されています");
                doErrorProcess = false;
            }

            return doErrorProcess;
        }

        /// <summary>
        /// ニックネーム使用中エラー処理
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <returns></returns>
        private bool Irc_NicknameInUseErrorProcess(object sender, IrcEventArgs e)
        {
            bool doErrorProcess = true; // 初期値:エラー処理をする

            if (e.Data.ReplyCode == ReplyCode.ErrorNicknameInUse)  // ニックネーム使用中
            {
                // ニックネーム使用中エラー処理スレッドを起動
                Thread t = new Thread(new ThreadStart(nickNameInUseErrorThread));
                t.Name = "TwitchChatClient nickNameInUseErrorThread";
                t.Start();

                doErrorProcess = false;
            }

            return doErrorProcess;
        }

        /// <summary>
        /// ニックネーム使用中エラー処理スレッド
        /// </summary>
        private void nickNameInUseErrorThread()
        {
            // チャンネルから退出するリクエスト送信
            foreach (ChannelInfoEx channelInfoEx in ChannelInfoExs)
            {
                System.Diagnostics.Debug.WriteLine("nickNameInUseErrorThread PART " + channelInfoEx.Name);
                channelInfoEx.IsParting = true;
                Irc.RfcPart(channelInfoEx.Name);
            }

            IrcClientThreadTerminate();

            if (FixedNickName.Length > 0)
            {
                MessageBox_ShowAsync("IRCの接続を切りました。ニックネームを変えてください。");
            }
            else
            {
                //MessageBox_ShowAsync("ニックネームが使用中でした。別のニックネームで接続します...");
                // IRCクライアントスレッド開始
                IrcClientThreadStart();
            }
        }

        /// <summary>
        /// Kickメソッドのエラー応答処理
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <returns></returns>
        private bool Irc_KickErrorReplyProcess(object sender, IrcEventArgs e)
        {
            bool kickDoErrorProcess = true; // エラー処理をする

            // Kickメソッドのエラー応答捕捉
            switch (e.Data.ReplyCode)
            {
                case ReplyCode.ErrorNeedMoreParams:
                case ReplyCode.ErrorNoSuchChannel:
                case ReplyCode.ErrorBadChannelMask:
                case ReplyCode.ErrorChannelOpPrivilegesNeeded:
                case ReplyCode.ErrorNotOnChannel:
                    if (KickReplyEvent != null)
                    {
                        KickErrorMessage = e.Data.Message;
                        KickReplyEvent.Set();
                        kickDoErrorProcess = false; // エラー処理をしない
                    }
                    break;
            }

            return kickDoErrorProcess;
        }

        /// <summary>
        /// Banメソッドのエラー応答処理
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <returns></returns>
        private bool Irc_ModeErrorReplyProcess(object sender, IrcEventArgs e)
        {
            bool modeDoErrorProcess = true; // エラー処理をする

            // Kickメソッドのエラー応答捕捉
            switch (e.Data.ReplyCode)
            {
                case ReplyCode.ErrorNeedMoreParams:
                case ReplyCode.ErrorChannelOpPrivilegesNeeded:
                case ReplyCode.ErrorNoSuchNickname:
                case ReplyCode.ErrorNotOnChannel:
                case ReplyCode.ErrorKeySet:
                case ReplyCode.ErrorUnknownMode:
                case ReplyCode.ErrorNoSuchChannel:
                case ReplyCode.ErrorUsersDoNotMatch:
                case ReplyCode.ErrorUserModeUnknownFlag:
                    if (ModeReplyEvent != null)
                    {
                        ModeErrorMessage = e.Data.Message;
                        ModeReplyEvent.Set();
                        modeDoErrorProcess = false; // エラー処理をしない
                    }
                    break;
            }

            return modeDoErrorProcess;
        }

        /// <summary>
        /// IRCトピックイベント
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Irc_OnTopic(object sender, TopicEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("--------------------(Irc_OnTopic)");
            if (e.Data.MessageArray != null)
            {
                foreach (string str in e.Data.MessageArray)
                {
                    System.Diagnostics.Debug.WriteLine(str);
                }

                // トピックの更新
                if (OnTopic != null)
                {
                    MainWindow.Dispatcher.Invoke(OnTopic, new object[] { this, e.Channel, "", e.Topic });
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[ERROR]e.Data.MessageArray is null");
            }
        }

        /// <summary>
        /// IRCトピック変更イベント
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Irc_OnTopicChange(object sender, TopicChangeEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("--------------------(Irc_OnTopicChange)");
            if (e.Data.MessageArray != null)
            {
                foreach (string str in e.Data.MessageArray)
                {
                    System.Diagnostics.Debug.WriteLine(str);
                }

                // トピックの更新
                if (OnTopic != null)
                {
                    MainWindow.Dispatcher.Invoke(OnTopic, new object[] { this, e.Channel, e.Who, e.NewTopic });
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[ERROR]e.Data.MessageArray is null");
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Irc_OnNames(object sender, NamesEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("--------------------(Irc_OnNames)");

            bool isBc = false;
            string who = "";
            string channelName = e.Channel;
            foreach (string user in e.UserList)
            {
                //System.Diagnostics.Debug.WriteLine(user); // オペレータ権限識別の先頭1文字が含まれる
                if (user.Length <= 0)
                {
                    continue;
                }

                isBc = false;
                switch (user[0])
                {
                    case '@':
                        // オペレータ
                        who = user.Substring(1);
                        break;
                    case '+':
                        /// ボイス
                        who = user.Substring(1);
                        break;
                    // RFC VIOLATION
                    // some IRC network do this and break our channel sync...
                    case '&':
                        who = user.Substring(1);
                        break;
                    case '%':
                        // Half Op
                        who = user.Substring(1);
                        break;
                    case '~':
                        // 配信者? チャンネルオペレータ?
                        isBc = true;
                        who = user.Substring(1);
                        break;
                    default:
                        who = user;
                        break;
                }
                if (isBc)
                {
                    ChannelInfoEx channelInfoEx = GetChannelInfoExByRealName(channelName);
                    if (channelInfoEx != null)
                    {
                        channelInfoEx.BcName = who;
                        System.Diagnostics.Debug.WriteLine("Channel Operator:" + who);
                    }
                    //break;
                }

                // ユーザー情報を登録
                ChannelUserInfoEx channelUserInfoEx = setChannelUserInfoEx(channelName, who);
                if (channelUserInfoEx != null)
                {
                    channelUserInfoEx.IsBc = isBc;
                }
            }

            if (OnUserCountChange != null)
            {
                MainWindow.Dispatcher.Invoke(OnUserCountChange, new object[] { this, channelName });
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Irc_OnWho(object sender, WhoEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("--------------------(Irc_OnWho)");
            System.Diagnostics.Debug.WriteLine(e.WhoInfo.Channel + " " + e.WhoInfo.Nick + " " + e.WhoInfo.Host);
            string who = e.WhoInfo.Nick;
            string host = e.WhoInfo.Host;
            string channelName = e.WhoInfo.Channel;
            if (channelName.Length > 0)
            {
                ChannelInfoEx channelInfoEx = GetChannelInfoExByRealName(channelName);
                if (channelInfoEx != null)
                {
                    ChannelUserInfoEx channelUserInfoEx = setChannelUserInfoEx(channelName, who);
                    channelUserInfoEx.Host = host;
                }
                else
                {
                    // 他のチャンネルのユーザーとして応答が返却された

                    // 参加中のチャンネルから同じニックネームのユーザーを探す
                    ChannelUserInfoEx[] channelUserInfoExs = _GetChannelUserInfoExsByNickName(who);
                    if (channelUserInfoExs != null && channelUserInfoExs.Length > 0)
                    {
                        foreach (ChannelUserInfoEx channelUserInfoEx in channelUserInfoExs)
                        {
                            channelUserInfoEx.Host = host;
                            System.Diagnostics.Debug.WriteLine("correct Who reply:" + who + " " + channelUserInfoEx.ChannelName);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void Irc_OnList(object sender, ListEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("--------------------(Irc_OnList)");
            System.Diagnostics.Debug.WriteLine(e.ListInfo.Channel + " user count:" + e.ListInfo.UserCount);
        }

        /// <summary>
        /// IRCチャンネル参加イベント
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Irc_OnJoin(object sender, JoinEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("--------------------(Irc_OnJoin)");
            System.Diagnostics.Debug.WriteLine(e.Channel + " " + e.Who);
            // add user
            string who = e.Who;
            string channelName = e.Channel;
            ChannelInfoEx channelInfoEx = null;

            if (IsMe(who))
            {
                channelInfoEx = GetChannelInfoExIncludingDerived(channelName);
                if (channelInfoEx != null)
                {
                    channelInfoEx.RealName = channelName;  // JOINできたチャンネル名を格納
                    channelInfoEx.IsJoined = true;
                    channelInfoEx.IsExisted = true;
                }
            }
            // 以下チャンネル名は、実際の名称で
            channelInfoEx = GetChannelInfoExByRealName(channelName);
            if (channelInfoEx == null)
            {
                return;
                //throw new NullReferenceException();
            }

            ChannelUser channelUser = Irc.GetChannelUser(channelName, who);
            string host = channelUser.Host;
            ChannelUserInfoEx channelUserInfoEx = setChannelUserInfoEx(channelName, who);
            channelUserInfoEx.Host = host;

            if (IsMe(who))
            {
                // ユーザー数変更イベント
                if (OnUserCountChange != null)
                {
                    MainWindow.Dispatcher.Invoke(OnUserCountChange, new object[] { this, channelName });
                }
            }

            /*
            // 前の席が取れた場合、後ろの席は譲る
            if (IsMe(who))
            {
                Thread t = new Thread(new ThreadStart(ArenaJoinedCheckThreadProc));
                t.Name = "ArenaJoinedCheckThreadProc";
                t.Start();
            }
            */
        }

        /*
        private void ArenaJoinedCheckThreadProc()
        {
            ChannelInfoEx joinedChannel = null;
            foreach (ChannelInfoEx channel in ChannelInfoExs)
            {
                if (joinedChannel == null)
                {
                    if (channel.IsExisted && channel.IsJoined)
                    {
                        joinedChannel = channel;
                    }
                }
                else
                {
                    if (channel.IsExisted && channel.IsJoined && !channel.IsParting)
                    {
                        Irc.RfcPart(channel.RealName);
                        channel.IsParting = true;
                    }
                }
            }
        }
        */

        /// <summary>
        /// IRCチャンネル退出イベント
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Irc_OnPart(object sender, PartEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("--------------------(Irc_OnPart)");
            System.Diagnostics.Debug.WriteLine(e.Channel + " " + e.Who);
            // remove user
            string channelName = e.Channel;
            string who = e.Who;
            Hashtable userHashtable = GetUserHashtable(channelName);

            if (who != null && userHashtable != null && userHashtable.Contains(who))
            {
                userHashtable.Remove(who);
            }

            // ユーザー数変更イベント
            if (OnUserCountChange != null)
            {
                MainWindow.Dispatcher.Invoke(OnUserCountChange, new object[] { this, channelName });
            }

            if (Irc.IsMe(who))
            {
                // 自分の場合
                ChannelInfoEx channelInfoEx = GetChannelInfoExByRealName(channelName);
                if (channelInfoEx != null)
                {
                    //channelInfoEx.RealName = "";
                    channelInfoEx.IsJoined = false;
                    channelInfoEx.IsParting = false;
                }
            }
            else
            {
                /*
                // 最後の一人になった場合は、退出する
                ChkAutoPart(channelName);
                */
            }
        }

        /// <summary>
        /// IRC キックイベント
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Irc_OnKick(object sender, KickEventArgs e)
        {
            // 注意: e.Whoはキックを実行したオペレータ e.Whomはキックされたユーザー
            System.Diagnostics.Debug.WriteLine("--------------------(Irc_OnKick)");
            System.Diagnostics.Debug.WriteLine(e.Channel + " " + e.Whom);
            string channelName = e.Channel;
            string who = e.Who; // キックを実行したオペレータ
            string whom = e.Whom;  // キックされたユーザー
            Hashtable userHashtable = GetUserHashtable(channelName);

            if (userHashtable != null && userHashtable.Contains(whom))
            {
                userHashtable.Remove(whom);
            }

            // 自分が誰かをキックした場合(Kickメソッドを使用)
            if (Irc.IsMe(who))
            {
                if (KickReplyEvent != null)
                {
                    // キック成功
                    KickErrorMessage = "";
                    // シグナルをONにする
                    KickReplyEvent.Set();
                }
            }

            // 自分がキックされた場合
            if (Irc.IsMe(whom))
            {
                ChannelInfoEx channelInfoEx = GetChannelInfoExByRealName(channelName);
                if (channelInfoEx != null)
                {
                    //channelInfoEx.RealName = "";
                    channelInfoEx.IsJoined = false;
                    channelInfoEx.IsParting = false;
                }
                MessageBox_ShowErrorAsync("キックされました:" + e.KickReason, "");
            }
        }

        /// <summary>
        /// IRCチャンネル参加禁止イベント
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Irc_OnBan(object sender, BanEventArgs e)
        {
            //注: e.WhoはBanを実行したユーザー e.HostMaskにバンされたホストの情報が格納されている
            System.Diagnostics.Debug.WriteLine("--------------------(Irc_OnBan)");
            System.Diagnostics.Debug.WriteLine(e.Channel + " " + e.Who + " " + e.Hostmask);

            string channelName = e.Channel;
            string who = e.Who; // Banを実行したオペレータ
            string hostMask = e.Hostmask;  // Banされたホストマスク

            // 自分が誰かをBanした場合
            if (Irc.IsMe(who))
            {
                if (ModeReplyEvent != null)
                {
                    // Banリストに追加成功
                    ModeErrorMessage = "";
                    // シグナルをONにする
                    ModeReplyEvent.Set();
                }
            }
        }

        /// <summary>
        /// IRCチャンネル参加禁止解除イベント
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Irc_OnUnban(object sender, UnbanEventArgs e)
        {
            //注: e.WhoはBanを実行したユーザー e.HostMaskにバンされたホストの情報が格納されている
            System.Diagnostics.Debug.WriteLine("--------------------(Irc_OnUnban)");
            System.Diagnostics.Debug.WriteLine(e.Channel + " " + e.Who + " " + e.Hostmask);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Irc_OnQuit(object sender, QuitEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("--------------------(Irc_OnQuit)");
            System.Diagnostics.Debug.WriteLine(e.Who);
            ////////////////////////////////////////////////
            // QUITは全チャンネル対象
            ////////////////////////////////////////////////
            string who = e.Who;

            // remove user
            foreach (ChannelInfoEx channelInfoEx in ChannelInfoExs)
            {
                Hashtable userHashtable = channelInfoEx.UserHashtable;
                if (userHashtable.Contains(who))
                {
                    userHashtable.Remove(who);
                }
            }

            // ユーザー数変更イベント
            if (OnUserCountChange != null)
            {
                foreach (ChannelInfoEx channelInfoEx in ChannelInfoExs)
                {
                    string channelName = channelInfoEx.RealName; // 現在のチャンネル名(立ち見等)を取得
                    MainWindow.Dispatcher.Invoke(OnUserCountChange, new object[] { this, channelName });
                }
            }
        }

        /// <summary>
        ///  自分以外のMODE変更通知
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Irc_OnChannelModeChange(object sender, IrcEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("--------------------(Irc_OnChannelModeChange)");
            //  :Twitchream-Bot!bot@Twitchream.tv MODE #ishikawanoriyuki_1 -iK
            string channelName = e.Data.RawMessageArray[2];
            string mode = e.Data.RawMessageArray[3].Substring(1);
            System.Diagnostics.Debug.WriteLine(channelName + " " + mode);
        }

        /// <summary>
        /// 自分のMODEの変更通知
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Irc_OnUserModeChange(object sender, IrcEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("--------------------(Irc_OnUserModeChange)");
            // :Twitchreamer-618888 MODE Twitchreamer-618888 :+wx
            string who = e.Data.RawMessageArray[2];
            string mode = e.Data.RawMessageArray[3].Substring(1);
            System.Diagnostics.Debug.WriteLine(who + " " + mode);
        }

        /// <summary>
        /// MODE変更通知
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Irc_OnModeChange(object sender, IrcEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("--------------------(Irc_OnModeChange)");
            if (OnModeChange != null)
            {
                MainWindow.Dispatcher.Invoke(OnModeChange, new object[] { this });
            }
        }

        /// <summary>
        /// IRCメッセージ受信イベント
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Irc_OnRawMessage(object sender, IrcEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("Received: " + e.Data.RawMessage);

            // IRCクライアントに実装されていないイベント処理実行
            bool doErrorProcess = true;  // エラー処理をするか
            doErrorProcess = Irc_OtherEventProcess(sender, e);
        }

        /// <summary>
        /// IRCクライアントに実装されていないイベント処理
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <returns></returns>
        private bool Irc_OtherEventProcess(object sender, IrcEventArgs e)
        {
            bool doErrorProcess = true; // 初期値:エラー処理をする
            /*
            // 配信者取得
            int replyCode = (int)e.Data.ReplyCode;
            if (replyCode == (int)AddedReplyCode.Bc)
            {
                // :chat17.Twitchream.tv 333 Twitchreamer-855064 #ishikawanoriyuki NORIYUKI-850901 1324431875
                string channelName = "";
                channelName = e.Data.RawMessageArray[3];
                ChannelInfoEx channelInfoEx = GetChannelInfoExByRealName(channelName);
                if (channelInfoEx != null)
                {
                    channelInfoEx.BcName = e.Data.RawMessageArray[4];
                }
            }
            */

            switch (e.Data.ReplyCode)
            {
                case ReplyCode.EndOfNames:
                    /*
                    string channelName;
                    // :chat20.Twitchream.tv 366 Twitchreamer-537964 #yellow-072_1 :End of /NAMES list.
                    channelName = e.Data.RawMessageArray[3];
                    // 自動退出チェック処理
                    ChkAutoPart(channelName);

                    doErrorProcess = false; // エラー処理をしない
                    */
                    break;

                case ReplyCode.ChannelModeIs:
                    // :chat18.Twitchream.tv 324 Twitchreamer-884671 #ishikawanoriyuki_1_1 +snticKCNTGlLZ 1480 #ishikawanoriyuki_1_1_1 10
                    if (OnModeChange != null)
                    {
                        // この時点では、IrcClientに情報が反映されていないので非同期でスレッドを実行
                        new Thread(new ParameterizedThreadStart(delegate (object param)
                        {
                            Thread.Sleep(50);
                            MainWindow.Dispatcher.Invoke(OnModeChange, new object[] { this });
                        })).Start(null);
                    }

                    doErrorProcess = false; // エラー処理をしない
                    break;

                case (ReplyCode)470: // チャット満員チャンネル転送メッセージ
                    // この処理をしてチャンネルBANされた。危険なので外す --> すぐに再参加しないで30秒後に再試行するように変更
                    // Banになってしまった場合でも、Banの電文が来る前にチャンネル転送電文が来るので何度も再参加しようとしてしまう不具合が発生した。Banされたかどうかをチェックする。
                    //  満員で立ち見に転送されたときの処理
                    //:sjc-chat03.Twitchream.tv 470 Twitchreamer-653210 [Link] #iboyuki has become full, so you are automatically being transferred to the linked channel #iboyuki_1
                    string fullChannelName = e.Data.RawMessageArray[4];
                    Thread t = new Thread(new ParameterizedThreadStart(Irc_chatFullThread));
                    t.Name = "Irc_chatFullThread";
                    t.Start(fullChannelName);
                    doErrorProcess = false; // エラー処理をしない
                    break;
            }

            return doErrorProcess;
        }

        /// <summary>
        /// チャンネル満員時の処理スレッド
        /// </summary>
        /// <param name="parameter"></param>
        private void Irc_chatFullThread(object parameter)
        {
            string fullChannelName = parameter as string;
            if (fullChannelName == null)
            {
                return;
            }

            // 30秒待機する (Ban対策)
            Thread.Sleep(30 * 1000);

            ChannelInfoEx[] notJoinedChannels = GetChannelInfoExsByJoined(false);
            // 30秒待ったのでBanされているかのフラグはセットされているはず。
            if (!IsBannedFromChannel)
            {
                foreach (ChannelInfoEx notJoinedchannelInfoEx in notJoinedChannels)
                {
                    if (notJoinedchannelInfoEx.IsExisted && notJoinedchannelInfoEx.Name == fullChannelName)
                    {
                        Irc.RfcJoin(notJoinedchannelInfoEx.Name);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// 電文送信イベント
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void Irc_OnWriteLine(object sender, WriteLineEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("send: " + e.Line);

        }

        //////////////////////////////////////////////////////////////
        // チャンネル/ユーザー
        //////////////////////////////////////////////////////////////
        /// <summary>
        /// チャンネル名リストを設定する
        /// </summary>
        /// <param name="names">チャンネル名配列</param>
        private void SetChannelNames(string[] names)
        {
            if (names == null)
            {
                return;
            }

            ChannelInfoExs = new ChannelInfoEx[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                System.Diagnostics.Debug.WriteLine(names[i]);
                ChannelInfoEx channelInfoEx = new ChannelInfoEx();
                channelInfoEx.Name = names[i];
                channelInfoEx.RealName = "";
                ChannelInfoExs[i] = channelInfoEx;
            }
        }

        /// <summary>
        /// チャンネルリストを破棄する
        /// </summary>
        private void ClearChannelNames()
        {
            // 現在のチャンネルリストを破棄する
            ChannelInfoExs = null;
            IsBannedFromChannel = false;
        }

        /*
        /// <summary>
        /// ニックネームを自動生成する
        /// NOTE: コンストラクタでニックネームを指定しない場合に実行
        /// </summary>
        /// <param name="keta">桁数(初期値:6)</param>
        private void GenerateNickName(int keta = 6)
        {
            int num = MyUtil.GetRandIntWithKeta(keta - 1, keta);

            _MyNickName = UserNamePrefix + num.ToString();
            _RealName = _MyNickName;
            _UserName = _MyNickName;
            System.Diagnostics.Debug.WriteLine(_MyNickName);
        }
        */

        /// <summary>
        /// 色比較
        /// </summary>
        private class ColorComparer : IComparer<Color>
        {
            public int Compare(Color x, Color y)
            {
                Color colorX = (Color)x;
                Color colorY = (Color)y;
                return (colorX.R + colorX.G + colorX.B - colorY.R - colorY.G - colorY.B);
                //return (int)(colorX.GetBrightness() - colorY.GetBrightness());
            }

        }

        /// <summary>
        /// 色一覧を作成する
        /// </summary>
        private void GenerateColors()
        {
            List<Color> results = new List<Color>();
            foreach (PropertyInfo prop in typeof(Color).GetProperties(BindingFlags.Public | BindingFlags.Static))
            {
                Color cl = (Color)prop.GetValue(null, null);
                if (cl.Name != "Transparent" && cl.Name != "Red" && cl.Name != "Blue" && cl.Name != "White" && cl.Name != "Black")
                {
                    string name = cl.Name;
                    int argb = cl.ToArgb();
                    cl = Color.FromArgb(argb);
                    //System.Diagnostics.Debug.WriteLine(String.Format("{0:000}:{1}={2}", ++count, ColorTranslator.ToHtml(cl), name));
                    results.Add(cl);
                }
            }
            IComparer<Color> comparer = new ColorComparer();
            results.Sort(comparer);
            this.Colors = results.ToArray();
        }

        /// <summary>
        /// ChannelUserInfoExにチャンネル情報を設定する
        /// </summary>
        /// <param name="channelName">チャンネル名(アリーナと立ち見は区別する)</param>
        /// <param name="nickName">ニックネーム</param>
        /// <returns>ChannelUserInfoEx</returns>
        private ChannelUserInfoEx setChannelUserInfoEx(string channelName, string nickName)
        {
            ChannelUserInfoEx channelUserInfoEx;
            Hashtable userHashtable = GetUserHashtable(channelName);
            if (userHashtable == null)
            {
                //throw new NullReferenceException();
                return null;
            }

            if (userHashtable.Contains(nickName))
            {
                channelUserInfoEx = (ChannelUserInfoEx)userHashtable[nickName];
            }
            else
            {
                channelUserInfoEx = new ChannelUserInfoEx();
                userHashtable[nickName] = channelUserInfoEx;
            }
            channelUserInfoEx.NickName = nickName;
            channelUserInfoEx.ChannelName = channelName;
            return channelUserInfoEx;
        }

        private ChannelUserInfoEx[] _GetChannelUserInfoExsByNickName(string nickName)
        {
            List<ChannelUserInfoEx> users = new List<ChannelUserInfoEx>();

            if (ChannelInfoExs != null)
            {
                foreach (ChannelInfoEx channelInfoEx in ChannelInfoExs)
                {
                    if (channelInfoEx.UserHashtable.ContainsKey(nickName))
                    {
                        ChannelUserInfoEx user = (ChannelUserInfoEx)channelInfoEx.UserHashtable[nickName];
                        users.Add(user);
                    }
                }
            }
            return users.ToArray();
        }

        /// <summary>
        /// ユーザーハッシュテーブルを取得する
        /// </summary>
        /// <param name="channelName">チャンネル名(アリーナと立ち見は区別する)</param>
        /// <returns>ユーザーハッシュテーブル</returns>
        private Hashtable GetUserHashtable(string channelName)
        {
            Hashtable userHashtable = null;

            ChannelInfoEx channelInfoEx = GetChannelInfoExByRealName(channelName);
            if (channelInfoEx != null)
            {
                userHashtable = channelInfoEx.UserHashtable;
            }
            else
            {
            }

            return userHashtable;
        }

        /// <summary>
        /// チャンネルの色を取得
        /// </summary>
        /// <param name="channelName">チャンネル名</param>
        /// <returns>チャンネルの色</returns>
        private Color getChannelColor(string channelName)
        {
            Color color = Color.Black; // 文字色

            channelName = channelName.ToLower();

            if (ChannelInfoExs != null)
            {
                foreach (ChannelInfoEx channelInfoEx in ChannelInfoExs)
                {
                    if (channelInfoEx.RealName == channelName)
                    {
                        if (channelInfoEx.Color == Color.Black)
                        {
                            //int randNum = MyUtil.GetExactRandInt(Colors.Length / 2, Colors.Length * 3 / 4);
                            int randNum = MyUtil.GetExactRandInt(Colors.Length / 4, Colors.Length / 2);
                            //int randNum = MyUtil.GetExactRandInt(Colors.Length / 4);
                            channelInfoEx.Color = Colors[randNum];
                        }
                        color = channelInfoEx.Color;
                        break;
                    }
                }
            }
            return color;
        }

        /// <summary>
        /// ユーザーの色を取得
        /// </summary>
        /// <param name="channelName">チャンネル名</param>
        /// <param name="nickName">ニックネーム</param>
        /// <returns>文字色</returns>
        private Color getUserColor(string channelName, string nickName)
        {
            Color color = Color.Black; // 文字色
            ChannelUserInfoEx channelUserInfoEx = setChannelUserInfoEx(channelName, nickName);
            if (channelUserInfoEx.Color == Color.Black)
            {
                int randNum = MyUtil.GetExactRandInt(Colors.Length / 4, Colors.Length / 2);
                channelUserInfoEx.Color = Colors[randNum];
            }
            color = channelUserInfoEx.Color;
            return color;
        }

        //////////////////////////////////////////////////////////////
        // Helper関数
        //////////////////////////////////////////////////////////////
        /// <summary>
        /// メッセージウィンドウ(INFO)を表示する
        /// </summary>
        /// <param name="">チャンネル名</param>
        private void MessageBox_ShowAsync(string text, string caption = "")
        {
            new Thread(new ThreadStart(delegate ()
            {
                MessageBox.Show(text, caption, MessageBoxButton.OK);
            })).Start();
        }

        /// <summary>
        /// メッセージウィンドウ(エラー)を表示する
        /// </summary>
        /// <param name="">チャンネル名</param>
        private void MessageBox_ShowErrorAsync(string text, string caption)
        {
            new Thread(new ThreadStart(delegate ()
            {
                MessageBox.Show(text, caption, MessageBoxButton.OK, MessageBoxImage.Error);
            })).Start();
        }


        /// <summary>
        /// チャンネル取得
        /// </summary>
        /// <param name="channelName">チャンネル名</param>
        /// <returns>ChannelUser</returns>
        public Channel GetChannel(string channelName)
        {
            if (Irc == null)
            {
                return null;
            }
            return Irc.GetChannel(channelName);
        }

        /// <summary>
        /// チャンネルユーザー取得
        /// </summary>
        /// <param name="channelName">チャンネル名</param>
        /// <param name="nickName">ニックネーム</param>
        /// <returns>ChannelUser</returns>
        public ChannelUser GetChannelUser(string channelName, string nickName)
        {
            if (Irc == null)
            {
                return null;
            }
            return Irc.GetChannelUser(channelName, nickName);
        }

        public ChannelUserInfoEx[] GetChannelUserInfoExsByNickName(string nickName)
        {
            return _GetChannelUserInfoExsByNickName(nickName);
        }

        /// <summary>
        /// 自分か?
        /// </summary>
        /// <param name="nickName">ニックネーム</param>
        /// <returns>true/false</returns>
        public bool IsMe(string nickName)
        {
            return NickName == nickName;
        }

        /// <summary>
        /// オペレーターか?
        /// </summary>
        /// <param name="channelName">チャンネル名</param>
        /// <param name="nickName">ニックネーム</param>
        /// <returns>true/false</returns>
        public bool IsOp(string channelName, string nickName)
        {
            if (Irc == null)
            {
                return false;
            }

            ChannelUser channelUser = Irc.GetChannelUser(channelName, nickName);
            if (channelUser == null)
            {
                return false;
            }
            return channelUser.IsOp;
        }

        /// <summary>
        /// 配信者か?
        /// </summary>
        /// <param name="channelName">チャンネル名</param>
        /// <param name="nickName">ニックネーム</param>
        /// <returns>true/false</returns>
        public bool IsBc(string channelName, string nickName)
        {
            ChannelInfoEx channelInfoEx = GetChannelInfoExByRealName(channelName);
            bool isBc = false;
            if (channelInfoEx != null && channelInfoEx.BcName == nickName)
            {
                isBc = true;
            }
            return isBc;
        }

        /// <summary>
        /// 発言権ありか?
        /// </summary>
        /// <param name="channelName">チャンネル名</param>
        /// <param name="nickName">ニックネーム</param>
        /// <returns>true/false</returns>
        public bool IsVoice(string channelName, string nickName)
        {
            if (Irc == null)
            {
                return false;
            }

            ChannelUser channelUser = Irc.GetChannelUser(channelName, nickName);
            if (channelUser == null)
            {
                return false;
            }
            return channelUser.IsVoice;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public ChannelInfoEx[] GetChannelInfoExs()
        {
            return ChannelInfoExs;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public ChannelInfoEx[] GetChannelInfoExsByJoined(bool isJoined)
        {
            List<ChannelInfoEx> hitInfos = new List<ChannelInfoEx>();

            if (ChannelInfoExs != null)
            {
                // まず指定チャンネルと同じチャンネル名で検索
                foreach (ChannelInfoEx channelInfoEx in ChannelInfoExs)
                {
                    // 参加中フラグが指定されたものと同じチャンネルかチェック
                    if (channelInfoEx.IsJoined == isJoined)
                    {
                        hitInfos.Add(channelInfoEx);
                    }
                }
            }

            return hitInfos.ToArray();
        }

        /// <summary>
        /// チャンネル追加情報取得
        /// </summary>
        /// <param name="channelName">チャンネル名(本当の名称で)</param>
        /// <returns>CHannelInfoEx</returns>
        private ChannelInfoEx GetChannelInfoExIncludingDerived(string channelName)
        {
            ChannelInfoEx hitInfo = null;

            channelName = channelName.ToLower(); // 小文字に変換してから比較する

            if (ChannelInfoExs != null)
            {
                // まず指定チャンネルと同じチャンネル名で検索
                foreach (ChannelInfoEx channelInfoEx in ChannelInfoExs)
                {
                    if (channelInfoEx.Name == channelName)
                    {
                        hitInfo = channelInfoEx;
                        break;
                    }
                }

                // 指定チャンネルでJOINできていない場合、派生チャンネルを検索
                if (hitInfo == null)
                {
                    foreach (ChannelInfoEx channelInfoEx in ChannelInfoExs)
                    {
                        if (channelInfoEx.IsSameOrDerivedName(channelName))
                        {
                            hitInfo = channelInfoEx;
                            break;
                        }
                    }
                }
            }
            if (hitInfo == null)
            {
                System.Diagnostics.Debug.WriteLine("[ERROR]GetChannelInfoExIncludingDerived unknown channelName:" + channelName);
                //MessageBox.Show("[ERROR]GetChannelInfoExIncludingDerived unknown channelName:" + channelName);
            }
            return hitInfo;
        }

        /// <summary>
        /// チャンネル追加情報取得
        /// </summary>
        /// <param name="channelName">チャンネル名(アリーナ、立ち見を区別する)</param>
        /// <returns>CHannelInfoEx</returns>
        public ChannelInfoEx GetChannelInfoExByRealName(string channelName)
        {
            ChannelInfoEx hitInfo = null;

            channelName = channelName.ToLower(); // 小文字に変換してから比較する
            if (ChannelInfoExs != null)
            {
                foreach (ChannelInfoEx channelInfoEx in ChannelInfoExs)
                {
                    if (channelInfoEx.RealName == channelName)
                    {
                        hitInfo = channelInfoEx;
                        break;
                    }
                }
            }
            if (hitInfo == null)
            {
                System.Diagnostics.Debug.WriteLine("[ERROR]GetChannelInfoExByRealName unknown channelName:" + channelName);
                //MessageBox.Show("[ERROR]GetChannelInfoExByRealName unknown channelName:" + channelName);
            }
            return hitInfo;
        }

        /// <summary>
        /// チャンネルの色を取得
        /// </summary>
        /// <param name="channelName">チャンネル名</param>
        /// <returns>文字色</returns>
        public Color GetChannelColor(string channelName)
        {
            return getChannelColor(channelName);
        }

        /// <summary>
        /// ユーザーの色を取得
        /// </summary>
        /// <param name="channelName">チャンネル名</param>
        /// <param name="nickName">ニックネーム</param>
        /// <returns>ユーザーの色</returns>
        public Color GetUserColor(string channelName, string nickName)
        {
            return getUserColor(channelName, nickName);
        }

        /// <summary>
        /// オペレーター一覧取得
        /// </summary>
        /// <param name="channelName">チャンネル名</param>
        /// <returns>オペレーター一覧</returns>
        public ChannelUser[] GetOps(string channelName)
        {
            if (Irc == null)
            {
                return null;
            }

            //List<string> opNames = null;
            List<ChannelUser> ops = null;

            channelName = channelName.ToLower(); // 小文字に変換

            Channel channel = Irc.GetChannel(channelName);
            if (channel != null)
            {
                Hashtable workOps = channel.Ops;  // ハッシュテーブルで管理されている(key: ニックネーム value: ChannelUser)
                //opNames = workOps.Keys as List<string>;
                ops = workOps.Values as List<ChannelUser>;
            }
            else
            {
                ops = null;
            }

            return ops.ToArray();
        }

        /// <summary>
        /// ユーザー一覧取得
        /// </summary>
        /// <param name="channelName">チャンネル名</param>
        /// <returns>ユーザー一覧</returns>
        public ChannelUser[] GetChannelUsers(string channelName)
        {
            if (Irc == null)
            {
                return null;
            }

            List<ChannelUser> channelUsers = null;

            channelName = channelName.ToLower(); // 小文字に変換

            Channel channel = Irc.GetChannel(channelName);
            if (channel != null)
            {
                Hashtable users = channel.Users;  // ハッシュテーブルで管理されている(key: ニックネーム value: ChannelUser)
                //nickNameList = users.Keys as List<string>;
                if (users != null)
                {
                    channelUsers = users.Values.Cast<ChannelUser>().ToList<ChannelUser>();
                }
            }
            else
            {
            }

            return (channelUsers != null) ? channelUsers.ToArray() : null;
        }

        /// <summary>
        /// ユーザー数の取得
        /// </summary>
        /// <param name="channelName">チャンネル名</param>
        /// <returns>ユーザー数</returns>
        public int GetChannelUserCount(string channelName)
        {
            if (Irc == null)
            {
                return 0;
            }

            int cnt = 0;

            channelName = channelName.ToLower(); // 小文字に変換

            Channel channel = Irc.GetChannel(channelName);
            if (channel != null)
            {
                Hashtable users = channel.Users;  // ハッシュテーブルで管理されている(key: ニックネーム value: ChannelUser)
                cnt = users.Count;
            }
            else
            {
                cnt = 0;
            }

            return cnt;
        }

        /// <summary>
        /// アクティブ数を取得
        /// </summary>
        /// <param name="channelName">チャンネル名</param>
        /// <returns></returns>
        public int GetActiveCount(string channelName)
        {
            int activeCnt = 0;

            channelName = channelName.ToLower(); // 小文字に変換

            ChannelInfoEx channelInfoEx = GetChannelInfoExByRealName(channelName);
            // アクティブ数を計算
            if (channelInfoEx != null)
            {
                activeCnt = channelInfoEx.GetActiveUserCount();
            }

            return activeCnt;
        }

        /// <summary>
        /// 自分のユーザーモードを取得
        /// </summary>
        /// <returns>ユーザーモード文字列</returns>
        public string GetUserMode()
        {
            if (Irc == null)
            {
                return "";
            }
            return Irc.Usermode;
        }

        /// <summary>
        /// チャンネルモードを取得
        /// </summary>
        /// <param name="channelName">チャンネル名</param>
        /// <returns>チャンネルモード文字列</returns>
        public string GetChannelMode(string channelName)
        {
            if (Irc == null)
            {
                return "";
            }
            string channelMode = "";

            channelName = channelName.ToLower(); // 小文字に変換

            Channel channel = Irc.GetChannel(channelName);
            if (channel != null)
            {
                channelMode = channel.Mode;
            }
            return channelMode;
        }

        /// <summary>
        /// Ban一覧取得
        /// </summary>
        /// <param name="channelName"></param>
        /// <returns></returns>
        public string[] GetBans(string channelName)
        {
            if (Irc == null)
            {
                return null;
            }

            //IList<BanInfo> banInfos;
            //banInfos = Irc.GetBanList(channelName);

            string[] bans = null;
            Channel channel = Irc.GetChannel(channelName);
            if (channel != null)
            {
                bans = new string[channel.Bans.Count];

                int i = 0;
                foreach (string banMask in channel.Bans)
                {
                    bans[i] = banMask;
                    i++;
                }
            }

            return bans;
        }

        /// <summary>
        /// チャンネルに参加する
        /// </summary>
        /// <param name="channelName">チャンネル名</param>
        public void JoinChannel(string channelName)
        {
            if (Irc == null)
            {
                return;
            }
            Irc.RfcJoin(channelName);
        }

        /// <summary>
        /// プライベートメッセージを送信
        /// </summary>
        /// <param name="channelName">チャンネル名</param>
        /// <param name="message">メッセージ</param>
        public void SendPrivMsg(string channelName, string message)
        {
            if (Irc == null)
            {
                return;
            }

            // PRIVMSG #channel :message
            Irc.RfcPrivmsg(channelName, message);

            // 送信者（呼び出し側)にダミーメッセージイベントを送る
            dummyChannelMessageToSender(channelName, message);
        }

        private void dummyChannelMessageToSender(string channelName, string message)
        {
            // 自分の送信したメッセージの追加
            if (OnChannelMessage != null)
            {
                ChannelUser channelUser = Irc.GetChannelUser(channelName, this.NickName);
                string host = "";
                if (channelUser != null && channelUser.Host != null)
                {
                    host = channelUser.Host;
                }
                // GUI側へ通知
                MainWindow.Dispatcher.Invoke(OnChannelMessage, new object[] { this, channelName, this.NickName, host, message });
            }
        }

        public void SendQueryMessage(string to, string message)
        {
            if (Irc == null)
            {
                return;
            }

            //Irc.SendMessage(SendType.Message, to, message);
            // PRIVMSG to :message
            Irc.RfcPrivmsg(to, message);
        }

        /// <summary>
        /// キックする
        /// </summary>
        /// <param name="channelName">[IN]チャンネル名</param>
        /// <param name="nickName">[IN]ニックネーム</param>
        /// <param name="reason">[IN]理由</param>
        /// <param name="outErrorMessage">[OUT]エラーメッセージ</param>
        /// <returns></returns>
        public bool Kick(string channelName, string nickName, string reason, out string outErrorMessage)
        {
            if (Irc == null)
            {
                outErrorMessage = "サーバーに接続されていません";
                return false;
            }
            bool success = false;

            // 受信イベント設定
            KickReplyEvent = new AutoResetEvent(false);
            KickErrorMessage = "";

            if (reason.Length > 0)
            {
                // KICK channel nick :reason
                Irc.RfcKick(channelName, nickName, reason);
            }
            else
            {
                // KICK channel nick
                Irc.RfcKick(channelName, nickName);
            }

            bool ret = KickReplyEvent.WaitOne(15000); // 15秒待つ
            if (ret)
            {
                if (KickErrorMessage.Length == 0)
                {
                    success = true;
                }
                else
                {
                    success = false;
                }
            }
            else
            {
                success = false;
                KickErrorMessage = "サーバーからの応答がありません";
            }
            // エラーを格納
            outErrorMessage = KickErrorMessage;

            System.Diagnostics.Debug.WriteLine("Kick success=" + success + "/errorMessage" + KickErrorMessage + "(channelName=" + channelName + "/nickName=" + nickName + "/reason=" + reason + ")");

            // 受信イベント破棄
            KickReplyEvent = null;
            KickErrorMessage = "";

            return success;
        }


        /// <summary>
        /// Banマスクに変換する
        /// </summary>
        /// <param name="nickName">ニックネーム(ワイルドカード*,?他使用可)</param>
        /// <param name="userName">ユーザー名(ワイルドカード*,?他使用可)</param>
        /// <param name="host">ホスト、ドメイン(ワイルドカード*,?他使用可)</param>
        /// <returns></returns>
        public static string ToBanMask(string nickName, string userName, string host)
        {
            ////////////////////////
            // nick!user@host
            ////////////////////////
            // 
            // *!*@dev-298607F4.stm.mesh.ad.jp
            // Twitchreamer-????!*@* 
            // ~q:*!*@*.pool.e-mobile.ne.jp
            // ~q:*!*@*.au-net.ne.jp
            // *!*@*.IP
            // *n*0*r*!*@*
            string mask;

            mask = nickName + "!" + userName + "@" + host;
            return mask;
        }

        /// <summary>
        /// チャンネル参加禁止リストに追加する
        /// </summary>
        /// <param name="channelName">チャンネル名</param>
        /// <param name="banMask">Banマスク</param>
        /// <param name="outErrorMessage">[OUT]エラーメッセージ</param>
        /// <returns></returns>
        public bool Ban(string channelName, string banMask, out string outErrorMessage)
        {
            bool success;
            success = doBan(true, channelName, banMask, out outErrorMessage);
            return success;
        }

        /// <summary>
        /// チャンネル参加禁止リストから削除する
        /// </summary>
        /// <param name="channelName">チャンネル名</param>
        /// <param name="banMask">Banマスク</param>
        /// <param name="outErrorMessage">[OUT]エラーメッセージ</param>
        /// <returns></returns>
        public bool Unban(string channelName, string banMask, out string outErrorMessage)
        {
            bool success;
            success = doBan(false, channelName, banMask, out outErrorMessage);
            return success;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="banFlag"></param>
        /// <param name="channelName"></param>
        /// <param name="banMask"></param>
        /// <param name="outErrorMessage"></param>
        /// <returns></returns>
        private bool doBan(bool banFlag, string channelName, string banMask, out string outErrorMessage)
        {
            if (Irc == null)
            {
                outErrorMessage = "サーバーに接続されていません";
                return false;
            }
            if (ModeReplyEvent != null)
            {
                outErrorMessage = "モード変更を実行中です";
                return false;
            }

            bool success = false;

            // 受信イベント設定
            ModeReplyEvent = new AutoResetEvent(false);
            ModeErrorMessage = "";

            if (banFlag)
            {
                // MODE channel +b banMask 送信
                Irc.Ban(channelName, banMask);
            }
            else
            {
                // MODE channel -b banMask 送信
                Irc.Unban(channelName, banMask);
            }

            bool ret = ModeReplyEvent.WaitOne(15000); // 15秒待つ
            if (ret)
            {
                if (ModeErrorMessage.Length == 0)
                {
                    success = true;
                }
                else
                {
                    success = false;
                }
            }
            else
            {
                success = false;
                ModeErrorMessage = "サーバーからの応答がありません";
            }
            // エラーを格納
            outErrorMessage = ModeErrorMessage;


            System.Diagnostics.Debug.WriteLine((banFlag ? "Ban" : "Unban") + "success=" + success + "/errorMessage" + ModeErrorMessage + "(channelName=" + channelName + "/banMask=" + banMask + ")");

            // 受信イベント破棄
            ModeReplyEvent = null;
            ModeErrorMessage = "";

            return success;
        }
    }
}
