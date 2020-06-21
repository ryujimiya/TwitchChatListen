using MyUtilLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Meebey.SmartIrc4net;
using System.Collections.ObjectModel;
using System.IO;

namespace TwitchChatListen
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        //////////////////////////////////////////////////////////////
        // デリゲート宣言
        //////////////////////////////////////////////////////////////

        //////////////////////////////////////////////////////////////
        // 定数
        //////////////////////////////////////////////////////////////
        const string ChatChannelNamePrefix = "#";
        const int MaxOldCommentLodingCount = 100;
        const string CommentLogFileName = @"comment.txt";

        //////////////////////////////////////////////////////////////
        // 型
        //////////////////////////////////////////////////////////////

        //////////////////////////////////////////////////////////////
        // 変数
        //////////////////////////////////////////////////////////////
        /// <summary>
        /// 対象チャンネル名
        /// </summary>
        private string ChannelName = "";
        /// <summary>
        /// アカウント名
        /// </summary>
        private string AccountName = "";
        /// <summary>
        /// チャットのパスワード
        /// </summary>
        private string ChatPassword = "";

        /// <summary>
        /// タイトルのベース
        /// </summary>
        private string TitleBase = "";
        /// <summary>
        /// 棒読みちゃん
        /// </summary>
        private MyUtilLib.BouyomiChan BouyomiChan = new MyUtilLib.BouyomiChan();
        /// <summary>
        /// Twitchチャットクライアント
        /// </summary>
        private TwitchChatClient TwitchChatClient;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();

            // GUI初期処理
            TitleBase = this.Title + " " + MyUtil.GetFileVersion();
            this.Title = TitleBase;
        }

        /// <summary>
        /// ウィンドウが開かれた
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 過去のログをロードする
            LoadLog();

            // 設定画面を開く
            SettingsBtn_Click(null, null);
        }

        /// <summary>
        /// ウィンドウが閉じられようとしている
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            TwitchChatClientStop();
            if (TwitchChatClient != null)
            {
                // Twitchチャットクライアント終了
                TwitchChatClient.Dispose();
                TwitchChatClient.OnStop -= TwitchChatClient_OnStop;
                TwitchChatClient.OnBusyChange -= TwitchChatClient_OnBusyChange;
                TwitchChatClient.OnChannelMessage -= TwitchChatClient_OnChannelMessage;
                TwitchChatClient.OnTopic -= TwitchChatClient_OnTopic;
                TwitchChatClient.OnUserCountChange -= TwitchChatClient_OnUserCountChange;
                TwitchChatClient.OnModeChange -= TwitchChatClient_OnModeChange;
                System.Diagnostics.Debug.WriteLine("TwitchChatClient Finalize done.");
            }
            BouyomiChan.ClearText();
            BouyomiChan.Dispose();
        }

        /// <summary>
        /// ウィンドウのサイズが変更された
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // ウィンドウの高さ Note:最大化のときthis.Heightだと値がセットされない
            double height = this.RenderSize.Height;
            // データグリッドの高さ変更
            StackPanel1.Height = height - SystemParameters.CaptionHeight;
            DataGrid.Height = StackPanel1.Height - WrapPanel1.Height - WrapPanel2.Height;
        }

        /// <summary>
        /// コメントログを記録する
        /// </summary>
        /// <param name="userName"></param>
        /// <param name="commentText"></param>
        private void WriteLog(string userName, string commentText)
        {
            string logText = userName + "\t" + commentText;
            System.IO.StreamWriter sw = new System.IO.StreamWriter(
                CommentLogFileName,
                true, // append : true
                System.Text.Encoding.GetEncoding("UTF-8"));
            sw.WriteLine(logText);
            sw.Close();
        }

        private void LoadLog()
        {
            if (!File.Exists(CommentLogFileName))
            {
                return;
            }
            IList<string> lines = new List<string>();
            using(var sr = new System.IO.StreamReader(CommentLogFileName))
            {
                while (!sr.EndOfStream)
                {
                    string line = sr.ReadLine();
                    lines.Add(line);
                }
            }

            int startIndex = 0;
            if (lines.Count > MaxOldCommentLodingCount)
            {
                startIndex = lines.Count - MaxOldCommentLodingCount;
            }
            for (int iLine = startIndex; iLine < lines.Count; iLine++)
            {
                string line = lines[iLine];
                string[] tokens = line.Split('\t');
                if (tokens.Length != 2)
                {
                    continue;
                }

                string userName = tokens[0];
                string commentText = tokens[1];

                // コメントの追加
                UiCommentData uiCommentData = new UiCommentData();
                uiCommentData.UserThumbUrl = "";
                uiCommentData.UserName = userName;
                uiCommentData.CommentStr = commentText;

                ViewModel viewModel = this.DataContext as ViewModel;
                ObservableCollection<UiCommentData> uiCommentDatas = viewModel.UiCommentDataCollection;
                uiCommentDatas.Add(uiCommentData);
            }
            // データグリッドを自動スクロール
            DataGridScrollToEnd();
        }

        /// <summary>
        /// データグリッドを自動スクロール
        /// </summary>
        private void DataGridScrollToEnd()
        {
            if (DataGrid.Items.Count > 0)
            {
                var border = VisualTreeHelper.GetChild(DataGrid, 0) as Decorator;
                if (border != null)
                {
                    var scroll = border.Child as ScrollViewer;
                    if (scroll != null) scroll.ScrollToEnd();
                }
            }
        }

        /// <summary>
        /// Twitchボタンがクリックされた
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TwitchBtn_Click(object sender, RoutedEventArgs e)
        {
            string liveUrl = "https://www.twitch.tv/" + ChannelName;
            // ブラウザで開く
            System.Diagnostics.Process.Start(liveUrl);
        }

        /// <summary>
        /// (チャンネル)更新ボタンがクリックされた
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UpdateBtn_Click(object sender, RoutedEventArgs e)
        {
            UpdateChannelName();
        }

        /// <summary>
        /// チャンネル名の更新
        /// </summary>
        private void UpdateChannelName()
        {
            // いま稼働しているクライアントを停止する
            TwitchChatClientStop();

            BouyomiChan.ClearText();

            // 新しい設定を取得
            string nickName = AccountName;
            string password = ChatPassword;
            string channelName = ChannelNameTextBox.Text;
            System.Diagnostics.Debug.Assert(password != "" && nickName != "");
            TwitchChatClientStart(nickName, password, channelName);
        }

        /// <summary>
        /// チャンネルテキストボックスのキーアップイベントハンドラ
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ChannelNameTextBox_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                UpdateChannelName();
            }
        }

        /// <summary>
        /// 送信ボタンがクリックされた
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SendBtn_Click(object sender, RoutedEventArgs e)
        {
            SendComment();
        }

        /// <summary>
        /// コメントテキストボックスのキーアップイベントハンドラ
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CommentTextBox_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SendComment();
            }
        }


        /// <summary>
        /// トピックを設定する
        /// </summary>
        /// <param name="channelName">チャンネル名</param>
        /// <param name="topic">トピック</param>
        private void SetTopic(string channelName, string topic)
        {
            string text = "";
            if (topic.IndexOf("<br>") >= 0)
            {
                topic = Regex.Replace(topic, "<br>", System.Environment.NewLine);
            }
            if (channelName.Length > 0)
            {

                text = "【" + channelName + "】 " + topic;
            }
            else
            {
                text = topic;
            }
            /*
            richTextBox1.Clear();
            richTextBox1.AppendText(text);
            */
        }

        /// <summary>
        /// ステータスストリップのユーザー数テキストを設定する
        /// </summary>
        private void UpdateUserCntText()
        {
            string channelName;
            int userCnt = 0;
            ChannelInfoEx[] channelInfoExs = TwitchChatClient.GetChannelInfoExsByJoined(true);

            if (channelInfoExs != null && channelInfoExs.Length > 0)
            {
                /*
                metroLabelStatus5.Text = "";
                */
                foreach (ChannelInfoEx channelInfoEx in channelInfoExs)
                {
                    channelName = channelInfoEx.RealName; // 実際のチャンネル名
                    userCnt += TwitchChatClient.GetChannelUserCount(channelName);
                }

            }

            /*
            metroLabelStatus1.Text = "【ユーザー数】" + string.Format("{0, 6}", userCnt) + "人";
            */
        }

        /// <summary>
        /// ステータスストリップのモードテキストを設定する
        /// </summary>
        private void UpdateModeText()
        {
            string channelName;
            string userMode = "";
            string channelMode = "";

            ChannelInfoEx[] channelInfoExs = TwitchChatClient.GetChannelInfoExsByJoined(true);
            userMode = TwitchChatClient.GetUserMode();
            if (channelInfoExs != null && channelInfoExs.Length > 0)
            {
                // チャンネルモードは先頭のチャンネル(アリーナ)のみ表示
                channelName = channelInfoExs[0].RealName;
                channelMode = TwitchChatClient.GetChannelMode(channelName);
            }

            /*
            metroLabelStatus3.Text = "【ユーザーモード】" + userMode;
            metroLabelStatus4.Text = "【チャンネルモード】" + channelMode;
            */
        }

        /// <summary>
        /// ステータスストリップのアクティブ数テキストを設定する
        /// </summary>
        private void UpdateActiveCntText()
        {
            int activeCnt = 0;
            string channelName =  ChatChannelNamePrefix + ChannelName;
            ChannelInfoEx[] channelInfoExs = TwitchChatClient.GetChannelInfoExsByJoined(true);

            if (channelInfoExs != null && channelInfoExs.Length > 0)
            {
                foreach (ChannelInfoEx channelInfoEx in channelInfoExs)
                {
                    channelName = channelInfoEx.RealName; // 実際のチャンネル名

                    activeCnt += TwitchChatClient.GetActiveCount(channelName);
                }

            }

            /*
            metroLabelStatus2.Text = "【アクティブ数】" + string.Format("{0, 6}", activeCnt) + "人";
            */
        }


        ///////////////////////////////////////////////////////////////
        // Twitchチャットクライアント起動/停止
        ///////////////////////////////////////////////////////////////
        /// <summary>
        /// Twitchチャットを開始する
        /// </summary>
        private void TwitchChatClientStart(string nickName, string password, string channelName)
        {
            ChannelName = channelName;

            string chatChannelName = ChatChannelNamePrefix + channelName;
            TwitchChatClient = new TwitchChatClient(this, nickName, password);
            TwitchChatClient.OnStop += TwitchChatClient_OnStop;
            TwitchChatClient.OnBusyChange += TwitchChatClient_OnBusyChange;
            TwitchChatClient.OnChannelMessage += TwitchChatClient_OnChannelMessage;
            TwitchChatClient.OnTopic += TwitchChatClient_OnTopic;
            TwitchChatClient.OnUserCountChange += TwitchChatClient_OnUserCountChange;
            TwitchChatClient.OnModeChange += TwitchChatClient_OnModeChange;
            TwitchChatClient.OnQueryMessage += TwitchChatClient_OnQueryMessage;

            if (!TwitchChatListen.TwitchChatClient.IsValidChannelFormat(chatChannelName))
            {
                MessageBox.Show("チャンネル名の書式が間違っています。");
                return;
            }
            // チャットに接続
            TwitchChatClient.Start(new string[] { chatChannelName });
        }

        /// <summary>
        /// Twitchチャットを停止する
        /// </summary>
        private void TwitchChatClientStop()
        {
            if (TwitchChatClient != null)
            {
                // チャット切断
                TwitchChatClient.Stop();
            }
        }

        ///////////////////////////////////////////////////////////////
        // Twitchチャットクライアントイベント
        ///////////////////////////////////////////////////////////////
        /// <summary>
        /// 終了通知コールバック
        /// </summary>
        /// <param name="sender"></param>
        private void TwitchChatClient_OnStop(TwitchChatClient sender)
        {
            System.Diagnostics.Debug.WriteLine("TwitchChatClient_OnStop...");

            System.Diagnostics.Debug.WriteLine("TwitchChatClient_OnStop...done.");
        }

        /// <summary>
        /// 処理中フラグ変更通知コールバック
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="isBusy"></param>
        private void TwitchChatClient_OnBusyChange(TwitchChatClient sender, bool isBusy)
        {
            UpdateBtn.IsEnabled = !isBusy;
        }

        /// <summary>
        /// チャンネルメッセージ受信イベントハンドラ
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="channelName">チャンネル名</param>
        /// <param name="nickName">ニックネーム</param>
        /// <param name="host">ホスト</param>
        /// <param name="message">チャンネルメッセージ</param>
        private void TwitchChatClient_OnChannelMessage(
            TwitchChatClient sender, string channelName, string nickName, string host, string message)
        {
            string date = DateTime.Now.ToString("HH:mm"); // 現在時刻
            /*
            ChannelUser channelUser;

            channelUser = TwitchChatClient.GetChannelUser(channelName, nickName);
            if (channelUser == null)
            {
                return;
            }
            */


            // 配信者判定
            bool isBc = TwitchChatClient.IsBc(channelName, nickName);

            // コメントの追加
            UiCommentData uiCommentData = new UiCommentData();
            uiCommentData.UserThumbUrl = "";
            uiCommentData.UserName = nickName;
            uiCommentData.CommentStr = message;

            System.Diagnostics.Debug.WriteLine("UserThumbUrl " + uiCommentData.UserThumbUrl);
            System.Diagnostics.Debug.WriteLine("UserName " + uiCommentData.UserName);
            System.Diagnostics.Debug.WriteLine("CommentStr " + uiCommentData.CommentStr);

            ViewModel viewModel = this.DataContext as ViewModel;
            ObservableCollection<UiCommentData> uiCommentDatas = viewModel.UiCommentDataCollection;
            uiCommentDatas.Add(uiCommentData);

            // データグリッドを自動スクロール
            DataGridScrollToEnd();

            // コメントログを記録
            WriteLog(uiCommentData.UserName, uiCommentData.CommentStr);

            // 棒読みちゃんへ送信
            BouyomiChan.Talk(uiCommentData.CommentStr);
        }

        /// <summary>
        /// トピックイベントハンドラ
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="channelName">チャンネル名</param>
        /// <param name="who">トピック変更ユーザーニックネーム</param>
        /// <param name="topic">トピック</param>
        private void TwitchChatClient_OnTopic(TwitchChatClient sender, string channelName, string who, string topic)
        {
            ChannelInfoEx[] channelInfoExs = TwitchChatClient.GetChannelInfoExsByJoined(true);
            if (channelInfoExs != null && channelInfoExs.Length > 0)
            {
                if (channelName == channelInfoExs[0].RealName)  // アリーナのみ対象
                {
                    // トピックの更新
                    SetTopic(channelName, topic);
                }
            }
        }

        /// <summary>
        /// ユーザー数変更イベントハンドラ
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="channelName">チャンネル名</param>
        private void TwitchChatClient_OnUserCountChange(TwitchChatClient sender, string channelName)
        {
            UpdateUserCntText();
        }

        /// <summary>
        /// モード変更イベントイベントハンドラ
        /// </summary>
        /// <param name="sender"></param>
        private void TwitchChatClient_OnModeChange(TwitchChatClient sender)
        {
            UpdateModeText();
        }

        /////////////////////////////////////////////////////////////////////////////////////////////////////
        // トーク
        /////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>
        /// トークのメッセージを受信した
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="nickName"></param>
        /// <param name="host"></param>
        /// <param name="message"></param>
        private void TwitchChatClient_OnQueryMessage(TwitchChatClient sender, string nickName, string host, string message)
        {
            /*
            TalkForm talkForm = talkForm_Show(nickName, host);
            talkForm.UpdateMessage(message);
            */
        }

        ////////////////////////////////////////////////////////////////////
        // コメント送信
        ////////////////////////////////////////////////////////////////////
        /// <summary>
        /// コメント送信
        /// </summary>
        private void SendComment()
        {
            if (CommentTextBox.Text.Length > 0)
            {
                string channelName;
                string message = CommentTextBox.Text;
                ChannelInfoEx[] channelInfoExs = TwitchChatClient.GetChannelInfoExsByJoined(true);
                if (channelInfoExs != null && channelInfoExs.Length > 0)
                {
                    channelName = channelInfoExs[0].RealName;
                    TwitchChatClient.SendPrivMsg(channelName, message);
                }
                else
                {
                    MessageBox.Show("チャンネルが見つかりません。");
                }

                CommentTextBox.Text = "";
            }
        }

        private void SettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            var window = new SettingsWindow();
            window.Owner = this;
            window.AccountName = AccountName;
            window.Password = ChatPassword;
            window.ShowDialog();
            AccountName = window.AccountName;
            ChatPassword = window.Password;
        }
    }
}
